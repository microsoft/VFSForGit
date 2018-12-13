namespace GVFS.Common
{
    public class ManifestEntry
    {
        public ManifestEntry(string name, string version, string args, string installerPath)
        {
            this.Name = name;
            this.Version = version;
            this.Args = args;
            this.RelativePath = installerPath;
        }

        public string Args { get; set; }
        public string Name { get; set; }
        public string RelativePath { get; set; }
        public string Version { get; set; }
    }
}
