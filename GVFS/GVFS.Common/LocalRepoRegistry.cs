using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Common
{
    /// <summary>
    /// File-backed repo registry usable from any GVFS process without going
    /// through GVFS.Service. The on-disk format is wire-compatible with
    /// GVFS.Service.RepoRegistry — both produce and consume the same
    /// <c>repo-registry</c> file under the shared service data directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On-disk format (line-oriented, identical to <c>GVFS.Service.RepoRegistry</c>):
    /// </para>
    /// <list type="bullet">
    /// <item>Line 1: registry format version (integer, currently <c>2</c>).</item>
    /// <item>
    /// Lines 2..N: one <see cref="LocalRepoRegistration"/> JSON object per line.
    /// Blank lines and lines that fail to parse are skipped (matches the service's
    /// tolerance for partial corruption).
    /// </item>
    /// </list>
    /// <para>
    /// Threading: instance methods that read or write the registry serialize on
    /// a private instance lock. Cross-process safety relies on the same atomic
    /// write-temp-then-replace pattern the service uses.
    /// </para>
    /// <para>
    /// This type does not pick its own storage location — callers pass
    /// <paramref name="registryDirectory"/> via the constructor. Production
    /// callers should pass <c>GVFSPlatform.Instance.GetSecureDataRootForGVFSComponent(ServiceDataDirName)</c>
    /// so the file lives at the same path the service uses (which honors the
    /// <c>GVFS_SECURE_DATA_ROOT</c> environment-variable redirect for user-level
    /// installs).
    /// </para>
    /// </remarks>
    public class LocalRepoRegistry
    {
        /// <summary>
        /// Subdirectory under the platform's secure-data root that holds the
        /// registry file. Matches the legacy service's name so both producers
        /// write to the same location.
        /// </summary>
        public const string ServiceDataDirName = "GVFS.Service";

        /// <summary>Final on-disk name of the registry file.</summary>
        public const string RegistryFileName = "repo-registry";

        /// <summary>
        /// Temp name used by the atomic write-then-replace pattern. Named
        /// <c>repo-registry.lock</c> for byte-for-byte compatibility with
        /// the legacy service's choice (so a writer interrupted mid-rename
        /// leaves a file with the same name the service would have left).
        /// </summary>
        public const string RegistryTempName = "repo-registry.lock";

        /// <summary>
        /// Registry format version this implementation can read AND write.
        /// Files with a higher version on disk are treated as opaque: read
        /// returns empty and we refuse to overwrite, so a newer GVFS that
        /// has written the registry is not corrupted by an older GVFS.
        /// </summary>
        public const int RegistryVersion = 2;

        private readonly ITracer tracer;
        private readonly PhysicalFileSystem fileSystem;
        private readonly string registryDirectory;
        private readonly object instanceLock = new object();

        public LocalRepoRegistry(ITracer tracer, PhysicalFileSystem fileSystem, string registryDirectory)
        {
            ArgumentNullException.ThrowIfNull(tracer);
            ArgumentNullException.ThrowIfNull(fileSystem);
            ArgumentNullException.ThrowIfNull(registryDirectory);

            this.tracer = tracer;
            this.fileSystem = fileSystem;
            this.registryDirectory = registryDirectory;
        }

        /// <summary>
        /// Convenience factory for production callers: constructs an instance
        /// pointed at the platform's secure-data path for the GVFS.Service
        /// component, using a real <see cref="PhysicalFileSystem"/>. This is
        /// the same path the legacy service writes to, so register/unregister
        /// operations are wire-compatible regardless of whether the service
        /// is running.
        /// </summary>
        public static LocalRepoRegistry CreateForCurrentPlatform(ITracer tracer)
        {
            ArgumentNullException.ThrowIfNull(tracer);
            return new LocalRepoRegistry(
                tracer,
                new PhysicalFileSystem(),
                GVFSPlatform.Instance.GetSecureDataRootForGVFSComponent(ServiceDataDirName));
        }

        /// <summary>
        /// Idempotently records the given enlistment root as active. If an
        /// entry already exists, it is reactivated and its <c>OwnerSID</c>
        /// is updated to <paramref name="ownerSID"/>. Matches the semantics
        /// of <c>GVFS.Service.RepoRegistry.TryRegisterRepo</c>.
        /// </summary>
        public bool TryRegisterRepo(string repoRoot, string ownerSID, out string errorMessage)
        {
            ArgumentNullException.ThrowIfNull(repoRoot);

            errorMessage = null;
            try
            {
                lock (this.instanceLock)
                {
                    Dictionary<string, LocalRepoRegistration> all = this.ReadRegistry();
                    if (all.TryGetValue(repoRoot, out LocalRepoRegistration existing))
                    {
                        if (!existing.IsActive || !string.Equals(existing.OwnerSID, ownerSID, StringComparison.Ordinal))
                        {
                            existing.IsActive = true;
                            existing.OwnerSID = ownerSID;
                            this.WriteRegistry(all);
                        }
                    }
                    else
                    {
                        all[repoRoot] = new LocalRepoRegistration(repoRoot, ownerSID);
                        this.WriteRegistry(all);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                errorMessage = string.Format("Error while registering repo {0}: {1}", repoRoot, e);
                this.tracer.RelatedError(errorMessage);
                return false;
            }
        }

        /// <summary>
        /// Marks the given entry inactive (retained on disk so
        /// <see cref="LocalRepoRegistration.OwnerSID"/> is preserved for a
        /// possible later re-register). Returns <c>true</c> when the entry
        /// existed (whether or not it was already inactive); returns
        /// <c>false</c> when the entry was not found.
        /// </summary>
        public bool TryDeactivateRepo(string repoRoot, out string errorMessage)
        {
            ArgumentNullException.ThrowIfNull(repoRoot);

            errorMessage = null;
            try
            {
                lock (this.instanceLock)
                {
                    Dictionary<string, LocalRepoRegistration> all = this.ReadRegistry();
                    if (all.TryGetValue(repoRoot, out LocalRepoRegistration existing))
                    {
                        if (existing.IsActive)
                        {
                            existing.IsActive = false;
                            this.WriteRegistry(all);
                        }

                        return true;
                    }

                    errorMessage = string.Format("Attempted to deactivate non-existent repo at '{0}'", repoRoot);
                    return false;
                }
            }
            catch (Exception e)
            {
                errorMessage = string.Format("Error while deactivating repo {0}: {1}", repoRoot, e);
                this.tracer.RelatedError(errorMessage);
                return false;
            }
        }

        /// <summary>
        /// Removes the entry entirely (not just deactivates it). Returns
        /// <c>true</c> on success, <c>false</c> if no such entry existed.
        /// </summary>
        public bool TryRemoveRepo(string repoRoot, out string errorMessage)
        {
            ArgumentNullException.ThrowIfNull(repoRoot);

            errorMessage = null;
            try
            {
                lock (this.instanceLock)
                {
                    Dictionary<string, LocalRepoRegistration> all = this.ReadRegistry();
                    if (all.Remove(repoRoot))
                    {
                        this.WriteRegistry(all);
                        return true;
                    }

                    errorMessage = string.Format("Attempted to remove non-existent repo at '{0}'", repoRoot);
                    return false;
                }
            }
            catch (Exception e)
            {
                errorMessage = string.Format("Error while removing repo {0}: {1}", repoRoot, e);
                this.tracer.RelatedError(errorMessage);
                return false;
            }
        }

        /// <summary>
        /// Returns the entries currently marked active. Inactive entries are
        /// excluded. Returns an empty list when the registry file does not
        /// exist yet.
        /// </summary>
        public bool TryGetActiveRepos(out List<LocalRepoRegistration> repoList, out string errorMessage)
        {
            repoList = null;
            errorMessage = null;

            lock (this.instanceLock)
            {
                try
                {
                    Dictionary<string, LocalRepoRegistration> all = this.ReadRegistry();
                    repoList = all.Values.Where(r => r.IsActive).ToList();
                    return true;
                }
                catch (Exception e)
                {
                    errorMessage = string.Format("Unable to get list of active repos: {0}", e);
                    this.tracer.RelatedError(errorMessage);
                    return false;
                }
            }
        }

        /// <summary>
        /// Returns the in-memory map of all entries currently on disk
        /// (active and inactive). Intended for diagnostics and tests; most
        /// production callers should use <see cref="TryGetActiveRepos"/>.
        /// </summary>
        public Dictionary<string, LocalRepoRegistration> ReadRegistry()
        {
            Dictionary<string, LocalRepoRegistration> allRepos =
                new Dictionary<string, LocalRepoRegistration>(GVFSPlatform.Instance.Constants.PathComparer);

            string registryFilePath = Path.Combine(this.registryDirectory, RegistryFileName);

            using (Stream stream = this.fileSystem.OpenFileStream(
                    registryFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.Read,
                    FileShare.Read,
                    callFlushFileBuffers: false))
            using (StreamReader reader = new StreamReader(stream))
            {
                string versionString = reader.ReadLine();
                if (versionString == null)
                {
                    // Empty file - first write will populate it.
                    return allRepos;
                }

                if (!int.TryParse(versionString, out int version) || version > RegistryVersion)
                {
                    EventMetadata metadata = new EventMetadata
                    {
                        { "OnDiskVersion", versionString },
                        { "MaxSupportedVersion", RegistryVersion },
                    };
                    this.tracer.RelatedError(metadata, $"{nameof(this.ReadRegistry)}: Unsupported registry version; treating as empty");
                    return allRepos;
                }

                while (!reader.EndOfStream)
                {
                    string entry = reader.ReadLine();
                    if (string.IsNullOrEmpty(entry))
                    {
                        continue;
                    }

                    try
                    {
                        LocalRepoRegistration registration = LocalRepoRegistration.FromJson(entry);
                        if (registration != null && !string.IsNullOrEmpty(registration.EnlistmentRoot))
                        {
                            allRepos[registration.EnlistmentRoot] = registration;
                        }
                    }
                    catch (Exception e)
                    {
                        // Tolerate corruption of individual lines; matches
                        // RepoRegistry.ReadRegistry's behavior.
                        EventMetadata metadata = new EventMetadata
                        {
                            { "entry", entry },
                            { "Exception", e.ToString() },
                        };
                        this.tracer.RelatedError(metadata, $"{nameof(this.ReadRegistry)}: Failed to parse entry; skipping");
                    }
                }
            }

            return allRepos;
        }

        private void WriteRegistry(Dictionary<string, LocalRepoRegistration> registry)
        {
            // Ensure the directory exists. The service relies on its install
            // step to create %ProgramData%\GVFS\GVFS.Service; the user-level
            // path under %LocalAppData% may not exist yet when this runs.
            if (!this.fileSystem.DirectoryExists(this.registryDirectory))
            {
                this.fileSystem.CreateDirectory(this.registryDirectory);
            }

            string tempFilePath = Path.Combine(this.registryDirectory, RegistryTempName);
            string finalFilePath = Path.Combine(this.registryDirectory, RegistryFileName);

            using (Stream stream = this.fileSystem.OpenFileStream(
                    tempFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    callFlushFileBuffers: true))
            using (StreamWriter writer = new StreamWriter(stream))
            {
                writer.WriteLine(RegistryVersion);
                foreach (LocalRepoRegistration registration in registry.Values)
                {
                    writer.WriteLine(registration.ToJson());
                }

                stream.Flush();
            }

            this.fileSystem.MoveAndOverwriteFile(tempFilePath, finalFilePath);
        }
    }
}
