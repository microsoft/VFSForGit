using System;
using GVFS.Common.FileSystem;

namespace GVFS.Platform.Mac
{
    public class MacVolumeStateWatcher : IVolumeStateWatcher
    {
        public event EventHandler<VolumeStateChangedEventArgs> VolumeStateChanged;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }

        protected void RaiseVolumeStateChanged(string volumePath, VolumeStateChangeType changeType)
        {
            this.VolumeStateChanged?.Invoke(this, new VolumeStateChangedEventArgs(volumePath, changeType));
        }
    }
}
