using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Management;
using GVFS.Common.FileSystem;

namespace GVFS.Platform.Windows
{
    public class WindowsVolumeStateWatcher : IVolumeStateWatcher
    {
        private readonly ManagementEventWatcher volumeWatcher;
        private readonly ManagementEventWatcher cryptoVolumeWatcher;
        private readonly IDictionary<string, bool> cryptoVolumeLockStatuses =
            new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public WindowsVolumeStateWatcher(TimeSpan pollingInterval)
        {
            int intervalSeconds = (int)pollingInterval.TotalSeconds;

            // Watch for mount and unmount of volumes
            string volumeQuery = $"SELECT * FROM Win32_VolumeChangeEvent WITHIN {intervalSeconds}";
            this.volumeWatcher = new ManagementEventWatcher(volumeQuery);
            this.volumeWatcher.EventArrived += this.OnVolumeEvent;

            // Watch for changes to BitLocker-protected volume states (unlock, lock, etc)
            string cryptoVolumeQuery = $"SELECT * FROM __InstanceModificationEvent WITHIN {intervalSeconds} WHERE TargetInstance ISA 'Win32_EncryptableVolume'";
            this.cryptoVolumeWatcher = new ManagementEventWatcher(BitLockerHelpers.VolumeEncryptionNamespace, cryptoVolumeQuery);
            this.cryptoVolumeWatcher.EventArrived += this.OnCryptoVolumeEvent;
        }

        public event EventHandler<VolumeStateChangedEventArgs> VolumeStateChanged;

        public void Start()
        {
            this.volumeWatcher.Start();
            this.cryptoVolumeWatcher.Start();
        }

        public void Stop()
        {
            this.cryptoVolumeWatcher.Stop();
            this.volumeWatcher.Stop();
        }

        public void Dispose()
        {
            this.Stop();

            this.volumeWatcher.EventArrived -= this.OnVolumeEvent;
            this.volumeWatcher.Dispose();

            this.cryptoVolumeWatcher.EventArrived -= this.OnCryptoVolumeEvent;
            this.cryptoVolumeWatcher.Dispose();
        }

        protected void RaiseVolumeStateChanged(string volumePath, VolumeStateChangeType changeType)
        {
            this.VolumeStateChanged?.Invoke(this, new VolumeStateChangedEventArgs(volumePath, changeType));
        }

        private void OnVolumeEvent(object sender, EventArrivedEventArgs e)
        {
            ManagementBaseObject mbo = e.NewEvent;

            var driveName = (string)mbo["DriveName"];
            var eventType = (ushort)mbo["EventType"];

            string volumePath = $@"{driveName}\";

            if (eventType == 2)
            {
                // Device Arrival
                // Check if the volume is not left locked by BitLocker on mount
                if (BitLockerHelpers.TryGetVolumeLockStatus(driveName, out bool isLocked) && !isLocked)
                {
                    this.RaiseVolumeStateChanged(volumePath, VolumeStateChangeType.VolumeAvailable);
                }
            }
            else if (eventType == 3)
            {
                // Device Removal
                this.RaiseVolumeStateChanged(volumePath, VolumeStateChangeType.VolumeUnavailable);
            }
        }

        private void OnCryptoVolumeEvent(object sender, EventArrivedEventArgs e)
        {
            ManagementBaseObject mbo = e.NewEvent;
            var targetMbo = (ManagementBaseObject)mbo["TargetInstance"];

            var driveName = (string)targetMbo?["DriveLetter"];
            if (driveName == null)
            {
                return;
            }

            string volumePath = $@"{driveName}\";

            // Get the new lock status of the volume
            // Note: we can only invoke the required "GetLockStatus" method on instances of type
            // ManagementObject, but management events only return instances of ManagementBaseObject.
            if (!BitLockerHelpers.TryGetVolumeLockStatus(driveName, out bool newLockStatus))
            {
                return;
            }

            // Get previous lock status
            // We only want to raise the 'VolumeStateChanged' event if the lock status has changed, but this event could
            // be raised for any modification to the BitLocker volume's configuration such as auto unlock on/off.
            // We track the previously seen lock statuses for all volumes so that we can detect changes to the locked status
            // and only raise the 'VolumeStateChanged' event in those cases.
            if (this.cryptoVolumeLockStatuses.TryGetValue(driveName, out bool prevLockStatus))
            {
                if (prevLockStatus && !newLockStatus)
                {
                    // Locked -> Unlocked
                    this.RaiseVolumeStateChanged(volumePath, VolumeStateChangeType.VolumeAvailable);
                }
                else if (!prevLockStatus && newLockStatus)
                {
                    // Unlocked -> Locked
                    this.RaiseVolumeStateChanged(volumePath, VolumeStateChangeType.VolumeUnavailable);
                }
            }
            else
            {
                // We've never seen this volume before (must have just been mounted).. report the current locked state as an event
                if (!newLockStatus)
                {
                    // Unlocked
                    this.RaiseVolumeStateChanged(volumePath, VolumeStateChangeType.VolumeAvailable);
                }
                else
                {
                    // Locked
                    this.RaiseVolumeStateChanged(volumePath, VolumeStateChangeType.VolumeUnavailable);
                }
            }

            // Update new lock status
            this.cryptoVolumeLockStatuses[driveName] = newLockStatus;
        }
    }
}
