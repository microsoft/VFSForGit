using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace GVFS.Common.NuGetUpgrade
{
    /// <summary>
    /// Details on the upgrade included in this package, including information
    /// on what packages are included and how to install them.
    /// </summary>
    public class InstallManifest
    {
        public const string WindowsPlatformKey = "Windows";

        public InstallManifest()
        {
            this.PlatformInstallManifests = new Dictionary<string, InstallManifestPlatform>();
        }

        /// <summary>
        /// Install manifests for different platforms.
        /// </summary>
        public Dictionary<string, InstallManifestPlatform> PlatformInstallManifests { get; private set; }

        public static InstallManifest FromJsonFile(string path)
        {
            using (StreamReader streamReader = File.OpenText(path))
            {
                return InstallManifest.FromJson(streamReader);
            }
        }

        public static InstallManifest FromJsonString(string json)
        {
            return JsonConvert.DeserializeObject<InstallManifest>(json);
        }

        public static InstallManifest FromJson(StreamReader stream)
        {
            JsonSerializer serializer = new JsonSerializer();
            return (InstallManifest)serializer.Deserialize(stream, typeof(InstallManifest));
        }

        public void AddPlatformInstallManifest(string platform, IEnumerable<InstallActionInfo> entries)
        {
            InstallManifestPlatform platformManifest = new InstallManifestPlatform(entries);
            this.PlatformInstallManifests.Add(platform, platformManifest);
        }
    }
}
