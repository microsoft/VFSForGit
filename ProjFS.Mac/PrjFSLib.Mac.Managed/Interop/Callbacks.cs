using System.Runtime.InteropServices;

namespace PrjFSLib.Mac.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Callbacks
    {
        public EnumerateDirectoryCallback OnEnumerateDirectory;
        public GetFileStreamCallback OnGetFileStream;
        public NotifyOperationCallback OnNotifyOperation;
        public LogErrorCallback OnLogError;
        public LogWarningCallback OnLogWarning;
        public LogInfoCallback OnLogInfo;
    }
}
