using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using GVFS.Common.FileSystem;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Serializes a <see cref="ConePatternSet"/> to git's cone-mode
    /// <c>info/sparse-checkout</c> format and writes it atomically.
    /// </summary>
    /// <remarks>
    /// The serialization mirrors git's <c>write_cone_to_file</c>
    /// (<c>builtin/sparse-checkout.c</c>): a root include line, a root directory exclude line,
    /// a positive and negative pair for each parent-only directory, and a single positive
    /// line for each recursive directory. Glob-special characters are escaped as git's
    /// <c>escaped_pattern</c> does. The file is UTF-8 without a byte order mark, and lines end
    /// with <c>\n</c>, as in git's own writer.
    /// <para>
    /// The writer does not lock the file. The caller must make sure that no other writer,
    /// including a git command, changes the file between <see cref="Write"/>, the use of the
    /// new cone, and a possible <see cref="TryRestoreBackup"/>.
    /// </para>
    /// <para>
    /// A git process that reads the file keeps it open without delete sharing, so the rename
    /// over the file can fail with <c>ERROR_ACCESS_DENIED</c> or
    /// <c>ERROR_SHARING_VIOLATION</c> for a short time. The writer then tries the replace
    /// again with a short backoff, as git's <c>mingw_rename</c> does. The caller sets the
    /// retry budget.
    /// </para>
    /// </remarks>
    public class ConeFileWriter
    {
        /// <summary>
        /// Suffix for the backup of the previous sparse-checkout file, which a caller can use
        /// to roll back when it cannot apply the new cone.
        /// </summary>
        public const string BackupExtension = ".gvfsbackup";

        /// <summary>
        /// The total time that the writer waits between attempts to replace the file when the
        /// caller does not give a retry budget.
        /// </summary>
        public static readonly TimeSpan DefaultReplaceRetryBudget = TimeSpan.FromMilliseconds(100);

        private const string LineEnding = "\n";
        private const string ConeHeader = "/*" + LineEnding + "!/*/" + LineEnding;

        private const int ErrorAccessDenied = 5;
        private const int ErrorSharingViolation = 32;
        private const uint Win32HResultFacilityMask = 0xFFFF0000;
        private const uint Win32HResultFacility = 0x80070000;

        // The same delays as git's retry_ask_yes_no in compat/mingw.c. After the last delay,
        // the writer keeps the last delay until the budget is spent.
        private static readonly int[] ReplaceRetryDelaysMilliseconds = { 0, 1, 10, 20, 40 };

        // PhysicalFileSystem.TryWriteTempFileAndRename writes through a StreamWriter, which
        // uses UTF-8 without a byte order mark.
        private static readonly UTF8Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly PhysicalFileSystem fileSystem;
        private readonly Action<TimeSpan> sleep;

        public ConeFileWriter(PhysicalFileSystem fileSystem)
            : this(fileSystem, Thread.Sleep)
        {
        }

        internal ConeFileWriter(PhysicalFileSystem fileSystem, Action<TimeSpan> sleep)
        {
            ArgumentNullException.ThrowIfNull(fileSystem);
            ArgumentNullException.ThrowIfNull(sleep);
            this.fileSystem = fileSystem;
            this.sleep = sleep;
        }

        private delegate bool ReplaceOperation(out Exception handledException);

        /// <summary>
        /// Serialize a cone pattern set to git's cone-mode sparse-checkout file content.
        /// </summary>
        public static string Serialize(ConePatternSet cone)
        {
            ArgumentNullException.ThrowIfNull(cone);

            StringBuilder builder = new StringBuilder();
            builder.Append(ConeHeader);

            foreach (string parentOnlyDirectory in cone.ParentOnlyDirectories)
            {
                string escaped = EscapePattern(parentOnlyDirectory);
                builder.Append(GVFSConstants.GitPathSeparator).Append(escaped).Append(GVFSConstants.GitPathSeparator).Append(LineEnding);
                builder.Append('!').Append(GVFSConstants.GitPathSeparator).Append(escaped).Append("/*/").Append(LineEnding);
            }

            foreach (string recursiveDirectory in cone.RecursiveDirectories)
            {
                string escaped = EscapePattern(recursiveDirectory);
                builder.Append(GVFSConstants.GitPathSeparator).Append(escaped).Append(GVFSConstants.GitPathSeparator).Append(LineEnding);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Escape glob-special characters (<c>*</c>, <c>?</c>, <c>[</c>, <c>\</c>) with a
        /// leading backslash, as git's <c>escaped_pattern</c> does.
        /// </summary>
        internal static string EscapePattern(string pattern)
        {
            ArgumentNullException.ThrowIfNull(pattern);

            StringBuilder escaped = new StringBuilder(pattern.Length);
            foreach (char character in pattern)
            {
                if (IsGlobSpecial(character))
                {
                    escaped.Append('\\');
                }

                escaped.Append(character);
            }

            return escaped.ToString();
        }

        /// <summary>
        /// Write the cone file atomically: write a temporary file, then rename it over the
        /// target.
        /// </summary>
        /// <param name="sparseCheckoutPath">The full path of the sparse-checkout file.</param>
        /// <param name="cone">The cone to write.</param>
        /// <param name="backupPath">
        /// When the method replaced an existing file, the path of a backup of its previous
        /// content. Otherwise null. If the method fails after it writes the backup, the backup
        /// path is still set. Restore only the backup path that this call returns. Each write
        /// replaces the backup of the previous write, and the method does not delete an old
        /// backup. The caller deletes the backup when it no longer needs it.
        /// </param>
        /// <param name="handledException">The exception that made the write fail, or null.</param>
        /// <param name="replaceRetryBudget">
        /// The total time to wait between attempts when the rename over the target fails with
        /// access denied or a sharing violation. Zero makes one attempt only. Null uses
        /// <see cref="DefaultReplaceRetryBudget"/>. The budget does not include the time of
        /// the attempts.
        /// </param>
        /// <returns>
        /// <see cref="ConeFileWriteResult.Unchanged"/> when the file already has exactly the
        /// serialized bytes. The method then writes nothing, not even a backup.
        /// <see cref="ConeFileWriteResult.Written"/> when the method wrote the new content.
        /// <see cref="ConeFileWriteResult.Failed"/> when the read, the backup, or the write
        /// failed. A failed write leaves the target file with its previous content.
        /// </returns>
        public ConeFileWriteResult Write(
            string sparseCheckoutPath,
            ConePatternSet cone,
            out string backupPath,
            out Exception handledException,
            TimeSpan? replaceRetryBudget = null)
        {
            ArgumentNullException.ThrowIfNull(sparseCheckoutPath);
            ArgumentNullException.ThrowIfNull(cone);
            TimeSpan retryBudget = GetRetryBudget(replaceRetryBudget);

            backupPath = null;
            handledException = null;

            string content = Serialize(cone);

            try
            {
                // Read the file instead of asking whether it exists. File.Exists also returns
                // false when access is denied, and the write must not then replace the file
                // without a backup.
                byte[] existingBytes = this.ReadExistingFileOrNull(sparseCheckoutPath);
                if (existingBytes != null)
                {
                    // Compare bytes, not decoded text: git reads bytes, so the same text in
                    // another encoding is a different file to git.
                    if (existingBytes.AsSpan().SequenceEqual(FileEncoding.GetBytes(content)))
                    {
                        return ConeFileWriteResult.Unchanged;
                    }

                    string candidateBackupPath = sparseCheckoutPath + BackupExtension;
                    if (!this.fileSystem.TryCopyToTempFileAndRename(sparseCheckoutPath, candidateBackupPath, out handledException))
                    {
                        return ConeFileWriteResult.Failed;
                    }

                    backupPath = candidateBackupPath;
                }

                if (!this.TryReplaceWithRetry(
                        (out Exception e) => this.fileSystem.TryWriteTempFileAndRename(sparseCheckoutPath, content, out e),
                        retryBudget,
                        out handledException))
                {
                    return ConeFileWriteResult.Failed;
                }
            }
            catch (IOException e)
            {
                handledException = e;
                return ConeFileWriteResult.Failed;
            }
            catch (UnauthorizedAccessException e)
            {
                handledException = e;
                return ConeFileWriteResult.Failed;
            }

            return ConeFileWriteResult.Written;
        }

        /// <summary>
        /// Restore the sparse-checkout file from a backup written by <see cref="Write"/>. The
        /// method copies the backup bytes exactly. The rename over the target uses the same
        /// retry as <see cref="Write"/>.
        /// </summary>
        public bool TryRestoreBackup(
            string sparseCheckoutPath,
            string backupPath,
            out Exception handledException,
            TimeSpan? replaceRetryBudget = null)
        {
            ArgumentNullException.ThrowIfNull(sparseCheckoutPath);
            ArgumentNullException.ThrowIfNull(backupPath);
            TimeSpan retryBudget = GetRetryBudget(replaceRetryBudget);

            return this.TryReplaceWithRetry(
                (out Exception e) => this.fileSystem.TryCopyToTempFileAndRename(backupPath, sparseCheckoutPath, out e),
                retryBudget,
                out handledException);
        }

        /// <summary>
        /// Return true for the failures that a short wait can fix: access denied and sharing
        /// violation. .NET reports these as a <see cref="Win32Exception"/> from
        /// <c>MoveFileEx</c>, as an <see cref="UnauthorizedAccessException"/>, or as an
        /// <see cref="IOException"/> with the Win32 HRESULT.
        /// </summary>
        internal static bool IsTransientReplaceFailure(Exception exception)
        {
            switch (exception)
            {
                case Win32Exception win32Exception:
                    return IsTransientWin32Error(win32Exception.NativeErrorCode);
                case UnauthorizedAccessException:
                    return true;
                case IOException ioException:
                    uint hresult = unchecked((uint)ioException.HResult);
                    return (hresult & Win32HResultFacilityMask) == Win32HResultFacility
                        && IsTransientWin32Error((int)(hresult & 0xFFFF));
                default:
                    return false;
            }
        }

        private static bool IsTransientWin32Error(int error)
        {
            return error == ErrorAccessDenied || error == ErrorSharingViolation;
        }

        private static TimeSpan GetRetryBudget(TimeSpan? replaceRetryBudget)
        {
            TimeSpan budget = replaceRetryBudget ?? DefaultReplaceRetryBudget;
            if (budget < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(replaceRetryBudget), budget, "The retry budget must not be negative.");
            }

            return budget;
        }

        private bool TryReplaceWithRetry(ReplaceOperation replace, TimeSpan retryBudget, out Exception handledException)
        {
            TimeSpan waited = TimeSpan.Zero;
            for (int attempt = 0; ; attempt++)
            {
                if (replace(out handledException))
                {
                    return true;
                }

                if (retryBudget == TimeSpan.Zero || !IsTransientReplaceFailure(handledException))
                {
                    return false;
                }

                int delayIndex = Math.Min(attempt, ReplaceRetryDelaysMilliseconds.Length - 1);
                TimeSpan delay = TimeSpan.FromMilliseconds(ReplaceRetryDelaysMilliseconds[delayIndex]);

                // waited never exceeds retryBudget, so the subtraction cannot overflow.
                if (delay > retryBudget - waited)
                {
                    return false;
                }

                this.sleep(delay);
                waited += delay;
            }
        }

        private static bool IsGlobSpecial(char character)
        {
            return character == '*' || character == '?' || character == '[' || character == '\\';
        }

        /// <summary>
        /// Return the file's bytes, or null when the file or its directory does not exist.
        /// Any other failure throws.
        /// </summary>
        private byte[] ReadExistingFileOrNull(string path)
        {
            try
            {
                return this.fileSystem.ReadAllBytes(path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }
    }
}
