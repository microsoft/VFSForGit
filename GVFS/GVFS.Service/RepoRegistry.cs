using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Service.Handlers;
using Microsoft.Diagnostics.Tracing;
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
        private object repoLock = new object();

        public RepoRegistry(ITracer tracer, string serviceDataLocation)
        {
            this.tracer = tracer;
            this.registryParentFolderPath = serviceDataLocation;

            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            metadata.Add("registryParentFolderPath", this.registryParentFolderPath);
            metadata.Add("Message", "RepoRegistry created");
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

                this.tracer.RelatedInfo("Registered repo {0}", repoRoot);
                return true;
            }
            catch (Exception e)
            {
                errorMessage = string.Format("Error while registering repo {0}: {1}", repoRoot, e.ToString());
                this.tracer.RelatedError(errorMessage);
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
                this.tracer.RelatedError("Error while tracing repos", e.ToString());
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

                        this.tracer.RelatedInfo("Deactivated repo {0}", repoRoot);
                        return true;
                    }
                    else
                    {
                        errorMessage = string.Format("Attempted to deactivate non-existent repo at '{0}'", repoRoot);
                        this.tracer.RelatedError(errorMessage);
                    }
                }
            }
            catch (Exception e)
            {
                errorMessage = string.Format("Error while deactivating repo {0}: {1}", repoRoot, e.ToString());
                this.tracer.RelatedError(errorMessage);
            }

            return false;
        }

        public void AutoMountRepos(int sessionId)
        {
            using (ITracer activity = this.tracer.StartActivity("AutoMount", EventLevel.Informational))
            {
                using (GVFSMountProcess process = new GVFSMountProcess(activity, sessionId))
                {
                    List<RepoRegistration> activeRepos = this.GetActiveReposForUser(process.CurrentUser.Identity.User.Value);
                    if (activeRepos.Count == 0)
                    {
                        return;
                    }

                    this.SendNotification(sessionId, "GVFS AutoMount", "Attempting to mount {0} GVFS repo(s)", activeRepos.Count);

                    foreach (RepoRegistration repo in activeRepos)
                    {
                        // TODO #1043088: We need to respect the elevation level of the original mount
                        if (process.Mount(repo.EnlistmentRoot))
                        {
                            this.SendNotification(sessionId, "GVFS AutoMount", "The following GVFS repo is now mounted: \n{0}", repo.EnlistmentRoot);
                        }
                        else
                        {
                            this.SendNotification(sessionId, "GVFS AutoMount", "The following GVFS repo failed to mount: \n{0}", repo.EnlistmentRoot);
                        }
                    }
                }
            }
        }

        public Dictionary<string, RepoRegistration> ReadRegistry()
        {
            Dictionary<string, RepoRegistration> allRepos = new Dictionary<string, RepoRegistration>(StringComparer.OrdinalIgnoreCase);

            using (FileStream stream = new FileStream(
                    Path.Combine(this.registryParentFolderPath, RegistryName),
                    FileMode.OpenOrCreate,
                    FileAccess.Read,
                    FileShare.Read))
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
                            metadata.Add("ErrorMessage", "ReadRegistry: Unsupported version");
                            this.tracer.RelatedError(metadata);
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
                                allRepos[registration.EnlistmentRoot] = registration;
                            }
                            catch (Exception e)
                            {
                                EventMetadata metadata = new EventMetadata();
                                metadata.Add("Area", EtwArea);
                                metadata.Add("entry", entry);
                                metadata.Add("Exception", e.ToString());
                                metadata.Add("ErrorMessage", "ReadRegistry: Failed to read entry");
                                this.tracer.RelatedError(metadata);
                            }
                        }
                    }
                }
            }

            return allRepos;
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

        private void SendNotification(int sessionId, string title, string format, params object[] args)
        {
            NamedPipeMessages.Notification.Request request = new NamedPipeMessages.Notification.Request();
            request.Title = title;
            request.Message = string.Format(format, args);

            NotificationHandler.Instance.SendNotification(this.tracer, sessionId, request);
        }

        private void WriteRegistry(Dictionary<string, RepoRegistration> registry)
        {
            string tempFilePath = Path.Combine(this.registryParentFolderPath, RegistryTempName);
            using (FileStream stream = new FileStream(
                    tempFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
            {
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    writer.WriteLine(RegistryVersion);

                    foreach (RepoRegistration repo in registry.Values)
                    {
                        writer.WriteLine(repo.ToJson());
                    }
                }
            }

            File.Replace(tempFilePath, Path.Combine(this.registryParentFolderPath, RegistryName), null);
        }
    }
}
