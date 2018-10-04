using System;

namespace GVFS.Common.FileSystem
{
    /// <summary>
    /// Monitors the system for changes to the state of any disk volume, notifying subscribers when a change occurs.
    /// </summary>
    public interface IVolumeStateWatcher : IDisposable
    {
        /// <summary>
        /// Raised when the state of a volume has changed, such as becoming available.
        /// </summary>
        event EventHandler<VolumeStateChangedEventArgs> VolumeStateChanged;

        /// <summary>
        /// Start watching for changes to the states of volumes on the system.
        /// </summary>
        void Start();

        /// <summary>
        /// Stop watching for changes to the states of volumes on the system.
        /// </summary>
        void Stop();
    }
}
