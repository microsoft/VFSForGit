using GVFS.Common.Tracing;
using System;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        internal abstract class FolderEntryData
        {
            public LazyUTF8String Name { get; protected set; }
            public abstract bool IsFolder { get; }

            protected static EventMetadata CreateEventMetadata(Exception e = null)
            {
                EventMetadata metadata = new EventMetadata();
                metadata.Add("Area", nameof(FolderEntryData));
                if (e != null)
                {
                    metadata.Add("Exception", e.ToString());
                }

                return metadata;
            }
        }
    }
}
