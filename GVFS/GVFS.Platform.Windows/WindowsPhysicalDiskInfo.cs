using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

namespace GVFS.Platform.Windows
{
    public class WindowsPhysicalDiskInfo
    {
        private static readonly Dictionary<int, string> MapBusType = new Dictionary<int, string>()
        {
            { 0, "unknwon" },
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

        private static readonly Dictionary<int, string> MapMediaType = new Dictionary<int, string>()
        {
            { 0, "unspecified" },
            { 3, "HDD" },
            { 4, "SSD" },
            { 5, "SCM" },
        };

        private static readonly Dictionary<int, string> MapDriveType = new Dictionary<int, string>()
        {
            { 0, "unknown" },
            { 1, "InvalidRootPath" },
            { 2, "Removable" },
            { 3, "Fixed" },
            { 4, "Remote" },
            { 5, "CDROM" },
            { 6, "RAMDisk" },
        };

        /// <summary>
        /// Get the properties of the drive/volume/partition/physical disk associated
        /// the given pathname.  For example, whether the drive is an SSD or HDD.
        /// </summary>
        /// <returns>A dictionary of platform-specific keywords and values.</returns>
        public static Dictionary<string, string> GetPhysicalDiskInfo(string path, bool sizeStatsOnly)
        {
            // Use the WMI APIs to get details about the physical disk associated with the given path.
            // Some of these fields are avilable using normal classes, such as System.IO.DriveInfo:
            // https://msdn.microsoft.com/en-us/library/system.io.driveinfo(v=vs.110).aspx
            //
            // But the lower-level fields, such as the BusType and SpindleSpeed, are not.
            //
            // MSFT_Partition:
            // https://msdn.microsoft.com/en-us/library/windows/desktop/hh830524(v=vs.85).aspx
            //
            // MSFT_Disk:
            // https://msdn.microsoft.com/en-us/library/windows/desktop/hh830493(v=vs.85).aspx
            //
            // MSFT_Volume:
            // https://msdn.microsoft.com/en-us/library/windows/desktop/hh830604(v=vs.85).aspx
            //
            // MSFT_PhysicalDisk:
            // https://msdn.microsoft.com/en-us/library/windows/desktop/hh830532(v=vs.85)
            //
            // An overview of these "classes" can be found here:
            // https://msdn.microsoft.com/en-us/library/hh830612.aspx
            //
            // The map variables defined above are based on property values documented in one of the above APIs.
            // There are helper functions below to convert from ManagementBaseObject values into the map values.
            // These do not do strict validation because the OS can add new values at any time.  For example, the
            // integer code for NVMe bus drives was recently added.  If an unrecognized value is received, the
            // raw integer value is used untranslated.
            //
            // They are accessed via a generic WQL language that is similar to SQL.  See here for an example:
            // https://blogs.technet.microsoft.com/josebda/2014/08/11/sample-c-code-for-using-the-latest-wmi-classes-to-manage-windows-storage/

            Dictionary<string, string> result = new Dictionary<string, string>();

            try
            {
                char driveLetter = PathToDriveLetter(path);
                result.Add("DriveLetter", driveLetter.ToString());

                ManagementScope scope = new ManagementScope(@"\\.\root\microsoft\windows\storage");
                scope.Connect();

                DiskSizeStatistics(scope, driveLetter, ref result);

                if (sizeStatsOnly)
                {
                    return result;
                }

                DiskTypeInfo(scope, driveLetter, ref result);
            }
            catch (Exception e)
            {
                result.Add("Error", e.Message);
            }

            return result;
        }

        private static void DiskSizeStatistics(ManagementScope scope, char driveLetter, ref Dictionary<string, string> result)
        {
            string queryVolumeString = $"SELECT DriveType,FileSystem,FileSystemLabel,Size,SizeRemaining FROM MSFT_Volume WHERE DriveLetter=\"{driveLetter}\"";
            ManagementBaseObject mbo = GetFirstRecord(scope, queryVolumeString);
            if (mbo != null)
            {
                result.Add("VolumeDriveType", GetMapValue(MapDriveType, FetchValue(mbo, "DriveType")));
                result.Add("VolumeFileSystem", FetchValue(mbo, "FileSystem"));
                result.Add("VolumeFileSystemLabel", FetchValue(mbo, "FileSystemLabel"));
                result.Add("VolumeSize", FetchValue(mbo, "Size"));
                result.Add("VolumeSizeRemaining", FetchValue(mbo, "SizeRemaining"));
            }
        }

        private static void DiskTypeInfo(ManagementScope scope, char driveLetter, ref Dictionary<string, string> result)
        {
            string queryPartitionString = $"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter=\"{driveLetter}\"";
            ManagementBaseObject mbo = GetFirstRecord(scope, queryPartitionString);
            if (mbo != null)
            {
                string diskNumber = FetchValue(mbo, "DiskNumber");
                result.Add("DiskNumber", diskNumber);

                if (diskNumber.Length > 0)
                {
                    string queryDiskString = $"SELECT Model,IsBoot,IsSystem,SerialNumber FROM MSFT_Disk WHERE Number=\"{diskNumber}\"";
                    mbo = GetFirstRecord(scope, queryDiskString);
                    if (mbo != null)
                    {
                        result.Add("DiskModel", FetchValue(mbo, "Model"));
                        result.Add("DiskIsSystem", FetchValue(mbo, "IsSystem"));
                        result.Add("DiskIsBoot", FetchValue(mbo, "IsBoot"));
                        result.Add("DiskSerialNumber", FetchValue(mbo, "SerialNumber"));
                    }

                    string queryPhysicalDiskString = $"SELECT MediaType,BusType,SpindleSpeed FROM MSFT_PhysicalDisk WHERE DeviceId=\"{diskNumber}\"";
                    mbo = GetFirstRecord(scope, queryPhysicalDiskString);
                    if (mbo != null)
                    {
                        result.Add("PhysicalMediaType", GetMapValue(MapMediaType, FetchValue(mbo, "MediaType")));
                        result.Add("PhysicalBusType", GetMapValue(MapBusType, FetchValue(mbo, "BusType")));
                        result.Add("PhysicalSpindleSpeed", FetchValue(mbo, "SpindleSpeed"));
                    }
                }
            }
        }

        private static string FetchValue(ManagementBaseObject mbo, string key)
        {
            return (mbo[key] != null) ? mbo[key].ToString().Trim() : string.Empty;
        }

        private static string GetMapValue(Dictionary<int, string> map, string rawValue)
        {
            return int.TryParse(rawValue, out int key) && map.Keys.Contains(key) ? map[key] : rawValue;
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

            // A bogus path or a UNC path.  This should not happen since the path should already
            // have been validated.
            throw new ArgumentException($"Could not map path '{path}' to a drive letter.");
        }

        private static ManagementBaseObject GetFirstRecord(ManagementScope scope, string queryString)
        {
            ObjectQuery q = new ObjectQuery(queryString);
            ManagementObjectSearcher s = new ManagementObjectSearcher(scope, q);

            // Only return the first result.  (There should only be one row returned for each of these queries.)
            return s.Get().Cast<ManagementBaseObject>().FirstOrDefault();
        }
    }
}
