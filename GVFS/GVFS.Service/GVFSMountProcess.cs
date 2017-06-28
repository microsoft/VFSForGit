using GVFS.Common;
using GVFS.Common.NamedPipes;
using GVFS.Common.Tracing;
using Microsoft.Diagnostics.Tracing;
using Newtonsoft.Json;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GVFS.Service
{
    public class GVFSMountProcess
    {
        private const int BackgroundProcessConnectTimeoutMS = 10000;
        private const string ParamPrefix = "--";

        private const string GvfltName = "gvflt";

        private const uint OkResult = 0;
        private const uint NameCollisionErrorResult = 0x801F0012;

        private readonly string pathToGVFSMount;
        private readonly string enlistmentRoot;
        private readonly ITracer tracer;
        
        public GVFSMountProcess(ITracer tracer, string enlistmentRoot)
        {
            this.tracer = tracer;
            this.pathToGVFSMount = Configuration.Instance.GVFSMountLocation;
            this.enlistmentRoot = enlistmentRoot;
        }
        
        public bool Mount(EventLevel verbosity, Keywords keywords, bool showDebugWindow)
        {
            this.CheckAntiVirusExclusion(this.tracer, this.enlistmentRoot);

            if (!this.TryAttachGvflt(this.tracer, this.enlistmentRoot))
            {
                return false;
            }

            if (!this.CallGVFSMount(verbosity, keywords, showDebugWindow))
            {
                this.tracer.RelatedError("Unable to start the GVFS.Mount process.");
                return false;
            }

            return this.WaitForGVFSStatus();
        }

        private bool CallGVFSMount(EventLevel verbosity, Keywords keywords, bool showDebugWindow)
        {
            string arguments = string.Join(
                " ",
                this.enlistmentRoot,
                ParamPrefix + MountParameters.Verbosity,
                verbosity,
                ParamPrefix + MountParameters.Keywords,
                keywords,
                showDebugWindow ? ParamPrefix + MountParameters.DebugWindow : string.Empty);

            return ProcessAsCurrentUser.Run(this.tracer, Configuration.Instance.GVFSMountLocation, arguments, showDebugWindow);
        }

        private bool WaitForGVFSStatus()
        {
            using (NamedPipeClient pipeClient = new NamedPipeClient(NamedPipeClient.GetPipeNameFromPath(this.enlistmentRoot)))
            {
                if (!pipeClient.Connect(BackgroundProcessConnectTimeoutMS))
                {
                    this.tracer.RelatedError("Unable to mount because the GVFS.Mount process is not responding.");
                    return false;
                }

                while (true)
                {
                    string response = string.Empty;
                    try
                    {
                        pipeClient.SendRequest(NamedPipeMessages.GetStatus.Request);
                        response = pipeClient.ReadRawResponse();
                        NamedPipeMessages.GetStatus.Response getStatusResponse =
                            NamedPipeMessages.GetStatus.Response.FromJson(response);

                        // TODO: 872426 Improve responsiveness of GVFS.Service waiting for GVFS.Mount to return status while mounting.
                        if (getStatusResponse.MountStatus == NamedPipeMessages.GetStatus.Ready)
                        {
                            this.tracer.RelatedInfo("Successfully mounted at {0}", this.enlistmentRoot);
                            return true;
                        }
                        else if (getStatusResponse.MountStatus == NamedPipeMessages.GetStatus.MountFailed)
                        {
                            this.tracer.RelatedInfo("Failed to mount at {0}", this.enlistmentRoot);
                            return false;
                        }
                        else
                        {
                            Thread.Sleep(500);
                        }
                    }
                    catch (BrokenPipeException e)
                    {
                        this.tracer.RelatedInfo("Could not connect to GVFS.Mount: {0}", e.ToString());
                        return false;
                    }
                    catch (JsonReaderException e)
                    {
                        EventMetadata metadata = new EventMetadata();
                        metadata.Add("Area", "GVFSService");
                        metadata.Add("Exception", e.ToString());
                        metadata.Add("ErrorMessage", "Mount: failed to parse response from GVFS.Mount");
                        metadata.Add("Response", response);
                        this.tracer.RelatedError(metadata);
                        return false;
                    }
                }
            }
        }

        private void CheckAntiVirusExclusion(ITracer tracer, string path)
        {
            string errorMessage;
            bool isExcluded;
            if (AntiVirusExclusions.TryGetIsPathExcluded(path, out isExcluded, out errorMessage))
            {
                if (!isExcluded)
                {
                    if (!AntiVirusExclusions.AddAntiVirusExclusion(path, out errorMessage))
                    {
                        tracer.RelatedError("Could not add this repo to the antivirus exclusion list. Error: {0}", errorMessage);
                    }
                }
            }
            else
            {
                tracer.RelatedError("Unable to determine if this repo is excluded from antivirus. Error: {0}", errorMessage);
            }
        }

        private bool TryAttachGvflt(ITracer tracer, string root)
        {
            try
            {
                StringBuilder volumePathName = new StringBuilder(GVFSConstants.MaxPath);
                if (!NativeMethods.GetVolumePathName(root, volumePathName, GVFSConstants.MaxPath))
                {
                    tracer.RelatedError("Could not get volume path name");
                    return false;
                }

                uint result = NativeMethods.FilterAttach(GvfltName, volumePathName.ToString(), null);
                if (result != OkResult && result != NameCollisionErrorResult)
                {
                    tracer.RelatedError("Attaching the filter driver resulted in: {0}", result);
                    return false;
                }
            }
            catch (Exception e)
            {
                tracer.RelatedError("Attaching the filter driver resulted in: {0}", e.Message);
                return false;
            }

            return true;
        }

        private static class NativeMethods
        {
            [DllImport("fltlib.dll", CharSet = CharSet.Unicode)]
            public static extern uint FilterAttach(
                string filterName,
                string volumeName,
                string instanceName,
                uint createdInstanceNameLength = 0,
                string createdInstanceName = null);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetVolumePathName(
                string volumeName,
                StringBuilder volumePathName,
                uint bufferLength);
        }
    }
}
