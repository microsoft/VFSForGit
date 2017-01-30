using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GVFS.Common
{
    public class WindowsProcessJob : IDisposable
    {
        private SafeJobHandle jobHandle;
        private bool disposed;

        public WindowsProcessJob(Process process)
        {
            IntPtr newHandle = Native.CreateJobObject(IntPtr.Zero, null);
            if (newHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to create a job.  Error: " + Marshal.GetLastWin32Error());
            }

            this.jobHandle = new SafeJobHandle(newHandle);

            Native.JOBOBJECT_BASIC_LIMIT_INFORMATION info = new Native.JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = 0x2000 // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            };

            Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION extendedInfo = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = info
            };

            int length = Marshal.SizeOf(typeof(Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            if (!Native.SetInformationJobObject(this.jobHandle, Native.JobObjectInfoType.ExtendedLimitInformation, ref extendedInfo, (uint)length))
            {
                throw new InvalidOperationException("Unable to configure the job.  Error: " + Marshal.GetLastWin32Error());
            }

            if (!Native.AssignProcessToJobObject(this.jobHandle, process.Handle))
            {
                throw new InvalidOperationException("Unable to add process to the job.  Error: " + Marshal.GetLastWin32Error());
            }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                this.jobHandle.Dispose();
                this.jobHandle = null;

                this.disposed = true;
            }
        }

        private static class Native
        {
            public enum JobObjectInfoType
            {
                AssociateCompletionPortInformation = 7,
                BasicLimitInformation = 2,
                BasicUIRestrictions = 4,
                EndOfJobTimeInformation = 6,
                ExtendedLimitInformation = 9,
                SecurityLimitInformation = 5,
                GroupInformation = 11
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr CreateJobObject(IntPtr attributes, string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetInformationJobObject(SafeJobHandle jobHandle, JobObjectInfoType infoType, [In] ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION jobObjectInfo, uint jobObjectInfoLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool AssignProcessToJobObject(SafeJobHandle jobHandle, IntPtr processHandle);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool CloseHandle(IntPtr handle);

            [StructLayout(LayoutKind.Sequential)]
            public struct IO_COUNTERS
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize;
                public UIntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct SECURITY_ATTRIBUTES
            {
                public uint Length;
                public IntPtr SecurityDescriptor;
                public int InheritHandle;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }
        }

        private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeJobHandle(IntPtr handle) : base(true)
            {
                this.SetHandle(handle);
            }

            protected override bool ReleaseHandle()
            {
                return Native.CloseHandle(this.handle);
            }
        }
    }
}