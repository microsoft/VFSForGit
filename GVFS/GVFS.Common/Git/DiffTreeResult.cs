using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.Common.Git
{
    public class DiffTreeResult
    {
        private static readonly HashSet<string> ValidTreeModes = new HashSet<string>() { "040000" };

        public enum Operations
        {
            Unknown,
            CopyEdit,
            RenameEdit,
            Modify,
            Delete,
            Add,
            Unmerged
        }

        public Operations Operation { get; set; }
        public bool SourceIsDirectory { get; set; }
        public bool TargetIsDirectory { get; set; }
        public string SourceFilename { get; set; }
        public string TargetFilename { get; set; }
        public string SourceSha { get; set; }
        public string TargetSha { get; set; }

        public static DiffTreeResult ParseFromDiffTreeLine(string line, string repoRoot)
        {
            line = line.Substring(1);
            
            // Filenames may contain spaces, but always follow a \t. Other fields are space delimited.
            string[] parts = line.Split('\t');
            parts = parts[0].Split(' ').Concat(parts.Skip(1)).ToArray();

            DiffTreeResult result = new DiffTreeResult();
            result.SourceIsDirectory = ValidTreeModes.Contains(parts[0]);
            result.TargetIsDirectory = ValidTreeModes.Contains(parts[1]);
            result.SourceSha = parts[2];
            result.TargetSha = parts[3];
            result.Operation = DiffTreeResult.ParseOperation(parts[4]);
            result.TargetFilename = ConvertPathToAbsoluteUtf8Path(repoRoot, parts.Last());
            result.SourceFilename = parts.Length == 7 ? ConvertPathToAbsoluteUtf8Path(repoRoot, parts[5]) : null;
            return result;
        }

        public static DiffTreeResult ParseFromLsTreeLine(string line, string repoRoot)
        {
            // Everything from ls-tree is an add.
            int treeIndex = line.IndexOf(GitCatFileProcess.TreeMarker);
            if (treeIndex >= 0)
            {
                DiffTreeResult treeAdd = new DiffTreeResult();
                treeAdd.TargetIsDirectory = true;
                treeAdd.TargetFilename = ConvertPathToAbsoluteUtf8Path(repoRoot, line.Substring(line.LastIndexOf("\t") + 1));
                treeAdd.Operation = DiffTreeResult.Operations.Add;

                return treeAdd;
            }
            else
            {
                int blobIndex = line.IndexOf(GitCatFileProcess.BlobMarker);
                if (blobIndex >= 0)
                {
                    DiffTreeResult blobAdd = new DiffTreeResult();
                    blobAdd.TargetSha = line.Substring(blobIndex + GitCatFileProcess.BlobMarker.Length, GVFSConstants.ShaStringLength);
                    blobAdd.TargetFilename = ConvertPathToAbsoluteUtf8Path(repoRoot, line.Substring(line.LastIndexOf("\t") + 1));
                    blobAdd.Operation = DiffTreeResult.Operations.Add;

                    return blobAdd;
                }
                else
                {
                    return null;
                }
            }
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

        private static string ConvertPathToAbsoluteUtf8Path(string repoRoot, string relativePath)
        {
            return Path.Combine(repoRoot, GitPathConverter.ConvertPathOctetsToUtf8(relativePath.Trim('"')).Replace('/', '\\'));
        }
    }
}