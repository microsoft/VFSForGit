using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Common
{
    /// <summary>
    /// Details on the upgrade included in this package, including information
    /// on what packages are included and how to install them.
    /// </summary>
    public class ReleaseManifest
    {
        public static string WindowsPlatformKey = "Windows";

        public ReleaseManifest()
        {
            this.PlatformInstallManifests = new Dictionary<string, InstallManifestPlatform>();
        }

        /// <summary>
        /// Install manifests for different platforms.
        /// </summary>
        public Dictionary<string, InstallManifestPlatform> PlatformInstallManifests { get; private set; }

        /// <summary>
        /// Construct ReleaseManifest from a JSON file.
        /// </summary>
        public static ReleaseManifest FromJsonFile(string path)
        {
            using (StreamReader streamReader = File.OpenText(path))
            {
                return ReleaseManifest.FromJson(streamReader);
            }
        }

        /// <summary>
        /// Construct ReleaseManifest from a JSON string.
        /// </summary>
        public static ReleaseManifest FromJsonString(string json)
        {
            return JsonConvert.DeserializeObject<ReleaseManifest>(json);
        }

        /// <summary>
        /// Construct ReleaseManifest from a JSON stream
        /// </summary>
        public static ReleaseManifest FromJson(StreamReader stream)
        {
            JsonSerializer serializer = new JsonSerializer();
            return (ReleaseManifest)serializer.Deserialize(stream, typeof(ReleaseManifest));
        }

        public void AddPlatformInstallManifest(string platform, IEnumerable<ManifestEntry> entries)
        {
            InstallManifestPlatform platformManifest = new InstallManifestPlatform(entries);
            this.PlatformInstallManifests.Add(platform, platformManifest);
        }
    }
}
