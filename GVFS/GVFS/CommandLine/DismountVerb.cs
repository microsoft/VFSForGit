using CommandLine;

namespace GVFS.CommandLine
{
    [Verb(DismountVerb.DismountVerbName, HelpText = "Dismount a GVFS virtual repo")]
    public class DismountVerb: UnmountVerb
    {
        private const string DismountVerbName = "dismount";
    }
}
