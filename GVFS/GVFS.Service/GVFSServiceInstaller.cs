using GVFS.Common;
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

            installer.DisplayName = GVFSConstants.Service.ServiceName;
            installer.StartType = ServiceStartMode.Automatic;
            installer.ServiceName = GVFSConstants.Service.ServiceName;
            installer.Description = "GVFS AutoMount and health monitoring";

            this.Installers.Add(procInstaller);
            this.Installers.Add(installer);
        }
    }
}
