using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace GVFS.GVFlt.DotGit
{
    public class AlwaysExcludeFile
    {
        private const string DefaultEntry = "*";
        private HashSet<string> entries;
        private HashSet<string> entriesToRemove;
        private FileSerializer fileSerializer;
        private GVFSContext context;

        public AlwaysExcludeFile(GVFSContext context, string virtualAlwaysExcludeFilePath)
        {
            this.entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.entriesToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        public CallbackResult FlushAndClose()
        {
            if (this.entriesToRemove.Count > 0)
            {
                foreach (string entry in this.entriesToRemove)
                {
                    this.entries.Remove(entry);
                }

                try
                {
                    this.fileSerializer.ReplaceFile(this.entries);
                }
                catch (IOException e)
                {
                    return this.ReportException(e, null, isRetryable: true);
                }
                catch (Win32Exception e)
                {
                    return this.ReportException(e, null, isRetryable: true);
                }
                catch (Exception e)
                {
                    return this.ReportException(e, null, isRetryable: false);
                }

                this.entriesToRemove.Clear();
            }

            this.fileSerializer.Close();
            return CallbackResult.Success;
        }

        public CallbackResult AddEntriesForPath(string virtualPath)
        {
            string entry = virtualPath.Replace(GVFSConstants.PathSeparator, GVFSConstants.GitPathSeparator);
            entry = "!" + GVFSConstants.GitPathSeparatorString + entry;

            CallbackResult result = this.AddParentFolderEntries(entry);
            if (result != CallbackResult.Success)
            {
                return result;
            }

            try
            {
                if (this.entries.Add(entry))
                {
                    this.fileSerializer.AppendLine(entry);
                }

                this.entriesToRemove.Remove(entry);
            }
            catch (IOException e)
            {
                return this.ReportException(e, entry, isRetryable: true);
            }
            catch (Exception e)
            {
                return this.ReportException(e, entry, isRetryable: false);
            }

            return CallbackResult.Success;
        }

        public CallbackResult RemoveEntriesForFile(string virtualPath)
        {
            string entry = virtualPath.Replace(GVFSConstants.PathSeparator, GVFSConstants.GitPathSeparator);
            entry = "!" + GVFSConstants.GitPathSeparatorString + entry;

            this.entriesToRemove.Add(entry);

            // We must add the folder path to this file so that git clean removes the folders if they are empty.
            CallbackResult result = this.AddParentFolderEntries(entry);
            if (result != CallbackResult.Success)
            {
                return result;
            }

            return CallbackResult.Success;
        }

        private CallbackResult AddParentFolderEntries(string fileEntry)
        {
            try
            {
                string[] pathParts = fileEntry.Split(new char[] { GVFSConstants.GitPathSeparator }, StringSplitOptions.RemoveEmptyEntries);
                StringBuilder path = new StringBuilder(pathParts[0] + GVFSConstants.GitPathSeparatorString, fileEntry.Length);

                // fileEntry starts with "!/", so we skip i = 0 to avoid adding exactly "!/".
                for (int i = 1; i < pathParts.Length - 1; i++)
                {
                    path.Append(pathParts[i]);
                    path.Append(GVFSConstants.GitPathSeparator);

                    string entry = path.ToString();
                    if (this.entries.Add(entry))
                    {
                        this.fileSerializer.AppendLine(entry);
                    }
                }
            }
            catch (IOException e)
            {
                return this.ReportException(e, fileEntry, isRetryable: true);
            }
            catch (Exception e)
            {
                return this.ReportException(e, fileEntry, isRetryable: false);
            }

            return CallbackResult.Success;
        }

        private CallbackResult ReportException(
            Exception e,
            string virtualPath,
            bool isRetryable,
            [System.Runtime.CompilerServices.CallerMemberName] string functionName = "")
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", "AlwaysExcludeFile");
            if (virtualPath != null)
            {
                metadata.Add("virtualPath", virtualPath);
            }

            metadata.Add("Exception", e.ToString());
            if (isRetryable)
            {
                this.context.Tracer.RelatedWarning(metadata, e.GetType().ToString() + " caught while processing " + functionName);
                return CallbackResult.RetryableError;
            }
            else
            {
                this.context.Tracer.RelatedError(metadata, e.GetType().ToString() + " caught while processing " + functionName);
                return CallbackResult.FatalError;
            }
        }
    }
}
