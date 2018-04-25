using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.IO;

namespace GVFS.PreBuild
{
    public class GenerateApplicationManifests : Task
    {
        [Required]
        public string Version { get; set; }

        [Required]
        public string BuildOutputPath { get; set; }

        public override bool Execute()
        {
            this.Log.LogMessage(MessageImportance.High, "Creating application manifest files");

            if (!Directory.Exists(this.BuildOutputPath))
            {
                Directory.CreateDirectory(this.BuildOutputPath);
            }

            string[] applicationNames =
            {
                "GVFS.FunctionalTests",
                "GVFS.Service",
            };

            foreach (string applicationName in applicationNames)
            {
                File.WriteAllText(
                    Path.Combine(this.BuildOutputPath, applicationName + ".exe.manifest"),
                    string.Format(
@"<?xml version=""1.0"" encoding=""utf-8""?>
<assembly manifestVersion=""1.0"" xmlns=""urn:schemas-microsoft-com:asm.v1"">
  <assemblyIdentity version=""{0}"" name=""Microsoft.GVFS.{1}""/>
  <compatibility xmlns=""urn:schemas-microsoft-com:compatibility.v1"">
    <application>
      <!-- Windows 10 -->
      <supportedOS Id=""{{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}}"" />
    </application>
  </compatibility>
</assembly>
",
                        this.Version,
                        applicationName));
            }

            return true;
        }
    }
}

