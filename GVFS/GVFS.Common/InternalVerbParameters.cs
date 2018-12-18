using Newtonsoft.Json;

namespace GVFS.Common
{
    public class InternalVerbParameters
    {
        public InternalVerbParameters(string serviceName, bool startedByService, string maintenanceJob)
        {
            this.ServiceName = serviceName;
            this.StartedByService = startedByService;
            this.MaintenanceJob = maintenanceJob;
        }

        public string ServiceName { get; private set; }
        public bool StartedByService { get; private set; }
        public string MaintenanceJob { get; private set; }

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
