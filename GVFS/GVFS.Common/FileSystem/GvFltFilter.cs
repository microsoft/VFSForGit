using GVFS.Common.Tracing;
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Security;
using System.ServiceProcess;
using System.Text;

namespace GVFS.Common.FileSystem
{
    public class GvFltFilter
    {
        public const RegistryHive GvFltParametersHive = RegistryHive.LocalMachine;
        public const string GvFltParametersKey = "SYSTEM\\CurrentControlSet\\Services\\Gvflt\\Parameters";
        public const string GvFltTimeoutValue = "CommandTimeoutInMs";
        private const string EtwArea = nameof(GvFltFilter);

        private const int MinGvFltTimeoutMs = 86400000;

        private const string GvFltName = "gvflt";

        private const uint OkResult = 0;
        private const uint NameCollisionErrorResult = 0x801F0012;

        public static bool TryAttach(ITracer tracer, string root, out string errorMessage)
        {
            errorMessage = null;
            try
            {
                StringBuilder volumePathName = new StringBuilder(GVFSConstants.MaxPath);
                if (!NativeMethods.GetVolumePathName(root, volumePathName, GVFSConstants.MaxPath))
                {
                    errorMessage = "Could not get volume path name";
                    tracer.RelatedError(errorMessage);
                    return false;
                }

                uint result = NativeMethods.FilterAttach(GvFltName, volumePathName.ToString(), null);
                if (result != OkResult && result != NameCollisionErrorResult)
                {
                    errorMessage = string.Format("Attaching the filter driver resulted in: {0}", result);
                    tracer.RelatedError(errorMessage);
                    return false;
                }
            }
            catch (Exception e)
            {
                errorMessage = string.Format("Attaching the filter driver resulted in: {0}", e.Message);
                tracer.RelatedError(errorMessage);
                return false;
            }

            return true;
        }

        public static bool IsHealthy(out string error, out string warning, ITracer tracer)
        {
            // TODO 1026787: Record errors\warnings in the event log

            warning = string.Empty;

            if (!IsServiceRunning(out error, tracer))
            {
                return false;
            }

            CheckTimeoutConfiguration(out warning, tracer);
            return true;
        }

        public static bool TryGetTimeout(out int timeoutMs, out string error, ITracer tracer = null)
        {
            timeoutMs = 0;
            error = string.Empty;
            object value;
            try
            {
                value = ProcessHelper.GetValueFromRegistry(GvFltParametersHive, GvFltParametersKey, GvFltTimeoutValue);
            }
            catch (UnauthorizedAccessException e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("ErrorMessage", "UnauthorizedAccessException while trying to read GvFlt timeout");
                    tracer.RelatedError(metadata);
                }

                error = "Failed to read GvFlt timeout from registry. " + e.Message;
                return false;
            }
            catch (SecurityException e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("ErrorMessage", "SecurityException while trying to read GvFlt timeout");
                    tracer.RelatedError(metadata);
                }

                error = "Failed to read GvFlt timeout from registry. " + e.Message;
                return false;
            }
            catch (Exception e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("ErrorMessage", "Exception while trying to read GvFlt timeout");
                    tracer.RelatedError(metadata);
                }

                error = "Failed to read GvFlt timeout from registry. " + e.Message;
                return false;
            }

            try
            {
                timeoutMs = Convert.ToInt32(value);
            }
            catch (Exception e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("ErrorMessage", "Exception while trying to convert GvFlt timeout to int");
                    tracer.RelatedError(metadata);
                }

                error = "GvFlt timeout not properly configured, failed to convert value to int: " + e.Message;
                return false;
            }

            return true;
        }

        private static bool IsServiceRunning(out string error, ITracer tracer)
        {
            error = string.Empty;

            bool gvfltServiceRunning = false;
            try
            {
                ServiceController controller = new ServiceController("gvflt");
                gvfltServiceRunning = controller.Status.Equals(ServiceControllerStatus.Running);
            }
            catch (InvalidOperationException e)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    metadata.Add("Exception", e.ToString());
                    metadata.Add("ErrorMessage", "InvalidOperationException: GvFlt Service was not found");
                    tracer.RelatedError(metadata);
                }

                error = "Error: GvFlt Service was not found. To resolve, re-install GVFS";
                return false;
            }

            if (!gvfltServiceRunning)
            {
                if (tracer != null)
                {
                    EventMetadata metadata = new EventMetadata();
                    metadata.Add("Area", EtwArea);
                    metadata.Add("ErrorMessage", "GvFlt Service is not running");
                    tracer.RelatedError(metadata);
                }

                error = "Error: GvFlt Service is not running. To resolve, run \"sc start gvflt\" from an elevated command prompt";
                return false;
            }

            return true;
        }

        private static void CheckTimeoutConfiguration(out string warning, ITracer tracer)
        {
            warning = string.Empty;
            string error;
            int timemoutMs;
            if (!TryGetTimeout(out timemoutMs, out error, tracer))
            {
                warning = "Warning: Failed to validate GvFlt timeout configuration: " + error;
            }
            else if (timemoutMs < MinGvFltTimeoutMs)
            {
                warning = string.Format("Warning: GvFlt timeout not properly configured, timeout {0} less than recommended value {1}", timemoutMs, MinGvFltTimeoutMs);
            }
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
