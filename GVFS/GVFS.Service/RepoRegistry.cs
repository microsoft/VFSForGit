using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Service.Handlers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Service
{
    public class RepoRegistry
    {
        public const string RegistryName = "repo-registry";
        private const string EtwArea = nameof(RepoRegistry);
        private const string RegistryTempName = "repo-registry.lock";
        private const int RegistryVersion = 2;

        private string registryParentFolderPath;
        private ITracer tracer;
        private PhysicalFileSystem fileSystem;
        private object repoLock = new object();

        public RepoRegistry(ITracer tracer, PhysicalFileSystem fileSystem, string serviceDataLocation)
        {
            this.tracer = tracer;
            this.fileSystem = fileSystem;
            this.registryParentFolderPath = serviceDataLocation;

            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            metadata.Add("registryParentFolderPath", this.registryParentFolderPath);
            metadata.Add(TracingConstants.MessageKey.InfoMessage, "RepoRegistry created");
            this.tracer.RelatedEvent(EventLevel.Informational, "RepoRegistry_Created", metadata);
        }

        public void Upgrade()
        {
            // Version 1 to Version 2, added OwnerSID
            Dictionary<string, RepoRegistration> allRepos = this.ReadRegistry();
            if (allRepos.Any())
            {
                this.WriteRegistry(allRepos);
            }
        }

        public bool TryRegisterRepo(string repoRoot, string ownerSID, out string errorMessage)
        {
            errorMessage = null;

            try
            {
                lock (this.repoLock)
                {
                    Dictionary<string, RepoRegistration> allRepos = this.ReadRegistry();
                    RepoRegistration repo;
                    if (allRepos.TryGetValue(repoRoot, out repo))
                    {
                        if (!repo.IsActive)
                        {
                            repo.IsActive = true;
                            repo.OwnerSID = ownerSID;
                            this.WriteRegistry(allRepos);
                        }
                    }
                    else
                    {
                        allRepos[repoRoot] = new RepoRegistration(repoRoot, ownerSID);
                        this.WriteRegistry(allRepos);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                errorMessage = string.Format("Error while registering repo {0}: {1}", repoRoot, e.ToString());
            }

            return false;
        }

        public void TraceStatus()
        {
            try
            {
                lock (this.repoLock)
                {
                    Dictionary<string, RepoRegistration> allRepos = this.ReadRegistry();
                    foreach (RepoRegistration repo in allRepos.Values)
                    {
                        this.tracer.RelatedInfo(repo.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                this.tracer.RelatedError("Error while tracing repos: {0}", e.ToString());
            }
        }

        public bool TryDeactivateRepo(string repoRoot, out string errorMessage)
        {
            errorMessage = null;

            try
            {
                lock (this.repoLock)
                {
                    Dictionary<string, RepoRegistration> allRepos = this.ReadRegistry();
                    RepoRegistration repo;
                    if (allRepos.TryGetValue(repoRoot, out repo))
                    {
                        if (repo.IsActive)
                        {
                            repo.IsActive = false;
                            this.WriteRegistry(allRepos);
                        }

                        return true;
                    }
                    else
                    {
                        errorMessage = string.Format("Attempted to deactivate non-existent repo at '{0}'", repoRoot);
                    }
                }
            }
            catch (Exception e)
            {
                errorMessage = string.Format("Error while deactivating repo {0}: {1}", repoRoot, e.ToString());
            }

            return false;
        }

        public bool TryRemoveRepo(string repoRoot, out string errorMessage)
        {
            errorMessage = null;

            try
            {
                lock (this.repoLock)
                {
                    Dictionary<string, RepoRegistration> allRepos = this.ReadRegistry();
                    if (allRepos.Remove(repoRoot))
                    {
                        this.WriteRegistry(allRepos);
                        return true;
                    }
                    else
                    {
                        errorMessage = string.Format("Attempted to remove non-existent repo at '{0}'", repoRoot);
                    }
                }
            }
            catch (Exception e)
            {
                errorMessage = string.Format("Error while removing repo {0}: {1}", repoRoot, e.ToString());
            }

            return false;
        }

        public Dictionary<string, RepoRegistration> ReadRegistry()
        {
            Dictionary<string, RepoRegistration> allRepos = new Dictionary<string, RepoRegistration>(StringComparer.OrdinalIgnoreCase);

            using (Stream stream = this.fileSystem.OpenFileStream(
                    Path.Combine(this.registryParentFolderPath, RegistryName),
                    FileMode.OpenOrCreate,
                    FileAccess.Read,
                    FileShare.Read,
                    callFlushFileBuffers: false))
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    string versionString = reader.ReadLine();
                    int version;
                    if (!int.TryParse(versionString, out version) ||
                        version > RegistryVersion)
                    {
                        if (versionString != null)
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Area", EtwArea);
                            metadata.Add("OnDiskVersion", versionString);
                            metadata.Add("ExpectedVersion", versionString);
                            this.tracer.RelatedError(metadata, "ReadRegistry: Unsupported version");
                        }

                        return allRepos;
                    }

                    while (!reader.EndOfStream)
                    {
                        string entry = reader.ReadLine();
                        if (entry.Length > 0)
                        {
                            try
                            {
                                RepoRegistration registration = RepoRegistration.FromJson(entry);

                                string errorMessage;
                                string enlistmentPath = registration.EnlistmentRoot;

                                // Try and normalize the enlistment path if the volume is available, otherwise just take the
                                // path verbatim.
                                string volumePath = GVFSPlatform.Instance.FileSystem.GetVolumeRoot(registration.EnlistmentRoot);
                                if (GVFSPlatform.Instance.FileSystem.IsVolumeAvailable(volumePath))
                                {
                                    string normalizedPath;
                                    if (GVFSPlatform.Instance.FileSystem.TryGetNormalizedPath(registration.EnlistmentRoot, out normalizedPath, out errorMessage))
                                    {
                                        if (!normalizedPath.Equals(registration.EnlistmentRoot, StringComparison.OrdinalIgnoreCase))
                                        {
                                            enlistmentPath = normalizedPath;

                                            EventMetadata metadata = new EventMetadata();
                                            metadata.Add("registration.EnlistmentRoot", registration.EnlistmentRoot);
                                            metadata.Add(nameof(normalizedPath), normalizedPath);
                                            metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(ReadRegistry)}: Mapping registered enlistment root to final path");
                                            this.tracer.RelatedEvent(EventLevel.Informational, $"{nameof(ReadRegistry)}_NormalizedPathMapping", metadata);
                                        }
                                    }
                                    else
                                    {
                                        EventMetadata metadata = new EventMetadata();
                                        metadata.Add("registration.EnlistmentRoot", registration.EnlistmentRoot);
                                        metadata.Add("NormalizedEnlistmentRootPath", normalizedPath);
                                        metadata.Add("ErrorMessage", errorMessage);
                                        this.tracer.RelatedWarning(metadata, $"{nameof(ReadRegistry)}: Failed to get normalized path name for registed enlistment root");
                                    }
                                }

                                allRepos[enlistmentPath] = registration;
                            }
                            catch (Exception e)
                            {
                                EventMetadata metadata = new EventMetadata();
                                metadata.Add("Area", EtwArea);
                                metadata.Add("entry", entry);
                                metadata.Add("Exception", e.ToString());
                                this.tracer.RelatedError(metadata, "ReadRegistry: Failed to read entry");
                            }
                        }
                    }
                }
            }

            return allRepos;
        }

        public bool TryGetActiveRepos(out List<RepoRegistration> repoList, out string errorMessage)
        {
            repoList = null;
            errorMessage = null;

            lock (this.repoLock)
            {
                try
                {
                    Dictionary<string, RepoRegistration> repos = this.ReadRegistry();
                    repoList = repos
                        .Values
                        .Where(repo => repo.IsActive)
                        .ToList();
                    return true;
                }
                catch (Exception e)
                {
                    errorMessage = string.Format("Unable to get list of active repos: {0}", e.ToString());
                    return false;
                }
            }
        }

        public bool TryGetActiveReposForUser(string ownerSID, out List<RepoRegistration> repoList, out string errorMessage)
        {
            repoList = null;
            errorMessage = null;

            lock (this.repoLock)
            {
                try
                {
                    Dictionary<string, RepoRegistration> repos = this.ReadRegistry();
                    repoList = repos
                        .Values
                        .Where(repo => repo.IsActive)
                        .Where(repo => string.Equals(repo.OwnerSID, ownerSID, StringComparison.InvariantCultureIgnoreCase))
                        .ToList();
                    return true;
                }
                catch (Exception e)
                {
                    errorMessage = string.Format("Unable to get list of active repos for user {0}: {1}", ownerSID, e.ToString());
                    return false;
                }
            }
        }

        private void WriteRegistry(Dictionary<string, RepoRegistration> registry)
        {
            string tempFilePath = Path.Combine(this.registryParentFolderPath, RegistryTempName);
            using (Stream stream = this.fileSystem.OpenFileStream(
                    tempFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    callFlushFileBuffers: true))
            using (StreamWriter writer = new StreamWriter(stream))
            {
                writer.WriteLine(RegistryVersion);

                foreach (RepoRegistration repo in registry.Values)
                {
                    writer.WriteLine(repo.ToJson());
                }

                stream.Flush();
            }

            this.fileSystem.MoveAndOverwriteFile(tempFilePath, Path.Combine(this.registryParentFolderPath, RegistryName));
        }
    }
}
