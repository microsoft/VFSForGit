using Newtonsoft.Json;
using System.IO;

namespace GVFS.Common
{
    /// <summary>
    ///   A ReleaseManifest that reads from a JSON representation.
    /// </summary>
    public class ReleaseManifestJson : ReleaseManifest
    {
        public override void Read(string path)
        {
            using (StreamReader file = File.OpenText(path))
            {
                JsonSerializer serializer = new JsonSerializer();
                InstallManifest manifest = (InstallManifest)serializer.Deserialize(file, typeof(InstallManifest));

                foreach (ManifestEntry entry in manifest.Platforms["Windows"].Installers)
                {
                    this.Entries.Add(entry);
                }
            }
        }
    }
}
