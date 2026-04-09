using GVFS.Common.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace GVFS.Common.Git
{
    public class GitIndexGenerator
    {
        private const long EntryCountOffset = 8;

        private const ushort ExtendedBit = 0x4000;
        private const ushort SkipWorktreeBit = 0x4000;

        private static readonly byte[] PaddingBytes = new byte[8];

        private static readonly byte[] IndexHeader = new byte[]
        {
            (byte)'D', (byte)'I', (byte)'R', (byte)'C', // Magic Signature
        };

        // We can't accurated fill times and length in realtime, so we block write the zeroes and probably save time.
        private static readonly byte[] EntryHeader = new byte[]
        {
            0, 0, 0, 0,
            0, 0, 0, 0, // ctime
            0, 0, 0, 0,
            0, 0, 0, 0, // mtime
            0, 0, 0, 0, // stat(2) dev
            0, 0, 0, 0, // stat(2) ino
            0, 0, 0x81, 0xA4, // filemode (0x81A4 in little endian)
            0, 0, 0, 0, // stat(2) uid
            0, 0, 0, 0, // stat(2) gid
            0, 0, 0, 0  // file length
        };

        private readonly string indexLockPath;

        private Enlistment enlistment;
        private ITracer tracer;
        private bool shouldHashIndex;

        private uint entryCount = 0;

        private BlockingCollection<LsTreeEntry> entryQueue = new BlockingCollection<LsTreeEntry>();

        public GitIndexGenerator(ITracer tracer, Enlistment enlistment, bool shouldHashIndex)
        {
            this.tracer = tracer;
            this.enlistment = enlistment;
            this.shouldHashIndex = shouldHashIndex;

            // The extension 'lock2' is chosen simply to not be '.lock' because, although this class reasonably
            // conforms to how index.lock is supposed to be used, its callers continue to do things to the tree
            // and the working tree and even the before this class comes along and after this class has been released.
            // FastFetch.IndexLock bodges around this by creating an empty file in the index.lock position, so we
            // need to create a different file.  See FastFetch.IndexLock for a proposed design to fix this.
            //
            // Note that there are two callers of this - one is from FastFetch, which we just discussed, and the
            // other is from the 'gvfs repair' verb.  That environment is special in that it only runs on unmounted
            // repo's, so 'index.lock' is irrelevant as a locking mechanism in that context.  There can't be git
            // commands to lock out.
            this.indexLockPath = Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName + ".lock2");
        }

        public string TemporaryIndexFilePath => this.indexLockPath;

        public bool HasFailures { get; private set; }

        /// <summary>Builds an index from scratch based on the current head pointer.</summary>
        /// <param name="indexVersion">The index version see https://git-scm.com/docs/index-format for details on what this means.</param>
        /// <param name="isFinal">
        /// If true, the index file will be written during this operation.  If not, the new index will be
        /// left in <see cref="TemporaryIndexFilePath"/>.
        /// </param>
        /// <remarks>
        /// The index created by this class has no data from the working tree, so when 'git status' is run, it
        /// will calculate the hash of everything in the working tree.
        /// </remarks>
        public void CreateFromRef(string refName, uint indexVersion, bool isFinal)
        {
            using (ITracer updateIndexActivity = this.tracer.StartActivity("CreateFromHeadTree", EventLevel.Informational))
            {
                Thread entryWritingThread = new Thread(() => this.WriteAllEntries(indexVersion, isFinal));
                entryWritingThread.Start();

                GitProcess git = new GitProcess(this.enlistment);
                GitProcess.Result result = git.LsTree(
                    refName,
                    this.EnqueueEntriesFromLsTree,
                    recursive: true,
                    showAllTrees: false);

                if (result.ExitCodeIsFailure)
                {
                    this.tracer.RelatedError("LsTree failed during index generation: {0}", result.Errors);
                    this.HasFailures = true;
                }

                this.entryQueue.CompleteAdding();
                entryWritingThread.Join();
            }
        }

        private void EnqueueEntriesFromLsTree(string line)
        {
            LsTreeEntry entry = LsTreeEntry.ParseFromLsTreeLine(line);
            if (entry != null)
            {
                this.entryQueue.Add(entry);
            }
        }

        private void WriteAllEntries(uint version, bool isFinal)
        {
            try
            {
                using (Stream indexStream = new FileStream(this.indexLockPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (BinaryWriter writer = new BinaryWriter(indexStream))
                {
                    writer.Write(IndexHeader);
                    writer.Write(EndianHelper.Swap(version));
                    writer.Write((uint)0); // Number of entries placeholder

                    uint lastStringLength = 0;
                    LsTreeEntry entry;
                    while (this.entryQueue.TryTake(out entry, Timeout.Infinite))
                    {
                        this.WriteEntry(writer, version, entry.Sha, entry.Filename, ref lastStringLength);
                    }

                    // Update entry count
                    writer.BaseStream.Position = EntryCountOffset;
                    writer.Write(EndianHelper.Swap(this.entryCount));
                    writer.Flush();
                }

                this.AppendIndexSha();
                if (isFinal)
                {
                    this.ReplaceExistingIndex();
                }
            }
            catch (Exception e)
            {
                this.tracer.RelatedError("Failed to generate index: {0}", e.ToString());
                this.HasFailures = true;
            }
        }

        private void WriteEntry(BinaryWriter writer, uint version, string sha, string filename, ref uint lastStringLength)
        {
            long startPosition = writer.BaseStream.Position;

            this.entryCount++;

            writer.Write(EntryHeader, 0, EntryHeader.Length);

            writer.Write(SHA1Util.BytesFromHexString(sha));

            byte[] filenameBytes = Encoding.UTF8.GetBytes(filename);

            ushort flags = (ushort)(filenameBytes.Length & 0xFFF);
            writer.Write(EndianHelper.Swap(flags));

            if (version >= 4)
            {
                this.WriteReplaceLength(writer, lastStringLength);
                lastStringLength = (uint)filenameBytes.Length;
            }

            writer.Write(filenameBytes);

            writer.Flush();
            long endPosition = writer.BaseStream.Position;

            // Version 4 requires a nul-terminated string.
            int numPaddingBytes = 1;
            if (version < 4)
            {
                // Version 2-3 has between 1 and 8 padding bytes including nul-terminator.
                numPaddingBytes = 8 - ((int)(endPosition - startPosition) % 8);
                if (numPaddingBytes == 0)
                {
                    numPaddingBytes = 8;
                }
            }

            writer.Write(PaddingBytes, 0, numPaddingBytes);

            writer.Flush();
        }

        private void WriteReplaceLength(BinaryWriter writer, uint value)
        {
            List<byte> bytes = new List<byte>();
            do
            {
                byte nextByte = (byte)(value & 0x7F);
                value = value >> 7;
                bytes.Add(nextByte);
            }
            while (value != 0);

            bytes.Reverse();
            for (int i = 0; i < bytes.Count; ++i)
            {
                byte toWrite = bytes[i];
                if (i < bytes.Count - 1)
                {
                    toWrite -= 1;
                    toWrite |= 0x80;
                }

                writer.Write(toWrite);
            }
        }

        private void AppendIndexSha()
        {
            byte[] sha = this.GetIndexHash();

            using (Stream indexStream = new FileStream(this.indexLockPath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                indexStream.Seek(0, SeekOrigin.End);
                indexStream.Write(sha, 0, sha.Length);
            }
        }

        private byte[] GetIndexHash()
        {
            if (this.shouldHashIndex)
            {
                using (Stream fileStream = new FileStream(this.indexLockPath, FileMode.Open, FileAccess.Read, FileShare.Write))
                using (HashingStream hasher = new HashingStream(fileStream))
                {
                    hasher.CopyTo(Stream.Null);
                    return hasher.Hash;
                }
            }

            return new byte[20];
        }

        private void ReplaceExistingIndex()
        {
            string indexPath = Path.Combine(this.enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName);
            File.Delete(indexPath);
            File.Move(this.indexLockPath, indexPath);
        }

        private class LsTreeEntry
        {
            public LsTreeEntry()
            {
                this.Filename = string.Empty;
            }

            public string Filename { get; private set; }
            public string Sha { get; private set; }

            public static LsTreeEntry ParseFromLsTreeLine(string line)
            {
                if (DiffTreeResult.IsLsTreeLineOfType(line, DiffTreeResult.BlobMarker))
                {
                    LsTreeEntry blobEntry = new LsTreeEntry();
                    blobEntry.Sha = line.Substring(DiffTreeResult.TypeMarkerStartIndex + DiffTreeResult.BlobMarker.Length, GVFSConstants.ShaStringLength);
                    blobEntry.Filename = GitPathConverter.ConvertPathOctetsToUtf8(line.Substring(line.LastIndexOf("\t") + 1).Trim('"'));

                    return blobEntry;
                }

                return null;
            }
        }
    }
}
