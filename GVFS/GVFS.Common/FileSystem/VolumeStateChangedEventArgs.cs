using System;

namespace GVFS.Common.FileSystem
{
    public enum VolumeStateChangeType
    {
        /// <summary>
        /// The volume is now available and ready to use.
        /// </summary>
        VolumeAvailable,

        /// <summary>
        /// The volume is no longer available.
        /// </summary>
        VolumeUnavailable,
    }

    public class VolumeStateChangedEventArgs : EventArgs
    {
        public VolumeStateChangedEventArgs(string volumePath, VolumeStateChangeType changeType)
        {
            this.VolumePath = volumePath;
            this.ChangeType = changeType;
        }

        /// <summary>
        /// Path to the root of the volume that has changed.
        /// </summary>
        public string VolumePath { get; }

        /// <summary>
        /// Type of change.
        /// </summary>
        public VolumeStateChangeType ChangeType { get; }
    }
}