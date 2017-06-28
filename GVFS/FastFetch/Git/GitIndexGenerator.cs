using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Physical.Git;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace FastFetch.Git
{
    public class GitIndexGenerator
    {
        private const long EntryCountOffset = 8;
        
        private static readonly byte[] PaddingBytes = new byte[8];

        private static readonly byte[] IndexHeader = new byte[]
        {
            (byte)'D', (byte)'I', (byte)'R', (byte)'C', // Magic Signature
            0, 0, 0, 2, // Version
            0, 0, 0, 0  // Number of Entries
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
            
            this.indexLockPath = Path.Combine(enlistment.DotGitRoot, GVFSConstants.DotGit.IndexName + GVFSConstants.DotGit.LockExtension);
        }

        public bool HasFailures { get; private set; }

        public void CreateFromHeadTree()
        {
            using (ITracer updateIndexActivity = this.tracer.StartActivity("CreateFromHeadTree", EventLevel.Informational))
            {
                Thread entryWritingThread = new Thread(this.WriteAllEntries);
                entryWritingThread.Start();

                GitProcess git = new GitProcess(this.enlistment);
                GitProcess.Result result = git.LsTree(
                    GVFSConstants.DotGit.HeadName,
                    this.EnqueueEntriesFromLsTree,
                    recursive: true,
                    showAllTrees: false);

                if (result.HasErrors)
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

        private void WriteAllEntries()
        {
            try
            {
                using (Stream indexStream = new FileStream(this.indexLockPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (BinaryWriter writer = new BinaryWriter(indexStream))
                {
                    writer.Write(IndexHeader);

                    LsTreeEntry entry;
                    while (this.entryQueue.TryTake(out entry, millisecondsTimeout: -1))
                    {
                        this.WriteEntry(writer, entry.Sha, entry.Filename);
                    }

                    // Update entry count
                    writer.BaseStream.Position = EntryCountOffset;
                    writer.Write(EndianHelper.Swap(this.entryCount));
                    writer.Flush();
                }

                this.AppendIndexSha();
                this.ReplaceExistingIndex();
            }
            catch (Exception e)
            {
                this.tracer.RelatedError("Failed to generate index: {0}", e.ToString());
                this.HasFailures = true;
            }
        }

        private void WriteEntry(BinaryWriter writer, string sha, string filename)
        {
            this.entryCount++;
            
            writer.Write(EntryHeader, 0, EntryHeader.Length);

            writer.Write(SHA1Util.BytesFromHexString(sha));

            byte[] filenameBytes = Encoding.UTF8.GetBytes(filename);
            writer.Write(EndianHelper.Swap((ushort)(filenameBytes.Length & 0xFFF)));

            writer.Write(filenameBytes);
            
            const long EntryLengthWithoutFilename = 62;

            // Between 1 and 8 padding bytes.
            int numPaddingBytes = 8 - ((int)(EntryLengthWithoutFilename + filenameBytes.Length) % 8);
            if (numPaddingBytes == 0)
            {
                numPaddingBytes = 8;
            }

            writer.Write(PaddingBytes, 0, numPaddingBytes);
            writer.Flush();
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
            public string Filename { get; private set; }
            public string Sha { get; private set; }

            public static LsTreeEntry ParseFromLsTreeLine(string line)
            {
                int blobIndex = line.IndexOf(DiffTreeResult.BlobMarker);
                if (blobIndex >= 0)
                {
                    LsTreeEntry blobEntry = new LsTreeEntry();
                    blobEntry.Sha = line.Substring(blobIndex + DiffTreeResult.BlobMarker.Length, GVFSConstants.ShaStringLength);
                    blobEntry.Filename = GitPathConverter.ConvertPathOctetsToUtf8(line.Substring(line.LastIndexOf("\t") + 1).Trim('"'));

                    return blobEntry;
                }
                
                return null;
            }
        }
    }
}
