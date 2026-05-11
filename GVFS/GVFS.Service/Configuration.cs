using GVFS.Common;
using System.IO;

namespace GVFS.Service
{
    public class Configuration
    {
        private static Configuration instance = new Configuration();
        private static string assemblyPath = null;

        private Configuration()
        {
            this.GVFSLocation = Path.Combine(AssemblyPath, GVFSPlatform.Instance.Constants.GVFSExecutableName);
        }

        public static Configuration Instance
        {
            get
            {
                return instance;
            }
        }

        public static string AssemblyPath
        {
            get
            {
                if (assemblyPath == null)
                {
                    assemblyPath = ProcessHelper.GetCurrentProcessLocation();
                }

                return assemblyPath;
            }
        }

        public string GVFSLocation { get; private set; }
    }
}
