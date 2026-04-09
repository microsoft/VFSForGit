using GVFS.Common.FileSystem;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GVFS.Common
{
    public class LocalCacheResolver
    {
        private const string EtwArea = nameof(LocalCacheResolver);
        private const string MappingFile = "mapping.dat";
        private const string MappingVersionKey = "GVFS_LocalCache_MappingVersion";
        private const string CurrentMappingDataVersion = "1";

        private GVFSEnlistment enlistment;
        private PhysicalFileSystem fileSystem;

        public LocalCacheResolver(GVFSEnlistment enlistment, PhysicalFileSystem fileSystem = null)
        {
            this.fileSystem = fileSystem ?? new PhysicalFileSystem();
            this.enlistment = enlistment;
        }

        public static bool TryGetDefaultLocalCacheRoot(GVFSEnlistment enlistment, out string localCacheRoot, out string localCacheRootError)
        {
            if (GVFSEnlistment.IsUnattended(tracer: null))
            {
                localCacheRoot = Path.Combine(enlistment.DotGVFSRoot, GVFSConstants.DefaultGVFSCacheFolderName);
                localCacheRootError = null;
                return true;
            }

            return GVFSPlatform.Instance.TryGetDefaultLocalCacheRoot(enlistment.EnlistmentRoot, out localCacheRoot, out localCacheRootError);
        }

        public bool TryGetLocalCacheKeyFromLocalConfigOrRemoteCacheServers(
            ITracer tracer,
            ServerGVFSConfig serverGVFSConfig,
            CacheServerInfo currentCacheServer,
            string localCacheRoot,
            out string localCacheKey,
            out string errorMessage)
        {
            if (serverGVFSConfig == null)
            {
                throw new ArgumentNullException(nameof(serverGVFSConfig));
            }

            localCacheKey = null;
            errorMessage = string.Empty;

            try
            {
                // A lock is required because FileBasedDictionary is not multi-process safe, neither is the act of adding a new cache
                string lockPath = Path.Combine(localCacheRoot, MappingFile + ".lock");

                string createDirectoryError;
                if (!GVFSPlatform.Instance.FileSystem.TryCreateDirectoryAccessibleByAuthUsers(localCacheRoot, out createDirectoryError, tracer))
                {
                    errorMessage = $"Failed to create '{localCacheRoot}': {createDirectoryError}";
                    return false;
                }

                using (FileBasedLock mappingLock = GVFSPlatform.Instance.CreateFileBasedLock(
                    this.fileSystem,
                    tracer,
                    lockPath))
                {
                    if (!this.TryAcquireLockWithRetries(tracer, mappingLock))
                    {
                        errorMessage = "Failed to acquire lock file at " + lockPath;
                        tracer.RelatedError(nameof(this.TryGetLocalCacheKeyFromLocalConfigOrRemoteCacheServers) + ": " + errorMessage);
                        return false;
                    }

                    FileBasedDictionary<string, string> mappingFile;
                    if (this.TryOpenMappingFile(tracer, localCacheRoot, out mappingFile, out errorMessage))
                    {
                        try
                        {
                            string mappingDataVersion;
                            if (mappingFile.TryGetValue(MappingVersionKey, out mappingDataVersion))
                            {
                                if (mappingDataVersion != CurrentMappingDataVersion)
                                {
                                    errorMessage = string.Format("Mapping file has different version than expected: {0} Actual: {1}", CurrentMappingDataVersion, mappingDataVersion);
                                    tracer.RelatedError(nameof(this.TryGetLocalCacheKeyFromLocalConfigOrRemoteCacheServers) + ": " + errorMessage);
                                    return false;
                                }
                            }
                            else
                            {
                                mappingFile.SetValueAndFlush(MappingVersionKey, CurrentMappingDataVersion);
                            }

                            if (mappingFile.TryGetValue(this.ToMappingKey(this.enlistment.RepoUrl), out localCacheKey) ||
                               (currentCacheServer.HasValidUrl() && mappingFile.TryGetValue(this.ToMappingKey(currentCacheServer.Url), out localCacheKey)))
                            {
                                EventMetadata metadata = CreateEventMetadata();
                                metadata.Add("localCacheKey", localCacheKey);
                                metadata.Add("this.enlistment.RepoUrl", this.enlistment.RepoUrl);
                                metadata.Add("currentCacheServer", currentCacheServer.ToString());
                                metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.TryGetLocalCacheKeyFromLocalConfigOrRemoteCacheServers) + ": Found existing local cache key");
                                tracer.RelatedEvent(EventLevel.Informational, "LocalCacheResolver_ExistingKey", metadata);

                                return true;
                            }
                            else
                            {
                                EventMetadata metadata = CreateEventMetadata();
                                metadata.Add("this.enlistment.RepoUrl", this.enlistment.RepoUrl);
                                metadata.Add("currentCacheServer", currentCacheServer.ToString());

                                string getLocalCacheKeyError;
                                if (this.TryGetLocalCacheKeyFromRemoteCacheServers(tracer, serverGVFSConfig, currentCacheServer, mappingFile, out localCacheKey, out getLocalCacheKeyError))
                                {
                                    metadata.Add("localCacheKey", localCacheKey);
                                    metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.TryGetLocalCacheKeyFromLocalConfigOrRemoteCacheServers) + ": Generated new local cache key");
                                    tracer.RelatedEvent(EventLevel.Informational, "LocalCacheResolver_NewKey", metadata);
                                    return true;
                                }

                                metadata.Add("getLocalCacheKeyError", getLocalCacheKeyError);
                                tracer.RelatedError(metadata, nameof(this.TryGetLocalCacheKeyFromLocalConfigOrRemoteCacheServers) + ": TryGetLocalCacheKeyFromRemoteCacheServers failed");

                                errorMessage = "Failed to generate local cache key";
                                return false;
                            }
                        }
                        finally
                        {
                            mappingFile.Dispose();
                        }
                    }

                    return false;
                }
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                metadata.Add("this.enlistment.RepoUrl", this.enlistment.RepoUrl);
                metadata.Add("currentCacheServer", currentCacheServer.ToString());
                tracer.RelatedError(metadata, nameof(this.TryGetLocalCacheKeyFromLocalConfigOrRemoteCacheServers) + ": Caught exception");

                errorMessage = string.Format("Exception while getting local cache key: {0}", e.Message);
                return false;
            }
        }

        private static EventMetadata CreateEventMetadata(Exception e = null)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Area", EtwArea);
            if (e != null)
            {
                metadata.Add("Exception", e.ToString());
            }

            return metadata;
        }

        private bool TryOpenMappingFile(ITracer tracer, string localCacheRoot, out FileBasedDictionary<string, string> mappingFile, out string errorMessage)
        {
            mappingFile = null;
            errorMessage = string.Empty;

            string error;
            string mappingFilePath = Path.Combine(localCacheRoot, MappingFile);
            if (!FileBasedDictionary<string, string>.TryCreate(
                tracer,
                mappingFilePath,
                this.fileSystem,
                out mappingFile,
                out error))
            {
                errorMessage = "Could not open mapping file for local cache: " + error;

                EventMetadata metadata = CreateEventMetadata();
                metadata.Add("mappingFilePath", mappingFilePath);
                metadata.Add("error", error);
                tracer.RelatedError(metadata, "TryOpenMappingFile: Could not open mapping file for local cache");

                return false;
            }

            return true;
        }

        private bool TryGetLocalCacheKeyFromRemoteCacheServers(
            ITracer tracer,
            ServerGVFSConfig serverGVFSConfig,
            CacheServerInfo currentCacheServer,
            FileBasedDictionary<string, string> mappingFile,
            out string localCacheKey,
            out string error)
        {
            error = null;
            localCacheKey = null;

            try
            {
                if (this.TryFindExistingLocalCacheKey(mappingFile, serverGVFSConfig.CacheServers, out localCacheKey))
                {
                    EventMetadata metadata = CreateEventMetadata();
                    metadata.Add("currentCacheServer", currentCacheServer.ToString());
                    metadata.Add("localCacheKey", localCacheKey);
                    metadata.Add("this.enlistment.RepoUrl", this.enlistment.RepoUrl);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.TryGetLocalCacheKeyFromRemoteCacheServers) + ": Found an existing a local key by cross referencing");
                    tracer.RelatedEvent(EventLevel.Informational, "LocalCacheResolver_ExistingKeyFromCrossReferencing", metadata);
                }
                else
                {
                    localCacheKey = Guid.NewGuid().ToString("N");

                    EventMetadata metadata = CreateEventMetadata();
                    metadata.Add("currentCacheServer", currentCacheServer.ToString());
                    metadata.Add("localCacheKey", localCacheKey);
                    metadata.Add("this.enlistment.RepoUrl", this.enlistment.RepoUrl);
                    metadata.Add(TracingConstants.MessageKey.InfoMessage, nameof(this.TryGetLocalCacheKeyFromRemoteCacheServers) + ": Generated a new local key after cross referencing");
                    tracer.RelatedEvent(EventLevel.Informational, "LocalCacheResolver_NewKeyAfterCrossReferencing", metadata);
                }

                List<KeyValuePair<string, string>> mappingFileUpdates = new List<KeyValuePair<string, string>>();

                mappingFileUpdates.Add(new KeyValuePair<string, string>(this.ToMappingKey(this.enlistment.RepoUrl), localCacheKey));

                if (currentCacheServer.HasValidUrl())
                {
                    mappingFileUpdates.Add(new KeyValuePair<string, string>(this.ToMappingKey(currentCacheServer.Url), localCacheKey));
                }

                foreach (CacheServerInfo cacheServer in serverGVFSConfig.CacheServers)
                {
                    string persistedLocalCacheKey;
                    if (mappingFile.TryGetValue(this.ToMappingKey(cacheServer.Url), out persistedLocalCacheKey))
                    {
                        if (!string.Equals(persistedLocalCacheKey, localCacheKey, StringComparison.OrdinalIgnoreCase))
                        {
                            EventMetadata metadata = CreateEventMetadata();
                            metadata.Add("cacheServer", cacheServer.ToString());
                            metadata.Add("persistedLocalCacheKey", persistedLocalCacheKey);
                            metadata.Add("localCacheKey", localCacheKey);
                            metadata.Add("currentCacheServer", currentCacheServer.ToString());
                            metadata.Add("this.enlistment.RepoUrl", this.enlistment.RepoUrl);
                            tracer.RelatedWarning(metadata, nameof(this.TryGetLocalCacheKeyFromRemoteCacheServers) + ": Overwriting persisted cache key with new value");

                            mappingFileUpdates.Add(new KeyValuePair<string, string>(this.ToMappingKey(cacheServer.Url), localCacheKey));
                        }
                    }
                    else
                    {
                        mappingFileUpdates.Add(new KeyValuePair<string, string>(this.ToMappingKey(cacheServer.Url), localCacheKey));
                    }
                }

                mappingFile.SetValuesAndFlush(mappingFileUpdates);
            }
            catch (Exception e)
            {
                EventMetadata metadata = CreateEventMetadata(e);
                tracer.RelatedError(metadata, nameof(this.TryGetLocalCacheKeyFromRemoteCacheServers) + ": Caught exception while getting local key");
                error = string.Format("Exception while getting local cache key: {0}", e.Message);
                return false;
            }

            return true;
        }

        private bool TryAcquireLockWithRetries(ITracer tracer, FileBasedLock mappingLock)
        {
            const int NumRetries = 100;
            const int WaitTimeMs = 100;

            for (int i = 0; i < NumRetries; ++i)
            {
                if (mappingLock.TryAcquireLock())
                {
                    return true;
                }
                else if (i < NumRetries - 1)
                {
                    Thread.Sleep(WaitTimeMs);

                    if (i % 20 == 0)
                    {
                        tracer.RelatedInfo("Waiting to acquire local cacke metadata lock file");
                    }
                }
            }

            return false;
        }

        private string ToMappingKey(string url)
        {
            return url.ToLowerInvariant();
        }

        private bool TryFindExistingLocalCacheKey(FileBasedDictionary<string, string> mappings, IEnumerable<CacheServerInfo> cacheServers, out string localCacheKey)
        {
            foreach (CacheServerInfo cacheServer in cacheServers)
            {
                if (mappings.TryGetValue(this.ToMappingKey(cacheServer.Url), out localCacheKey))
                {
                    return true;
                }
            }

            localCacheKey = null;
            return false;
        }
    }
}
