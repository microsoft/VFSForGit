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
    public class RepoRegistry : IRepoRegistry
    {
        public const string RegistryName = "repo-registry";
        private const string EtwArea = nameof(RepoRegistry);
        private const string RegistryTempName = "repo-registry.lock";
        private const int RegistryVersion = 2;

        private string registryParentFolderPath;
        private ITracer tracer;
        private PhysicalFileSystem fileSystem;
        private object repoLock = new object();
        private IRepoMounter repoMounter;
        private INotificationHandler notificationHandler;

        public RepoRegistry(
            ITracer tracer,
            PhysicalFileSystem fileSystem,
            string serviceDataLocation,
            IRepoMounter repoMounter,
            INotificationHandler notificationHandler)
        {
            this.tracer = tracer;
            this.fileSystem = fileSystem;
            this.registryParentFolderPath = serviceDataLocation;
            this.repoMounter = repoMounter;
            this.notificationHandler = notificationHandler;

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

        public void AutoMountRepos(string userId, int sessionId)
        {
            using (ITracer activity = this.tracer.StartActivity("AutoMount", EventLevel.Informational))
            {
                List<RepoRegistration> activeRepos = this.GetActiveReposForUser(userId);
                foreach (RepoRegistration repo in activeRepos)
                {
                    // TODO #1089: We need to respect the elevation level of the original mount
                    if (!this.repoMounter.MountRepository(repo.EnlistmentRoot, sessionId))
                    {
                        this.SendNotification(
                            sessionId,
                            NamedPipeMessages.Notification.Request.Identifier.MountFailure,
                            repo.EnlistmentRoot);
                    }
                }
            }
        }

        public Dictionary<string, RepoRegistration> ReadRegistry()
        {
            Dictionary<string, RepoRegistration> allRepos = new Dictionary<string, RepoRegistration>(GVFSPlatform.Instance.Constants.PathComparer);

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
                                string normalizedEnlistmentRootPath = registration.EnlistmentRoot;
                                if (this.fileSystem.TryGetNormalizedPath(registration.EnlistmentRoot, out normalizedEnlistmentRootPath, out errorMessage))
                                {
                                    if (!normalizedEnlistmentRootPath.Equals(registration.EnlistmentRoot, GVFSPlatform.Instance.Constants.PathComparison))
                                    {
                                        EventMetadata metadata = new EventMetadata();
                                        metadata.Add("registration.EnlistmentRoot", registration.EnlistmentRoot);
                                        metadata.Add(nameof(normalizedEnlistmentRootPath), normalizedEnlistmentRootPath);
                                        metadata.Add(TracingConstants.MessageKey.InfoMessage, $"{nameof(this.ReadRegistry)}: Mapping registered enlistment root to final path");
                                        this.tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.ReadRegistry)}_NormalizedPathMapping", metadata);
                                    }
                                }
                                else
                                {
                                    EventMetadata metadata = new EventMetadata();
                                    metadata.Add("registration.EnlistmentRoot", registration.EnlistmentRoot);
                                    metadata.Add("NormalizedEnlistmentRootPath", normalizedEnlistmentRootPath);
                                    metadata.Add("ErrorMessage", errorMessage);
                                    this.tracer.RelatedWarning(metadata, $"{nameof(this.ReadRegistry)}: Failed to get normalized path name for registed enlistment root");
                                }

                                if (normalizedEnlistmentRootPath != null)
                                {
                                    allRepos[normalizedEnlistmentRootPath] = registration;
                                }
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

        private List<RepoRegistration> GetActiveReposForUser(string ownerSID)
        {
            lock (this.repoLock)
            {
                try
                {
                    Dictionary<string, RepoRegistration> repos = this.ReadRegistry();
                    return repos
                        .Values
                        .Where(repo => repo.IsActive)
                        .Where(repo => string.Equals(repo.OwnerSID, ownerSID, StringComparison.InvariantCultureIgnoreCase))
                        .ToList();
                }
                catch (Exception e)
                {
                    this.tracer.RelatedError("Unable to get list of active repos for user {0}: {1}", ownerSID, e.ToString());
                    return new List<RepoRegistration>();
                }
            }
        }

        private void SendNotification(
            int sessionId,
            NamedPipeMessages.Notification.Request.Identifier requestId,
            string enlistment = null,
            int enlistmentCount = 0)
        {
            NamedPipeMessages.Notification.Request request = new NamedPipeMessages.Notification.Request();
            request.Id = requestId;
            request.Enlistment = enlistment;
            request.EnlistmentCount = enlistmentCount;

            this.notificationHandler.SendNotification(request);
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
