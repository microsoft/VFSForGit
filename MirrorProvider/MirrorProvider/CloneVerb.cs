using System;
using System.IO;
using CommandLine;

namespace MirrorProvider
{
    [Verb("clone")]
    public class CloneVerb
    {
        [Value(
            0,
            Required = true,
            MetaName = "Path to mirror",
            HelpText = "The local path to mirror from")]
        public string PathToMirror { get; set; }

        [Value(
            1,
            Required = true,
            MetaName = "Enlistment root",
            HelpText = "The path to create the mirrored enlistment in")]
        public string EnlistmentRoot { get; set;  }

        public void Execute(FileSystemVirtualizer fileSystemVirtualizer)
        {   
            Console.WriteLine($"Cloning from {Path.GetFullPath(this.PathToMirror)} to {Path.GetFullPath(this.EnlistmentRoot)}");

            if (Directory.Exists(this.EnlistmentRoot))
            {
                Console.WriteLine($"Error: Directory {this.EnlistmentRoot} already exists");
                return;
            }

            Enlistment enlistment = Enlistment.CreateNewEnlistment(this.EnlistmentRoot, this.PathToMirror);
            if (enlistment == null)
            {
                Console.WriteLine("Error: Unable to create enlistment");
                return;
            }

            if (fileSystemVirtualizer.TryConvertVirtualizationRoot(enlistment.SrcRoot, out string error))
            {
                Console.WriteLine($"Virtualization root created successfully at {enlistment.SrcRoot}");
            }
            else
            {
                Console.WriteLine("Error: Failed to create virtualization root: " + error);
            }
        }
    }
}
