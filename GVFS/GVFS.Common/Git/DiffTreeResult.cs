using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Common.Git
{
    public class DiffTreeResult
    {
        public const string TreeMarker = "tree ";
        public const string BlobMarker = "blob ";

        public const int TypeMarkerStartIndex = 7;

        private const ushort SymLinkFileIndexEntry = 0xA000;

        private static readonly HashSet<string> ValidTreeModes = new HashSet<string>() { "040000" };

        public enum Operations
        {
            Unknown,
            CopyEdit,
            RenameEdit,
            Modify,
            Delete,
            Add,
            Unmerged,
            TypeChange,
        }

        public Operations Operation { get; set; }
        public bool SourceIsDirectory { get; set; }
        public bool TargetIsDirectory { get; set; }
        public bool TargetIsSymLink { get; set; }
        public string TargetPath { get; set; }
        public string SourceSha { get; set; }
        public string TargetSha { get; set; }
        public ushort SourceMode { get; set; }
        public ushort TargetMode { get; set; }

        public static DiffTreeResult ParseFromDiffTreeLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                throw new ArgumentException("Line to parse cannot be null or empty", nameof(line));
            }

            /*
             * The lines passed to this method should be the result of a call to git diff-tree -r -t (sourceTreeish) (targetTreeish)
             *
             * Example output lines from git diff-tree
             * :000000 040000 0000000000000000000000000000000000000000 cee82f9d431bf610404f67bcdda3fee76f0c1dd5 A\tGVFS/FastFetch/Git
             * :000000 100644 0000000000000000000000000000000000000000 cdc036f9d561f14d908e0a0c337105b53c778e5e A\tGVFS/FastFetch/Git/FastFetchGitObjects.cs
             * :040000 000000 f68b90da732791438d67c0326997a2d26e4c2de4 0000000000000000000000000000000000000000 D\tGVFS/GVFS.CLI
             * :100644 000000 1242fc97c612ff286a5f1221d569508600ca5e06 0000000000000000000000000000000000000000 D\tGVFS/GVFS.CLI/GVFS.CLI.csproj
             * :040000 040000 3823348f91113a619eed8f48fe597cc9c7d088d8 fd56ff77b12a0b76567cb55ed4950272eac8b8f6 M\tGVFS/GVFS.Common
             * :100644 100644 57d9c737c8a48632cfbb12cae00c97d512b9f155 524d7dbcebd33e4007c52711d3f21b17373de454 M\tGVFS/GVFS.Common/GVFS.Common.csproj
             *  ^-[0]  ^-[1]  ^-[2]                                    ^-[3]                                    ^-[4]
             *                                                                                                   ^-tab
             *                                                                                                     ^-[5]
             *
             * This output will only happen if -C or -M is passed to the diff-tree command
             * Since we are not passing those options we shouldn't have to handle this format.
             * :100644 100644 3ac7d60a25bb772af1d5843c76e8a070c062dc5d c31a95125b8a6efd401488839a7ed1288ce01634 R094\tGVFS/GVFS.CLI/CommandLine/CloneVerb.cs\tGVFS/GVFS/CommandLine/CloneVerb.cs
             */

            if (!line.StartsWith(":"))
            {
                throw new ArgumentException($"diff-tree lines should start with a :", nameof(line));
            }

            // Skip the colon at the front
            line = line.Substring(1);

            // Filenames may contain spaces, but always follow a \t. Other fields are space delimited.
            // Splitting on \t will give us the mode, sha, operation in parts[0] and that path in parts[1] and optionally in paths[2]
            string[] parts = line.Split(new[] { '\t' }, count: 2);

            // Take the mode, sha, operation part and split on a space then add the paths that were split on a tab to the end
            parts = parts[0].Split(' ').Concat(parts.Skip(1)).ToArray();

            if (parts.Length != 6 ||
                parts[5].Contains('\t'))
            {
                // Look at file history to see how -C -M with 7 parts could be handled
                throw new ArgumentException($"diff-tree lines should have 6 parts unless passed -C or -M which this method doesn't handle", nameof(line));
            }

            DiffTreeResult result = new DiffTreeResult();
            result.SourceIsDirectory = ValidTreeModes.Contains(parts[0]);
            result.TargetIsDirectory = ValidTreeModes.Contains(parts[1]);
            result.SourceMode = Convert.ToUInt16(parts[0], 8);
            result.TargetMode = Convert.ToUInt16(parts[1], 8);

            if (!result.TargetIsDirectory)
            {
                result.TargetIsSymLink = result.TargetMode == SymLinkFileIndexEntry;
            }

            result.SourceSha = parts[2];
            result.TargetSha = parts[3];
            result.Operation = DiffTreeResult.ParseOperation(parts[4]);
            result.TargetPath = ConvertPathToUtf8Path(parts[5]);
            if (result.TargetIsDirectory || result.SourceIsDirectory)
            {
                // Since diff-tree is not doing rename detection, file->directory or directory->file transformations are always multiple lines
                // with a delete line and an add line
                // :000000 040000 0000000000000000000000000000000000000000 cee82f9d431bf610404f67bcdda3fee76f0c1dd5 A\tGVFS/FastFetch/Git
                // :040000 040000 3823348f91113a619eed8f48fe597cc9c7d088d8 fd56ff77b12a0b76567cb55ed4950272eac8b8f6 M\tGVFS/GVFS.Common
                // :040000 000000 f68b90da732791438d67c0326997a2d26e4c2de4 0000000000000000000000000000000000000000 D\tGVFS/GVFS.CLI
                result.TargetPath = AppendPathSeparatorIfNeeded(result.TargetPath);
            }

            return result;
        }

        /// <summary>
        /// Parse the output of calling git ls-tree
        /// </summary>
        /// <param name="line">A line that was output from calling git ls-tree</param>
        /// <returns>A DiffTreeResult build from the output line</returns>
        /// <remarks>
        /// The call to ls-tree could be any of the following
        /// git ls-tree (treeish)
        /// git ls-tree -r (treeish)
        /// git ls-tree -t (treeish)
        /// git ls-tree -r -t (treeish)
        /// </remarks>
        public static DiffTreeResult ParseFromLsTreeLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                throw new ArgumentException("Line to parse cannot be null or empty", nameof(line));
            }

            /*
             * Example output lines from ls-tree
             *
             * 040000 tree 73b881d52b607b0f3e9e620d36f556d3d233a11d\tGVFS
             * 100644 blob 44c5f5cba4b29d31c2ad06eed51ea02af76c27c0\tReadme.md
             * 100755 blob 196142fbb753c0a3c7c6690323db7aa0a11f41ec\tScripts/BuildGVFSForMac.sh
             * ^-mode ^-marker                                     ^-tab
             *             ^-sha                                     ^-path
             */

            // Everything from ls-tree is an add.
            if (IsLsTreeLineOfType(line, TreeMarker))
            {
                DiffTreeResult treeAdd = new DiffTreeResult();
                treeAdd.TargetIsDirectory = true;
                treeAdd.TargetPath = AppendPathSeparatorIfNeeded(ConvertPathToUtf8Path(line.Substring(line.LastIndexOf("\t") + 1)));
                treeAdd.Operation = DiffTreeResult.Operations.Add;

                return treeAdd;
            }
            else
            {
                if (IsLsTreeLineOfType(line, BlobMarker))
                {
                    DiffTreeResult blobAdd = new DiffTreeResult();
                    blobAdd.TargetMode = Convert.ToUInt16(line.Substring(0, 6), 8);
                    blobAdd.TargetIsSymLink = blobAdd.TargetMode == SymLinkFileIndexEntry;
                    blobAdd.TargetSha = line.Substring(TypeMarkerStartIndex + BlobMarker.Length, GVFSConstants.ShaStringLength);
                    blobAdd.TargetPath = ConvertPathToUtf8Path(line.Substring(line.LastIndexOf("\t") + 1));
                    blobAdd.Operation = DiffTreeResult.Operations.Add;

                    return blobAdd;
                }
                else
                {
                    return null;
                }
            }
        }

        public static bool IsLsTreeLineOfType(string line, string typeMarker)
        {
            if (line.Length <= TypeMarkerStartIndex + typeMarker.Length)
            {
                return false;
            }

            return line.IndexOf(typeMarker, TypeMarkerStartIndex, typeMarker.Length, StringComparison.OrdinalIgnoreCase) == TypeMarkerStartIndex;
        }

        private static string AppendPathSeparatorIfNeeded(string path)
        {
            return path.Last() == Path.DirectorySeparatorChar ? path : path + Path.DirectorySeparatorChar;
        }

        private static Operations ParseOperation(string gitOperationString)
        {
            switch (gitOperationString)
            {
                case "U": return Operations.Unmerged;
                case "M": return Operations.Modify;
                case "A": return Operations.Add;
                case "D": return Operations.Delete;
                case "X": return Operations.Unknown;
                case "T": return Operations.TypeChange;
                default:
                    if (gitOperationString.StartsWith("C"))
                    {
                        return Operations.CopyEdit;
                    }
                    else if (gitOperationString.StartsWith("R"))
                    {
                        return Operations.RenameEdit;
                    }

                    throw new InvalidDataException("Unrecognized diff-tree operation: " + gitOperationString);
            }
        }

        private static string ConvertPathToUtf8Path(string relativePath)
        {
            return GitPathConverter.ConvertPathOctetsToUtf8(relativePath.Trim('"')).Replace(GVFSConstants.GitPathSeparator, Path.DirectorySeparatorChar);
        }
    }
}
