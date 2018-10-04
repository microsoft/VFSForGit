using System;
using System.Linq;
using System.Management;

namespace GVFS.Platform.Windows
{
    internal static class BitLockerHelpers
    {
        public const string VolumeEncryptionNamespace = @"root\cimv2\security\MicrosoftVolumeEncryption";

        public static bool TryGetVolumeLockStatus(string driveName, out bool isLocked)
        {
            string queryString = $"SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter = '{driveName}'";

            using (var searcher = new ManagementObjectSearcher(VolumeEncryptionNamespace, queryString))
            {
                ManagementObjectCollection results = searcher.Get();
                ManagementObject mo = results.OfType<ManagementObject>().FirstOrDefault();
                if (mo != null)
                {
                    // Check the protection status
                    // ProtectionStatus == 0 means the drive is not protected by BitLocker and therefore cannot be locked
                    if (mo.Properties["ProtectionStatus"].Value is uint protectedInt && protectedInt == 0)
                    {
                        isLocked = false;
                        return true;
                    }

                    // Check the lock status
                    // Lock status is not a property and must be retrieved by the GetLockStatus method
                    var args = new object[1];
                    if (TryInvokeMethod(mo, "GetLockStatus", args) && args[0] is uint lockedInt)
                    {
                        // Unlocked
                        if (lockedInt == 0)
                        {
                            isLocked = false;
                            return true;
                        }

                        // Locked
                        if (lockedInt == 1)
                        {
                            isLocked = true;
                            return true;
                        }
                    }
                }
            }

            isLocked = false;
            return false;
        }

        private static bool TryInvokeMethod(ManagementObject mo, string methodName, object[] args)
        {
            try
            {
                return mo.InvokeMethod(methodName, args) is uint result && result == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
