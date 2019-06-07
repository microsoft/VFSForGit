using GVFS.Common.FileSystem;
using GVFS.Common.Tracing;
using System;
using System.IO;

namespace GVFS.Common
{
    public abstract class ProductUpgraderPlatformStrategy
    {
        public ProductUpgraderPlatformStrategy(PhysicalFileSystem fileSystem, ITracer tracer)
        {
            this.FileSystem = fileSystem;
            this.Tracer = tracer;
        }

        protected PhysicalFileSystem FileSystem { get; }
        protected ITracer Tracer { get; }

        public abstract bool TryPrepareLogDirectory(out string error);

        public abstract bool TryPrepareApplicationDirectory(out string error);

        public abstract bool TryPrepareDownloadDirectory(out string error);

        protected void TraceException(Exception exception, string method, string message)
        {
            this.TraceException(this.Tracer, exception, method, message);
        }

        protected void TraceException(ITracer tracer, Exception exception, string method, string message)
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("Method", method);
            metadata.Add("Exception", exception.ToString());
            tracer.RelatedError(metadata, message);
        }
    }
}
