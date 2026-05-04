using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GVFS.Platform.Windows
{
    /// <summary>
    /// Collects physical disk telemetry using P/Invoke (kernel32 + DeviceIoControl)
    /// instead of System.Management/WMI, which requires COM interop incompatible
    /// with NativeAOT.
    /// </summary>
    public class WindowsPhysicalDiskInfo
    {
        private static readonly Dictionary<uint, string> MapDriveType = new Dictionary<uint, string>()
        {
            { 0, "unknown" },
            { 1, "InvalidRootPath" },
            { 2, "Removable" },
            { 3, "Fixed" },
            { 4, "Remote" },
            { 5, "CDROM" },
            { 6, "RAMDisk" },
        };

        private static readonly Dictionary<int, string> MapBusType = new Dictionary<int, string>()
        {
            { 0, "unknown" },
            { 1, "SCSI" },
            { 2, "ATAPI" },
            { 3, "ATA" },
            { 4, "1394" },
            { 5, "SSA" },
            { 6, "FibreChannel" },
            { 7, "USB" },
            { 8, "RAID" },
            { 9, "iSCSI" },
            { 10, "SAS" },
            { 11, "SATA" },
            { 12, "SD" },
            { 13, "MMC" },
            { 14, "Virtual" },
            { 15, "FileBackedVirtual" },
            { 16, "StorageSpaces" },
            { 17, "NVMe" },
        };

        #region P/Invoke constants

        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

        private const int StorageAdapterProperty = 1;
        private const int StorageDeviceSeekPenaltyProperty = 7;

        private const int PropertyStandardQuery = 0;

        #endregion

        #region P/Invoke declarations

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetDriveType(string lpRootPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumeInformation(
            string lpRootPathName,
            char[] lpVolumeNameBuffer,
            int nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            char[] lpFileSystemNameBuffer,
            int nFileSystemNameSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetDiskFreeSpaceEx(
            string lpDirectoryName,
            out ulong lpFreeBytesAvailableToCaller,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            ref StoragePropertyQuery lpInBuffer,
            int nInBufferSize,
            IntPtr lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            int nInBufferSize,
            IntPtr lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        #endregion

        #region Native structs

        [StructLayout(LayoutKind.Sequential)]
        private struct StoragePropertyQuery
        {
            public int PropertyId;
            public int QueryType;
            public byte AdditionalParameters;
        }

        #endregion

        /// <summary>
        /// Get the properties of the drive/volume/partition/physical disk associated
        /// with the given pathname.  For example, whether the drive is an SSD or HDD.
        ///
        /// Uses direct P/Invoke calls (GetDriveType, GetVolumeInformation,
        /// GetDiskFreeSpaceEx, DeviceIoControl) instead of WMI so the code is
        /// compatible with NativeAOT compilation.
        /// </summary>
        /// <returns>A dictionary of platform-specific keywords and values.</returns>
        public static Dictionary<string, string> GetPhysicalDiskInfo(string path, bool sizeStatsOnly)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();

            try
            {
                char driveLetter = PathToDriveLetter(path);
                result["DriveLetter"] = driveLetter.ToString();

                string rootPath = $"{driveLetter}:\\";

                uint driveType = GetDriveType(rootPath);
                result["VolumeDriveType"] = MapDriveType.TryGetValue(driveType, out string dtName)
                    ? dtName
                    : driveType.ToString();

                CollectVolumeInfo(rootPath, result);
                CollectVolumeSizeInfo(rootPath, result);

                if (sizeStatsOnly)
                {
                    return result;
                }

                CollectPhysicalDiskProperties(driveLetter, result);
            }
            catch (Exception e)
            {
                result["Error"] = e.Message;
            }

            return result;
        }

        private static void CollectVolumeInfo(string rootPath, Dictionary<string, string> result)
        {
            char[] volumeLabel = new char[261];
            char[] fileSystemName = new char[261];

            if (GetVolumeInformation(
                rootPath,
                volumeLabel,
                volumeLabel.Length,
                out _,
                out _,
                out _,
                fileSystemName,
                fileSystemName.Length))
            {
                result["VolumeFileSystem"] = new string(fileSystemName).TrimEnd('\0');
                result["VolumeFileSystemLabel"] = new string(volumeLabel).TrimEnd('\0');
            }
            else
            {
                result["VolumeFileSystem"] = "unknown";
                result["VolumeFileSystemLabel"] = "unknown";
            }
        }

        private static void CollectVolumeSizeInfo(string rootPath, Dictionary<string, string> result)
        {
            if (GetDiskFreeSpaceEx(rootPath, out _, out ulong totalBytes, out ulong freeBytes))
            {
                result["VolumeSize"] = totalBytes.ToString();
                result["VolumeSizeRemaining"] = freeBytes.ToString();
            }
            else
            {
                result["VolumeSize"] = "unknown";
                result["VolumeSizeRemaining"] = "unknown";
            }
        }

        /// <summary>
        /// Opens the volume handle, resolves the physical disk number via
        /// IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, then queries the physical disk
        /// for seek-penalty (SSD vs HDD) and bus type via IOCTL_STORAGE_QUERY_PROPERTY.
        /// </summary>
        private static void CollectPhysicalDiskProperties(char driveLetter, Dictionary<string, string> result)
        {
            string volumePath = $@"\\.\{driveLetter}:";
            using SafeFileHandle volumeHandle = CreateFile(
                volumePath,
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (volumeHandle.IsInvalid)
            {
                result["DiskNumber"] = "unknown";
                result["PhysicalMediaType"] = "unknown";
                result["PhysicalBusType"] = "unknown";
                return;
            }

            int diskNumber = GetDiskNumberFromVolume(volumeHandle);
            if (diskNumber < 0)
            {
                result["DiskNumber"] = "unknown";
                result["PhysicalMediaType"] = "unknown";
                result["PhysicalBusType"] = "unknown";
                return;
            }

            result["DiskNumber"] = diskNumber.ToString();

            string diskPath = $@"\\.\PhysicalDrive{diskNumber}";
            using SafeFileHandle diskHandle = CreateFile(
                diskPath,
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (diskHandle.IsInvalid)
            {
                result["PhysicalMediaType"] = "unknown";
                result["PhysicalBusType"] = "unknown";
                return;
            }

            result["PhysicalMediaType"] = QueryMediaType(diskHandle);
            result["PhysicalBusType"] = QueryBusType(diskHandle);
        }

        /// <summary>
        /// Uses IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS to determine which physical
        /// disk number backs the given volume.
        /// </summary>
        private static int GetDiskNumberFromVolume(SafeFileHandle volumeHandle)
        {
            const int bufferSize = 256;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                if (DeviceIoControl(
                        volumeHandle,
                        IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                        IntPtr.Zero,
                        0,
                        buffer,
                        bufferSize,
                        out _,
                        IntPtr.Zero))
                {
                    int count = Marshal.ReadInt32(buffer, 0);
                    if (count > 0)
                    {
                        return Marshal.ReadInt32(buffer, 8);
                    }
                }

                return -1;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Queries StorageDeviceSeekPenaltyProperty via DeviceIoControl.
        /// No seek penalty means SSD; seek penalty means HDD.
        /// </summary>
        private static string QueryMediaType(SafeFileHandle diskHandle)
        {
            StoragePropertyQuery query = new StoragePropertyQuery
            {
                PropertyId = StorageDeviceSeekPenaltyProperty,
                QueryType = PropertyStandardQuery,
            };

            const int outSize = 32;
            IntPtr buffer = Marshal.AllocHGlobal(outSize);
            try
            {
                if (DeviceIoControl(
                        diskHandle,
                        IOCTL_STORAGE_QUERY_PROPERTY,
                        ref query,
                        Marshal.SizeOf<StoragePropertyQuery>(),
                        buffer,
                        outSize,
                        out _,
                        IntPtr.Zero))
                {
                    byte penalty = Marshal.ReadByte(buffer, 8);
                    return penalty != 0 ? "HDD" : "SSD";
                }

                return "unknown";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Queries StorageAdapterProperty via DeviceIoControl to read the
        /// STORAGE_BUS_TYPE from the STORAGE_ADAPTER_DESCRIPTOR.
        /// </summary>
        private static string QueryBusType(SafeFileHandle diskHandle)
        {
            StoragePropertyQuery query = new StoragePropertyQuery
            {
                PropertyId = StorageAdapterProperty,
                QueryType = PropertyStandardQuery,
            };

            const int outSize = 256;
            IntPtr buffer = Marshal.AllocHGlobal(outSize);
            try
            {
                if (DeviceIoControl(
                        diskHandle,
                        IOCTL_STORAGE_QUERY_PROPERTY,
                        ref query,
                        Marshal.SizeOf<StoragePropertyQuery>(),
                        buffer,
                        outSize,
                        out _,
                        IntPtr.Zero))
                {
                    int busType = Marshal.ReadByte(buffer, 24);
                    return MapBusType.TryGetValue(busType, out string busName)
                        ? busName
                        : busType.ToString();
                }

                return "unknown";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static char PathToDriveLetter(string path)
        {
            FileInfo fi = new FileInfo(path);
            string drive = Path.GetPathRoot(fi.FullName);
            if ((drive.Length == 3) && (drive[1] == ':') && (drive[2] == '\\'))
            {
                if ((drive[0] >= 'A') && (drive[0] <= 'Z'))
                {
                    return drive[0];
                }

                if ((drive[0] >= 'a') && (drive[0] <= 'z'))
                {
                    return char.ToUpper(drive[0]);
                }
            }

            throw new ArgumentException($"Could not map path '{path}' to a drive letter.");
        }
    }
}
