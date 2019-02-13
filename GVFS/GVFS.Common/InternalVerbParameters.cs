using Newtonsoft.Json;

namespace GVFS.Common
{
    public class InternalVerbParameters
    {
        public InternalVerbParameters(
            string serviceName = null,
            bool startedByService = true,
            string maintenanceJob = null,
            string packfileMaintenanceBatchSize = null)
        {
            this.ServiceName = serviceName;
            this.StartedByService = startedByService;
            this.MaintenanceJob = maintenanceJob;
            this.PackfileMaintenanceBatchSize = packfileMaintenanceBatchSize;
        }

        public string ServiceName { get; private set; }
        public bool StartedByService { get; private set; }
        public string MaintenanceJob { get; private set; }
        public string PackfileMaintenanceBatchSize { get; private set; }

        public static InternalVerbParameters FromJson(string json)
        {
            return JsonConvert.DeserializeObject<InternalVerbParameters>(json);
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
