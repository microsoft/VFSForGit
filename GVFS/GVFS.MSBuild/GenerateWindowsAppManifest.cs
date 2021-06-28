using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.IO;

namespace GVFS.MSBuild
{
    public class GenerateWindowsAppManifest : Task
    {
        [Required]
        public string Version { get; set; }

        [Required]
        public string ApplicationName { get; set; }

        [Required]
        public string OutputFile { get; set; }

        public override bool Execute()
        {
            this.Log.LogMessage(MessageImportance.Normal, "Creating application manifest file for '{0}'...", this.ApplicationName);

            string manifestDirectory = Path.GetDirectoryName(this.OutputFile);
            if (!Directory.Exists(manifestDirectory))
            {
                Directory.CreateDirectory(manifestDirectory);
            }

            // Any application that calls GetVersionEx must have an application manifest in order to get an accurate response.
            // See https://msdn.microsoft.com/en-us/library/windows/desktop/ms724451(v=vs.85).aspx for details
            File.WriteAllText(
                this.OutputFile,
                string.Format(
                    @"<?xml version=""1.0"" encoding=""utf-8""?>
<assembly manifestVersion=""1.0"" xmlns=""urn:schemas-microsoft-com:asm.v1"">
  <assemblyIdentity version=""{0}"" name=""{1}""/>
  <compatibility xmlns=""urn:schemas-microsoft-com:compatibility.v1"">
    <application>
      <!-- Windows 10 -->
      <supportedOS Id=""{{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}}"" />
    </application>
  </compatibility>
</assembly>
",
                    this.Version,
                    this.ApplicationName));

            return true;
        }
    }
}