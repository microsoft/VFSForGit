using GVFS.Common.NamedPipes;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common.Sparse
{
    /// <summary>
    /// Test only: makes the mount refuse cone widen and apply requests, so that the functional
    /// tests can check what the hooks do when the mount cannot prepare the sparse index.
    /// </summary>
    /// <remarks>
    /// The mount reads the <see cref="FaultFileEnvironmentVariable"/> environment variable once,
    /// when it starts. Only the functional tests set it. When it is not set, this class does
    /// nothing and the mount reads no file.
    /// <para>
    /// When it is set, the mount reads the named file at each widen and apply request, so that a
    /// test can add and remove a fault while the mount runs. Each line of the file has a request
    /// header (<see cref="NamedPipeMessages.ConeManagement.WidenRequest"/> or
    /// <see cref="NamedPipeMessages.ConeManagement.ApplyRequest"/>), a space, and the reply
    /// (<see cref="NamedPipeMessages.ConeManagement.FailureResult"/> or
    /// <see cref="NamedPipeMessages.ConeManagement.NotLockHolderResult"/>). The mount sends that
    /// reply and does not change the cone or the index. A missing file means no fault.
    /// </para>
    /// </remarks>
    public sealed class ConeRequestFaultInjection
    {
        /// <summary>The environment variable that names the fault file. Test only.</summary>
        public const string FaultFileEnvironmentVariable = "GVFS_TEST_CONE_REQUEST_FAULT_FILE";

        // A fault file is a few short lines. A larger file is not a fault file.
        private const int MaxFaultFileBytes = 4096;

        private readonly string faultFilePath;
        private readonly Func<string, string> readFile;

        private ConeRequestFaultInjection(string faultFilePath, Func<string, string> readFile)
        {
            this.faultFilePath = faultFilePath;
            this.readFile = readFile;
        }

        /// <summary>The path of the fault file.</summary>
        public string FaultFilePath => this.faultFilePath;

        /// <summary>
        /// Return the fault injection that <see cref="FaultFileEnvironmentVariable"/> configures, or
        /// null when the variable is not set.
        /// </summary>
        /// <param name="getEnvironmentVariable">Reads an environment variable.</param>
        /// <param name="readFile">Returns the content of a file, or null when it does not exist.</param>
        public static ConeRequestFaultInjection FromEnvironment(Func<string, string> getEnvironmentVariable, Func<string, string> readFile = null)
        {
            ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

            string path = getEnvironmentVariable(FaultFileEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return new ConeRequestFaultInjection(path, readFile ?? ReadSmallFile);
        }

        /// <summary>
        /// Return the reply that the fault file sets for <paramref name="requestHeader"/>, or null
        /// when the mount must handle the request. Never throws.
        /// </summary>
        public string GetForcedReply(string requestHeader)
        {
            if (requestHeader != NamedPipeMessages.ConeManagement.WidenRequest &&
                requestHeader != NamedPipeMessages.ConeManagement.ApplyRequest)
            {
                return null;
            }

            string content;
            try
            {
                content = this.readFile(this.faultFilePath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }

            return content == null ? null : ParseFaults(content).GetValueOrDefault(requestHeader);
        }

        /// <summary>
        /// Parse the content of a fault file. Lines that are not valid are ignored.
        /// </summary>
        internal static Dictionary<string, string> ParseFaults(string content)
        {
            Dictionary<string, string> faults = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string rawLine in content.Split('\n'))
            {
                string[] fields = rawLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 2)
                {
                    continue;
                }

                bool isRequest = fields[0] == NamedPipeMessages.ConeManagement.WidenRequest ||
                    fields[0] == NamedPipeMessages.ConeManagement.ApplyRequest;
                bool isReply = fields[1] == NamedPipeMessages.ConeManagement.FailureResult ||
                    fields[1] == NamedPipeMessages.ConeManagement.NotLockHolderResult;
                if (isRequest && isReply)
                {
                    faults[fields[0]] = fields[1];
                }
            }

            return faults;
        }

        private static string ReadSmallFile(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length > MaxFaultFileBytes)
            {
                return null;
            }

            return File.ReadAllText(path);
        }
    }
}
