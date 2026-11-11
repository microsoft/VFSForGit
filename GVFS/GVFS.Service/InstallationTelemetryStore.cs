using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using GVFS.Platform.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace GVFS.Service
{
    public sealed class InstallationTelemetryStore
    {
        public const string DirectoryName = "InstallationTelemetry";
        public const int MaxRecordBytes = 4096;
        public const int MaxRecords = 256;
        public const int MaxTotalBytes = 1024 * 1024;
        public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

        private readonly string directory;
        private readonly ITracer tracer;
        private readonly TimeProvider clock;
        private readonly PhysicalFileSystem fileSystem;
        private readonly InstallationTelemetrySecurity security;

        public InstallationTelemetryStore(
            string directory,
            ITracer tracer,
            TimeProvider clock = null,
            PhysicalFileSystem fileSystem = null,
            InstallationTelemetrySecurity security = null)
        {
            ArgumentNullException.ThrowIfNull(directory);
            ArgumentNullException.ThrowIfNull(tracer);
            this.directory = Path.GetFullPath(directory);
            this.tracer = tracer;
            this.clock = clock ?? TimeProvider.System;
            this.fileSystem = fileSystem ?? new PhysicalFileSystem();
            this.security = security ?? new InstallationTelemetrySecurity();
            this.Health = new InstallationTelemetryHealth(tracer, this.clock);
        }

        internal InstallationTelemetryHealth Health { get; }

        public List<KeyValuePair<string, InstallationTelemetryRecord>> ReadBatch(CancellationToken cancellationToken = default)
        {
            List<KeyValuePair<string, InstallationTelemetryRecord>> records = new List<KeyValuePair<string, InstallationTelemetryRecord>>();
            if (!this.fileSystem.DirectoryExists(this.directory))
            {
                return records;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.security.VerifyDirectory(this.directory);
                DateTimeOffset now = this.clock.GetUtcNow();
                KeyValuePair<string, FileProperties>[] files = this.fileSystem.GetFiles(this.directory, "*")
                    .Where(file => IsRecordName(Path.GetFileName(file)))
                    .Select(file => new KeyValuePair<string, FileProperties>(file, this.fileSystem.GetFileProperties(file)))
                    .OrderBy(file => file.Value.LastWriteTimeUTC)
                    .ThenBy(file => file.Key, StringComparer.Ordinal)
                    .ToArray();
                List<KeyValuePair<string, FileProperties>> retained = new List<KeyValuePair<string, FileProperties>>();
                foreach (KeyValuePair<string, FileProperties> file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if ((file.Value.FileAttributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    {
                        this.Warn("Rejected a reparse point in the installation telemetry directory.");
                        continue;
                    }

                    if (file.Value.LastWriteTimeUTC < now.Subtract(Retention).UtcDateTime || file.Value.Length > MaxRecordBytes)
                    {
                        this.Delete(file.Key, "Expired or excessive installation telemetry record.");
                        continue;
                    }

                    retained.Add(file);
                }

                long totalBytes = retained.Sum(file => file.Value.Length);
                int count = retained.Count;
                foreach (KeyValuePair<string, FileProperties> file in retained)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (count > MaxRecords || totalBytes > MaxTotalBytes)
                    {
                        totalBytes -= file.Value.Length;
                        count--;
                        this.Delete(file.Key, "Excessive installation telemetry record.");
                        continue;
                    }

                    if (Path.GetExtension(file.Key) != ".json")
                    {
                        continue;
                    }

                    try
                    {
                        this.security.VerifyFile(file.Key);
                        InstallationTelemetryRecord record;
                        using (Stream stream = this.fileSystem.OpenFileStream(
                            file.Key, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, callFlushFileBuffers: false))
                        {
                            if (stream.Length > MaxRecordBytes)
                            {
                                throw new InvalidDataException("Installation telemetry record exceeds the size limit.");
                            }

                            record = JsonSerializer.Deserialize(stream, ServiceJsonContext.Default.InstallationTelemetryRecord);
                        }

                        if (record != null && record.SchemaVersion != 1)
                        {
                            this.Warn("Installation telemetry record has an unsupported schema version.");
                            continue;
                        }

                        if (record == null || !record.IsValid(now)
                            || Path.GetFileNameWithoutExtension(file.Key) != record.EventId.ToString("D"))
                        {
                            throw new InvalidDataException("Installation telemetry record has invalid fields.");
                        }

                        if (record.OccurredUtc < now.Subtract(Retention))
                        {
                            this.Delete(file.Key, "Expired installation telemetry event.");
                            continue;
                        }

                        records.Add(new KeyValuePair<string, InstallationTelemetryRecord>(file.Key, record));
                    }
                    catch (FileNotFoundException)
                    {
                        // The producer can prune an immutable record during a scan.
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                        || ex is JsonException || ex is InvalidDataException)
                    {
                        this.Warn(ex.ToString());
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                || ex is InvalidDataException || ex is System.Security.SecurityException)
            {
                this.Warn(ex.ToString());
            }

            return records;
        }

        public void Complete(string path)
        {
            this.Delete(path, null);
        }

        private static bool IsRecordName(string name)
        {
            string extension = Path.GetExtension(name);
            return (extension == ".json" || extension == ".tmp")
                && Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "D", out Guid id);
        }

        private void Delete(string path, string reason)
        {
            try
            {
                this.fileSystem.DeleteFile(path);
                if (reason != null)
                {
                    this.tracer.RelatedInfo(nameof(InstallationTelemetryStore) + ": " + reason);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                this.Warn(ex.ToString());
            }
        }

        private void Warn(string message)
        {
            this.Health.ReportFailure(InstallationTelemetryHealth.FailureReason.StoreRejected, message);
        }
    }
}
