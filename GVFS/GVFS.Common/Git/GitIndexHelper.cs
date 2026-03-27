using System;
using System.IO;

namespace GVFS.Common.Git
{
    /// <summary>
    /// Lightweight utilities for reading git index metadata without full parsing.
    /// </summary>
    public static class GitIndexHelper
    {
        private const int IndexHeaderSize = 12;
        private const int EntryCountOffset = 8;
        private const int EntryCountSize = 4;

        /// <summary>
        /// Reads the entry count from the git index file header (bytes 8-11, big-endian).
        /// This is extremely fast (~0.1ms) because it reads only 4 bytes, unlike
        /// full index parsing which must allocate and parse every entry.
        /// </summary>
        /// <param name="indexPath">Full path to the git index file (e.g. .git/index).</param>
        /// <returns>The number of entries, or -1 if the file cannot be read.</returns>
        public static int ReadEntryCount(string indexPath)
        {
            using (FileStream indexFile = new FileStream(
                indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return ReadEntryCount(indexFile);
            }
        }

        /// <summary>
        /// Reads the entry count from a git index stream (bytes 8-11, big-endian).
        /// </summary>
        /// <param name="indexStream">A readable stream positioned anywhere; will be seeked to offset 8.</param>
        /// <returns>The number of entries, or -1 if the stream is too short.</returns>
        public static int ReadEntryCount(Stream indexStream)
        {
            if (indexStream.Length < IndexHeaderSize)
            {
                return -1;
            }

            indexStream.Position = EntryCountOffset;
            byte[] bytes = new byte[EntryCountSize];
            int bytesRead = indexStream.Read(bytes, 0, EntryCountSize);
            if (bytesRead < EntryCountSize)
            {
                return -1;
            }

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToInt32(bytes, 0);
        }
    }
}
