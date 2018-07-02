using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.IO;

namespace GVFS.PreBuild
{
    public class GenerateGVFSInstallersNuspec : Task
    {
        [Required]
        public string GVFSSetupPath { get; set; }

        [Required]
        public string GitPackageVersion { get; set; }

        [Required]
        public string PackagesPath { get; set; }

        [Required]
        public string OutputFile { get; set; }

        public override bool Execute()
        {
            this.Log.LogMessage(MessageImportance.High, "Generating GVFS.Installers.nuspec");

            this.GVFSSetupPath = Path.GetFullPath(this.GVFSSetupPath);
            this.PackagesPath = Path.GetFullPath(this.PackagesPath);

            File.WriteAllText(
                this.OutputFile,
                string.Format(
@"<?xml version=""1.0""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
  <metadata>
    <id>GVFS.Installers</id>
    <version>$GVFSVersion$</version>
    <authors>Microsoft</authors>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <description>GVFS and G4W installers</description>
  </metadata>
  <files>
    <file src=""{0}"" target=""GVFS"" />
    <file src=""{1}\GitForWindows.GVFS.Installer.{2}\tools\*"" target=""G4W"" />
    <file src=""{1}\GitForWindows.GVFS.Portable.{2}\tools\*"" target=""G4W"" />
  </files>
</package>",
                    this.GVFSSetupPath,
                    this.PackagesPath,
                    this.GitPackageVersion));

            return true;
        }
    }
}
