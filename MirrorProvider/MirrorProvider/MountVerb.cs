using System;
using System.IO;
using CommandLine;

namespace MirrorProvider
{
    [Verb("mount")]
    public class MountVerb
    {
        private Enlistment enlistment;

        [Value(
            0,
            Required = true,
            MetaName = "Enlistment root",
            HelpText = "The path to create the mirrored enlistment in")]
        public string EnlistmentRoot { get; set; }

        public void Execute(FileSystemVirtualizer fileSystemVirtualizer)
        {
            this.enlistment = Enlistment.LoadExistingEnlistment(this.EnlistmentRoot);
            if (this.enlistment == null)
            {
                Console.WriteLine("Error: Unable to load enlistment");
		return;
            }

            Console.WriteLine();
            Console.WriteLine($"Mounting {Path.GetFullPath(this.enlistment.EnlistmentRoot)}");

            if (fileSystemVirtualizer.TryStartVirtualizationInstance(this.enlistment, out string error))
            {
                Console.WriteLine("Virtualization instance started successfully");

                Console.WriteLine("Press Enter to end the instance");
                Console.ReadLine();
            }
            else
            {
                Console.WriteLine("Virtualization instance failed to start: " + error);
                return;
            }

            fileSystemVirtualizer.Stop();
        }
    }
}
