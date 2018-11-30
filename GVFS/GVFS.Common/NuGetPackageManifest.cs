using System.Collections.Generic;
using System.IO;

namespace GVFS.Common
{
    public class NuGetPackageManifest
    {
        public NuGetPackageManifest()
        {
            this.Properties = new Dictionary<string, string>();
        }

        public Dictionary<string, string> Properties { get; set; }

        public void Read(string path)
        {
            string[] lines = File.ReadAllLines(path);
            foreach (string line in lines)
            {
                string[] tokens = line.Split(':');
                this.Properties.Add(tokens[0].Trim(), tokens[1].Trim());
            }
        }
    }
}
