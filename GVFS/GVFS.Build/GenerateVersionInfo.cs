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

        public override bool Execute()
        {
            this.Log.LogMessage(MessageImportance.High, "Creating version files");

            File.WriteAllText(
                Path.Combine(this.BuildOutputPath, "CommonAssemblyVersion.cs"),
                string.Format(
@"using System.Reflection; 

[assembly: AssemblyVersion(""{0}"")]
[assembly: AssemblyFileVersion(""{0}"")]
",
                this.Version));

            string commaDelimetedVersion = this.Version.Replace('.', ',');
            File.WriteAllText(
                Path.Combine(this.BuildOutputPath, "CommonVersionHeader.h"),
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
    }
}
