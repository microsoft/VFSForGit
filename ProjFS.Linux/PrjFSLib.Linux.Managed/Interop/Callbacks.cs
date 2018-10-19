using System.Runtime.InteropServices;

namespace PrjFSLib.Linux.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Callbacks
    {
        public EnumerateDirectoryCallback OnEnumerateDirectory;
        public GetFileStreamCallback OnGetFileStream;
        public NotifyOperationCallback OnNotifyOperation;
    }
}
