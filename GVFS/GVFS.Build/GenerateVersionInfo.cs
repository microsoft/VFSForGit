using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.IO;

namespace GVFS.PreBuild
{
    public class GenerateVersionInfo : Task
    {
        [Required]
        public string Version { get; set; }

        [Required]
        public string BuildOutputPath { get; set; }

        [Required]
        public string AssemblyVersion { get; set; }

        [Required]
        public string VersionHeader { get; set; }

        public override bool Execute()
        {
            this.Log.LogMessage(MessageImportance.High, "Creating version files");

            EnsureParentDirectoryExistsFor(this.AssemblyVersion);
            File.WriteAllText(
                this.AssemblyVersion,
                string.Format(
@"using System.Reflection;

[assembly: AssemblyVersion(""{0}"")]
[assembly: AssemblyFileVersion(""{0}"")]
",
                this.Version));

            string commaDelimetedVersion = this.Version.Replace('.', ',');
            EnsureParentDirectoryExistsFor(this.VersionHeader);
            File.WriteAllText(
                this.VersionHeader,
                string.Format(
@"
#define GVFS_FILE_VERSION {0}
#define GVFS_FILE_VERSION_STRING ""{1}""
#define GVFS_PRODUCT_VERSION {0}
#define GVFS_PRODUCT_VERSION_STRING ""{1}""
",
                    commaDelimetedVersion,
                    this.Version));

            return true;
        }

        private void EnsureParentDirectoryExistsFor(string filename)
        {
            string directory = Path.GetDirectoryName(filename);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

        }
    }
}
