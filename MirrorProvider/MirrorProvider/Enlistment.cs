using System.IO;

namespace MirrorProvider
{
    public class Enlistment
    {
        private Enlistment(string root, string mirrorRoot)
        {
            this.EnlistmentRoot = root;
            this.DotMirrorRoot = Path.Combine(root, ".mirror");
            this.SrcRoot = Path.Combine(root, "src");

            this.ConfigFile = Path.Combine(this.DotMirrorRoot, "config");
            this.MirrorRoot = mirrorRoot;
        }

        public string EnlistmentRoot { get; private set; }
        public string DotMirrorRoot { get; private set; }
        public string SrcRoot { get; private set; }

        public string ConfigFile { get; private set; }

        public string MirrorRoot { get; private set; }

        public static Enlistment CreateNewEnlistment(string enlistmentRoot, string mirrorRoot)
        {
            if (!Directory.Exists(enlistmentRoot) && Directory.Exists(mirrorRoot))
            {
                Enlistment enlistment = new Enlistment(enlistmentRoot, mirrorRoot);

                Directory.CreateDirectory(enlistment.EnlistmentRoot);
                Directory.CreateDirectory(enlistment.DotMirrorRoot);
                Directory.CreateDirectory(enlistment.SrcRoot);

                File.WriteAllText(enlistment.ConfigFile, mirrorRoot);
                return enlistment;
            }

            return null;
        }

        public static Enlistment LoadExistingEnlistment(string enlistmentRoot)
        {
            if (Directory.Exists(enlistmentRoot))
            {
                Enlistment enlistment = new Enlistment(enlistmentRoot, null);
                enlistment.MirrorRoot = File.ReadAllText(enlistment.ConfigFile);

                if (Directory.Exists(enlistment.MirrorRoot))
                {
                    return enlistment;
                }
            }

            return null;
        }
    }
}
