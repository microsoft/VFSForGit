using GVFS.Common;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GVFS.GVFlt.DotGit
{
    public class ExcludeFile
    {
        private const string DefaultEntry = "*";
        private HashSet<string> entries;
        private FileSerializer fileSerializer;
        private GVFSContext context;

        public ExcludeFile(GVFSContext context, string virtualExcludeFilePath)
        {
            this.entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.fileSerializer = new FileSerializer(context, virtualExcludeFilePath);
            this.context = context;
        }

        public void LoadOrCreate()
        {
            foreach (string line in this.fileSerializer.ReadAll())
            {
                string sanitizedFileLine;
                if (GitConfigFileUtils.TrySanitizeConfigFileLine(line, out sanitizedFileLine))
                {
                    this.entries.Add(sanitizedFileLine);
                }
            }

            // Ensure the default entry is always in the exclude file
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

        public CallbackResult FolderChanged(string virtualPath)
        {
            try
            {
                string[] pathParts = virtualPath.Split(new char[] { GVFSConstants.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);

                StringBuilder path = new StringBuilder("!");
                for (int i = 0; i < pathParts.Length; i++)
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
                metadata.Add("Area", "ExcludeFile");
                metadata.Add("virtualPath", virtualPath);
                metadata.Add("Exception", e.ToString());
                metadata.Add("ErrorMessage", "IOException caught while processing FolderChanged");
                this.context.Tracer.RelatedError(metadata);
                return CallbackResult.RetryableError;
            }
            catch (Exception e)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", "ExcludeFile");
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
