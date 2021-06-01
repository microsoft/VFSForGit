namespace GVFS.Common.NuGetUpgrade
{
    public class InstallActionInfo
    {
        /// <summary>
        /// Well known tokens that will be replaced when encountered in an Arguments field.
        /// </summary>
        public const string ManifestEntryInstallationIdToken = "installation_id";
        public const string ManifestEntryLogDirectoryToken = "log_directory";
        public const string ManifestEntryInstallerBaseDirectoryToken = "installer_base_path";

        public InstallActionInfo(string name, string version, string args, string installerRelativePath, string command)
        {
            this.Name = name;
            this.Version = version;
            this.Args = args;
            this.InstallerRelativePath = installerRelativePath;
            this.Command = command;
        }

        /// <summary>
        /// The arguments that should be passed to the install command
        /// </summary>
        public string Args { get; }

        /// <summary>
        /// User friendly name for the install action
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The path to the application or document to start. The path
        /// is relative to the content directory of the NuGet package.
        /// </summary>
        public string InstallerRelativePath { get; }

        /// <summary>
        /// The version of the component that this entry installs
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// The command to run. If this is present, the command is run
        /// directly (with the processed args), and the
        /// InstallerRelativePath property is ignored.
        /// </summary>
        public string Command { get; }
    }
}
