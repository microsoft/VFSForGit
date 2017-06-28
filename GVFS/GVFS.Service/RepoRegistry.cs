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
        private const string EtwArea = nameof(RepoRegistry);
        private const string RegistryName = "repo-registry";
        private const string RegistryTempName = "repo-registry.lock";
        private const string RegistryVersionString = "1";

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
        
        public bool TryRegisterRepo(string repoRoot)
        {
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
                            this.WriteRegistry(allRepos);                            
                        }
                    }
                    else
                    {
                        allRepos[repoRoot] = new RepoRegistration(repoRoot);
                        this.WriteRegistry(allRepos);
                    }
                }

                this.tracer.RelatedInfo("Registered repo {0}", repoRoot);
                return true;
            }
            catch (Exception e)
            {
                this.tracer.RelatedError("Error while registering repo {0}: {1}", repoRoot, e.ToString());
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

        public bool TryDeactivateRepo(string repoRoot)
        {
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
                        this.tracer.RelatedError("Attempted to deactivate non-existent repo at '{0}'", repoRoot);
                    }
                }                
            }
            catch (Exception e)
            {
                this.tracer.RelatedError("Error while deactivating repo {0}: {1}", repoRoot, e.ToString());
            }

            return false;
        }

        public void AutoMountRepos()
        {
            List<string> activeRepos = this.GetAllActiveRepos();
            if (activeRepos.Count == 0)
            {
                return;
            }

            using (ITracer activity = this.tracer.StartActivity("AutoMount", EventLevel.Informational))
            {
                this.SendNotification("GVFS AutoMount", "Attempting to mount {0} GVFS repo(s)", activeRepos.Count);

                EventLevel verbosity = EventLevel.Informational;
                if (!Enum.TryParse(MountParameters.DefaultVerbosity, out verbosity))
                {
                    this.tracer.RelatedError("Unable to parse DefaultVerbosity'{0}'", MountParameters.DefaultVerbosity);
                }

                foreach (string repoRoot in activeRepos)
                {
                    GVFSMountProcess process = new GVFSMountProcess(activity, repoRoot);
                    if (process.Mount(verbosity, Keywords.Any, false))
                    {
                        this.SendNotification("GVFS AutoMount", "The following GVFS repo is now mounted: \n{0}", repoRoot);
                    }
                    else
                    {
                        this.SendNotification("GVFS AutoMount", "The following GVFS repo failed to mount: \n{0}", repoRoot);
                    }
                }
            }
        }
        
        private List<string> GetAllActiveRepos()
        {
            lock (this.repoLock)
            {
                try
                {
                    Dictionary<string, RepoRegistration> repos = this.ReadRegistry();
                    return repos
                        .Values
                        .Where(repo => repo.IsActive)
                        .Select(repo => repo.EnlistmentRoot)
                        .ToList();
                }
                catch (Exception e)
                {
                    this.tracer.RelatedError("Unable to get list of active repos: {0}", e.ToString());
                    return new List<string>();
                }
            }
        }

        private void SendNotification(string title, string format, params object[] args)
        {
            NamedPipeMessages.Notification.Request request = new NamedPipeMessages.Notification.Request();
            request.Title = title;
            request.Message = string.Format(format, args);

            NotificationHandler.Instance.SendNotification(this.tracer, request);
        }

        private Dictionary<string, RepoRegistration> ReadRegistry()
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
                    if (versionString != RegistryVersionString)
                    {
                        if (versionString != null)
                        {
                            EventMetadata metadata = new EventMetadata();
                            metadata.Add("Area", EtwArea);
                            metadata.Add("OnDiskVersion", versionString);
                            metadata.Add("ExpectedVersion", RegistryVersionString);
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
                    writer.WriteLine(RegistryVersionString);

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
