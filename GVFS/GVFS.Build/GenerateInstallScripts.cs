using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.IO;

namespace GVFS.PreBuild
{
    public class GenerateInstallScripts : Task
    {
        [Required]
        public string G4WInstallerPath { get; set; }

        [Required]
        public string GVFSSetupPath { get; set; }

        [Required]
        public string G4WInstallBatPath { get; set; }

        [Required]
        public string GVFSInstallBatPath { get; set; }

        public override bool Execute()
        {
            this.Log.LogMessage(MessageImportance.High, "Creating install script for " + this.G4WInstallerPath);

            File.WriteAllText(
                this.G4WInstallBatPath,
                this.G4WInstallerPath + @" /DIR=""C:\Program Files\Git"" /NOICONS /COMPONENTS=""ext,ext\shellhere,ext\guihere,assoc,assoc_sh"" /GROUP=""Git"" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART");

            File.WriteAllText(
                this.GVFSInstallBatPath,
                this.GVFSSetupPath + " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART");

            return true;
        }
    }
}
