using System.Collections.Generic;

namespace GVFS.Common.Tracing
{
    // This is a convenience class to make code around event metadata look nicer.
    // It's more obvious to see EventMetadata than Dictionary<string, object> everywhere.
    public class EventMetadata : Dictionary<string, object>
    {
        public EventMetadata()
        {
        }

        public EventMetadata(Dictionary<string, object> metadata)
            : base(metadata)
        {
        }
    }
}
