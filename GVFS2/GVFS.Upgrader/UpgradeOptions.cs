using CommandLine;

namespace GVFS.Upgrader
{
    [Verb("UpgradeOrchestrator", HelpText = "Upgrade VFS for Git.")]
    public class UpgradeOptions
    {
        [Option(
            "dry-run",
            Default = false,
            Required = false,
            HelpText = "Display progress and errors, but don't install GVFS")]
        public bool DryRun { get; set; }

        [Option(
            "no-verify",
            Default = false,
            Required = false,
            HelpText = "Don't verify authenticode signature of installers")]
        public bool NoVerify { get; set; }
    }
}
