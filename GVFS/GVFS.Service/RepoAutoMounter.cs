using System;
using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using GVFS.Service.Handlers;

namespace GVFS.Service
{
    public class RepoAutoMounter : IDisposable
    {
        private readonly ITracer tracer;
        private readonly RepoRegistry repoRegistry;
        private readonly int sessionId;
        private readonly GVFSMountProcessManager mountProcessManager;
        private readonly string userSid;

        private IVolumeStateWatcher volumeWatcher;

        public RepoAutoMounter(ITracer tracer, RepoRegistry repoRegistry, int sessionId)
        {
            this.tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
            this.repoRegistry = repoRegistry ?? throw new ArgumentNullException(nameof(repoRegistry));
            this.sessionId = sessionId;

            // Create a mount process factory for this session/user
            this.mountProcessManager = new GVFSMountProcessManager(this.tracer, sessionId);
            this.userSid = this.mountProcessManager.CurrentUser.Identity.User?.Value;
        }

        public void Start()
        {
            this.tracer.RelatedInfo("Starting auto mounter for session {0}", this.sessionId);

            // Try mounting all the user's active repo straight away
            this.tracer.RelatedInfo("Attempting to mount all known repos for user {0}", this.userSid);
            this.MountAll();

            // Start watching for changes to volume availability
            this.volumeWatcher = GVFSPlatform.Instance.FileSystem.CreateVolumeStateWatcher();
            this.volumeWatcher.VolumeStateChanged += this.OnVolumeStateChanged;
            this.volumeWatcher.Start();
        }

        public void Stop()
        {
            this.tracer.RelatedInfo("Stopping auto mounter for session {0}", this.sessionId);

            // Stop watching for changes to volume availability
            if (this.volumeWatcher != null)
            {
                this.volumeWatcher.Stop();
                this.volumeWatcher.VolumeStateChanged -= this.OnVolumeStateChanged;
                this.volumeWatcher.Dispose();
                this.volumeWatcher = null;
            }
        }

        public void Dispose()
        {
            this.Stop();
            this.mountProcessManager?.Dispose();
        }

        private void MountAll(string rootPath = null)
        {
            if (this.repoRegistry.TryGetActiveReposForUser(this.userSid, out var activeRepos, out string errorMessage))
            {
                foreach (RepoRegistration repo in activeRepos)
                {
                    if (rootPath == null || GVFSPlatform.Instance.FileSystem.IsPathUnderDirectory(rootPath, repo.EnlistmentRoot))
                    {
                        this.Mount(repo.EnlistmentRoot);
                    }
                }
            }
            else
            {
                this.tracer.RelatedError("Could not get repos to auto mount for user. Error: " + errorMessage);
            }
        }

        private void Mount(string enlistmentRoot)
        {
            var metadata = new EventMetadata
            {
                ["EnlistmentRoot"] = enlistmentRoot
            };

            using (var activity = this.tracer.StartActivity("AutoMount", EventLevel.Informational, metadata))
            {
                string volumeRoot = GVFSPlatform.Instance.FileSystem.GetVolumeRoot(enlistmentRoot);
                if (GVFSPlatform.Instance.FileSystem.IsVolumeAvailable(volumeRoot))
                {
                    // TODO #1043088: We need to respect the elevation level of the original mount
                    if (this.mountProcessManager.StartMount(enlistmentRoot))
                    {
                        this.SendNotification("GVFS AutoMount", "The following GVFS repo is now mounted:\n{0}", enlistmentRoot);
                        activity.RelatedInfo("Auto mount was successful for '{0}'", enlistmentRoot);
                    }
                    else
                    {
                        this.SendNotification("GVFS AutoMount", "The following GVFS repo failed to mount:\n{0}", enlistmentRoot);
                        activity.RelatedError("Failed to auto mount '{0}'", enlistmentRoot);
                    }
                }
                else
                {
                    activity.RelatedInfo("Cannot auto mount '{0}' because the volume '{1}' not available.", enlistmentRoot, volumeRoot);
                }
            }
        }

        private void OnVolumeStateChanged(object sender, VolumeStateChangedEventArgs e)
        {
            var metadata = new EventMetadata
            {
                ["State"] = e.ChangeType.ToString(),
                ["Volume"] = e.VolumePath,
            };

            this.tracer.RelatedEvent(EventLevel.Informational, "VolumeStateChange", metadata);

            switch (e.ChangeType)
            {
                case VolumeStateChangeType.VolumeAvailable:
                    this.MountAll(rootPath: e.VolumePath);
                    break;
                case VolumeStateChangeType.VolumeUnavailable:
                    // There is no need to do anything here to stop any potentially orphaned mount processes
                    // since they will self-terminate if the volume is removed.
                    break;
                default:
                    this.tracer.RelatedWarning("Unknown volume state change type: {0}", e.ChangeType);
                    break;
            }
        }

        private void SendNotification(string title, string format, params object[] args)
        {
            var request = new NamedPipeMessages.Notification.Request
            {
                Title = title,
                Message = string.Format(format, args)
            };

            NotificationHandler.Instance.SendNotification(this.tracer, this.sessionId, request);
        }
    }
}
