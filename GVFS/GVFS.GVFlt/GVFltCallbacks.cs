using Newtonsoft.Json;
using System;

namespace GVFS.GVFlt
{
    public class GVFltCallbacks
    {
        /// <summary>
        /// This struct must remain here for DiskLayout9to10Upgrade_BackgroundAndPlaceholderListToFileBased
        /// </summary>
        /// <remarks>
        /// This struct should only be used by the upgrader, it has been replaced by GVFS.Virtualization.Background.FileSystemTask
        /// </remarks>
        [Serializable]
        public struct BackgroundGitUpdate
        {
            // This enum must be present or the BinarySerializer will always deserialze Operation as 0
            public enum OperationType
            {
                Invalid = 0,
            }

            public OperationType Operation { get; set; }
            public string VirtualPath { get; set; }
            public string OldVirtualPath { get; set; }

            // Used by the logging in the upgrader
            public override string ToString()
            {
                return JsonConvert.SerializeObject(this);
            }
        }
    }
}
