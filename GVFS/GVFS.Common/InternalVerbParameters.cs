using Newtonsoft.Json;

namespace GVFS.Common
{
    public class InternalVerbParameters
    {
        public InternalVerbParameters(string serviceName, bool startedByService)
        {
            this.ServiceName = serviceName;
            this.StartedByService = startedByService;
        }

        public string ServiceName { get; private set; }
        public bool StartedByService { get; private set; }

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
