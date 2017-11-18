using RGFS.Common;
using System.IO;

namespace RGFS.Service
{
    public class Configuration
    {
        private static Configuration instance = new Configuration();
        private static string assemblyPath = null;
        
        private Configuration()
        {
            this.RGFSLocation = Path.Combine(AssemblyPath, RGFSConstants.RGFSExecutableName);
            this.RGFSServiceUILocation = Path.Combine(AssemblyPath, RGFSConstants.Service.UIName + RGFSConstants.ExecutableExtension);
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

        public string RGFSLocation { get; private set; }
        public string RGFSServiceUILocation { get; private set; }
    }
}
