using GVFS.Common;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GVFS.Common.Git;

namespace GVFS.GVFlt.DotGit
{
    public class AlwaysExcludeFile
    {
        private const string DefaultEntry = "*";
        private HashSet<string> entries;
        private FileSerializer fileSerializer;
        private GVFSContext context;

        public AlwaysExcludeFile(GVFSContext context, string virtualAlwaysExcludeFilePath)
        {
            this.entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.fileSerializer = new FileSerializer(context, virtualAlwaysExcludeFilePath);
            this.context = context;
        }

        public void LoadOrCreate()
        {
            foreach (string line in this.fileSerializer.ReadAll())
            {
                string sanitizedFileLine;
                if (GitConfigHelper.TrySanitizeConfigFileLine(line, out sanitizedFileLine))
                {
                    this.entries.Add(sanitizedFileLine);
                }
            }

            // Ensure the default entry is always in the always_exclude file
            if (this.entries.Add(DefaultEntry))
            {
                this.fileSerializer.AppendLine(DefaultEntry);
                this.fileSerializer.Close();
            }
        }

        public void Close()
        {
            this.fileSerializer.Close();
        }

        public CallbackResult AddEntriesForFileOrFolder(string virtualPath, bool isFolder)
        {
            try
            {
                string[] pathParts = virtualPath.Split(new char[] { GVFSConstants.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
                int numberOfPathPartsToUse = pathParts.Length;
                if (!isFolder)
                {
                    // Don't need an entry for the file since only folders are in the always_exclude
                    numberOfPathPartsToUse -= 1;
                }

                StringBuilder path = new StringBuilder("!");
                for (int i = 0; i < numberOfPathPartsToUse; i++)
                {
                    path.Append(GVFSConstants.GitPathSeparatorString + pathParts[i]);
                    string entry = path.ToString();
                    if (this.entries.Add(entry))
                    {
                        this.fileSerializer.AppendLine(entry);
                    }
                }

                string finalEntry = path.ToString() + GVFSConstants.GitPathSeparatorString + "*";
                if (this.entries.Add(finalEntry))
                {
                    this.fileSerializer.AppendLine(finalEntry);
                }
            }
            catch (IOException e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "AlwaysExcludeFile");
                metadata.Add("virtualPath", virtualPath);
                metadata.Add("Exception", e.ToString());
                metadata.Add("ErrorMessage", "IOException caught while processing FolderChanged");
                this.context.Tracer.RelatedError(metadata);
                return CallbackResult.RetryableError;
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "AlwaysExcludeFile");
                metadata.Add("virtualPath", virtualPath);
                metadata.Add("Exception", e.ToString());
                metadata.Add("ErrorMessage", "Exception caught while processing FolderChanged");
                this.context.Tracer.RelatedError(metadata);
                return CallbackResult.FatalError;
            }

            return CallbackResult.Success;
        }
    }
}
