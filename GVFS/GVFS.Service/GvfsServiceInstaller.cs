using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace GVFS.Service
{
    [RunInstaller(true)]
    public class GVFSServiceInstaller : Installer
    {
        public GVFSServiceInstaller()
        {
            ServiceProcessInstaller procInstaller = new ServiceProcessInstaller();
            ServiceInstaller installer = new ServiceInstaller();

            procInstaller.Account = ServiceAccount.LocalSystem;
            procInstaller.Username = null;
            procInstaller.Password = null;

            installer.DisplayName = "GVFS.Service";
            installer.StartType = ServiceStartMode.Automatic;
            installer.ServiceName = "GVFS.Service";

            this.Installers.Add(procInstaller);
            this.Installers.Add(installer);
        }
    }
}
