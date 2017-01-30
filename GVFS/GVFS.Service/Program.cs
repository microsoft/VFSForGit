using System;
using GVFS.Common.Tracing;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;

namespace GVFS.Service
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            using (JsonEtwTracer tracer = new JsonEtwTracer("GVFS.Service", "GVFS.Service"))
            using (GVFSService service = new GVFSService(tracer))
            {
                if (args.Any("--console".Equals))
                {
                    tracer.AddConsoleEventListener(EventLevel.Verbose, Keywords.Any);
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Message", "Starting service as console app, Ctrl+C to stop.");
                    tracer.RelatedEvent(EventLevel.Informational, "ServiceStart", metadata);

                    Console.CancelKeyPress += (sender, evtArgs) => service.Stop();
                    service.Run();
                }
                else
                {
                    ServiceBase.Run(new[] { service });
                }
            }
        }
    }
}