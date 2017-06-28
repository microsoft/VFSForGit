using GVFS.Common;
using GVFS.Mount;
using System.IO;

namespace GVFS.Service
{
    public class Configuration
    {
        private static Configuration instance = new Configuration();
        private static string assemblyPath = null;
        
        private Configuration()
        {
            this.GVFSMountLocation = Path.Combine(AssemblyPath, InProcessMountVerb.MountExeName);
            this.GVFSServiceUILocation = Path.Combine(AssemblyPath, GVFSConstants.Service.UIName + GVFSConstants.ExecutableExtension);
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
        
        public string GVFSMountLocation { get; private set; }
        public string GVFSServiceUILocation { get; private set; }
    }
}
