using GVFS.Common;
using GVFS.Common.NamedPipes;
using System;
using System.IO;

namespace GVFS.CommandLine
{
    public class UpgradeVerb : GVFSVerb.ForNoEnlistment
    {
        private const string UpgradeVerbName = "upgrade";

        public UpgradeVerb()
        {
            this.Output = Console.Out;
        }

        public string InstallerPath { get; set; }

        public bool AllowUnsigned { get; set; }

        public static System.CommandLine.Command CreateCommand()
        {
            System.CommandLine.Command cmd = new System.CommandLine.Command("upgrade", "Upgrade VFS for Git by running an installer through the GVFS service (no UAC required).");

            System.CommandLine.Argument<string> installerPathArg = new System.CommandLine.Argument<string>("installer-path")
            {
                Description = "Path to the SetupGVFS.*.exe installer",
            };
            cmd.Add(installerPathArg);

#if DEBUG
            System.CommandLine.Option<bool> allowUnsignedOption = new System.CommandLine.Option<bool>(
                "--allow-unsigned") { Description = "Skip Authenticode signature verification (debug builds only, requires admin)" };
            cmd.Add(allowUnsignedOption);
#endif

            System.CommandLine.Option<string> internalOption = GVFSVerb.CreateInternalParametersOption();
            cmd.Add(internalOption);

            GVFSVerb.SetActionForNoEnlistment<UpgradeVerb>(cmd, internalOption,
                (verb, result) =>
                {
                    verb.InstallerPath = result.GetValue(installerPathArg);
#if DEBUG
                    verb.AllowUnsigned = result.GetValue(allowUnsignedOption);
#endif
                });

            return cmd;
        }

        protected override string VerbName
        {
            get { return UpgradeVerbName; }
        }

        public override void Execute()
        {
            if (string.IsNullOrWhiteSpace(this.InstallerPath))
            {
                this.ReportErrorAndExit("Installer path is required. Usage: gvfs upgrade <installer-path>");
                return;
            }

            string fullPath = Path.GetFullPath(this.InstallerPath);
            if (!File.Exists(fullPath))
            {
                this.ReportErrorAndExit($"Installer not found: {fullPath}");
                return;
            }

            this.Output.WriteLine($"Requesting upgrade via GVFS service...");
            this.Output.WriteLine($"Installer: {fullPath}");

            if (this.AllowUnsigned)
            {
                this.Output.WriteLine("WARNING: Authenticode signature verification is disabled (--allow-unsigned)");
            }

            NamedPipeMessages.RunInstallerRequest request = new NamedPipeMessages.RunInstallerRequest
            {
                InstallerPath = fullPath,
                AllowUnsigned = this.AllowUnsigned,
            };

            using (NamedPipeClient client = new NamedPipeClient(this.ServicePipeName))
            {
                if (!client.Connect())
                {
                    this.ReportErrorAndExit(
                        "Unable to connect to GVFS service. Is GVFS.Service running?");
                    return;
                }

                try
                {
                    client.SendRequest(request.ToMessage());
                    NamedPipeMessages.Message rawResponse = client.ReadResponse();

                    // Old GVFS.Service (pre-2.1) doesn't know about
                    // RunInstallerRequest and returns the literal
                    // "UnknownRequest" sentinel with no body. Detect that
                    // case explicitly so the user gets a clear "service is
                    // too old" message instead of a JSON deserialization
                    // stack trace.
                    if (string.Equals(rawResponse.Header, NamedPipeMessages.UnknownRequest, StringComparison.Ordinal))
                    {
                        this.ReportErrorAndExit(
                            "The installed GVFS service does not support 'gvfs upgrade'. " +
                            "This feature requires GVFS.Service 2.1 or later — please install a " +
                            "newer GVFS using the standard installer first, then retry.");
                        return;
                    }

                    NamedPipeMessages.RunInstallerRequest.Response response =
                        NamedPipeMessages.RunInstallerRequest.Response.FromMessage(rawResponse);

                    if (response.State == NamedPipeMessages.CompletionState.Success)
                    {
                        this.Output.WriteLine("Upgrade started. The installer is running in the background.");
                        this.Output.WriteLine("GVFS service will restart automatically. Check 'gvfs version' after a few seconds.");
                    }
                    else
                    {
                        this.ReportErrorAndExit(
                            $"Upgrade failed: {response.ErrorMessage}");
                    }
                }
                catch (BrokenPipeException ex)
                {
                    this.ReportErrorAndExit(
                        $"Lost connection to GVFS service during upgrade: {ex.Message}");
                }
            }
        }
    }
}
