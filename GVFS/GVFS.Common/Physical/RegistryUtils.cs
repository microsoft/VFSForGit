using Microsoft.Win32;

namespace GVFS.Common.Physical
{
    public class RegistryUtils
    {
        public static string GetStringFromRegistry(RegistryHive registryHive, string key, string valueName)
        {
            string value = GetStringFromRegistry(registryHive, key, valueName, RegistryView.Registry64);
            if (value == null)
            {
                value = GetStringFromRegistry(registryHive, key, valueName, RegistryView.Registry32);
            }

            return value;
        }

        private static string GetStringFromRegistry(RegistryHive registryHive, string key, string valueName, RegistryView view)
        {
            RegistryKey localKey = RegistryKey.OpenBaseKey(registryHive, view);
            var localKeySub = localKey.OpenSubKey(key);

            object value = localKeySub == null ? null : localKeySub.GetValue(valueName);

            if (value == null)
            {
                return null;
            }

            return (string)value;
        }
    }
}
