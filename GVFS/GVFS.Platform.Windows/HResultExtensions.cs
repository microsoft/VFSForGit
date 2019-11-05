using Microsoft.Windows.ProjFS;

namespace GVFS.Platform.Windows
{
    public class HResultExtensions
    {
        public const int GenericProjFSError = -2147024579; // returned by ProjFS::DeleteFile() on Win server 2016 while deleting a partial file

        private const int FacilityNtBit = 0x10000000; // FACILITY_NT_BIT
        private const int FacilityWin32 = 7;          // FACILITY_WIN32

        // #define HRESULT_FROM_NT(x)      ((HRESULT) ((x) | FACILITY_NT_BIT))
        public enum HResultFromNtStatus : int
        {
            FileNotAvailable = unchecked((int)0xC0000467) | FacilityNtBit,       // STATUS_FILE_NOT_AVAILABLE
            FileClosed = unchecked((int)0xC0000128) | FacilityNtBit,             // STATUS_FILE_CLOSED
            IoReparseTagNotHandled = unchecked((int)0xC0000279) | FacilityNtBit, // STATUS_IO_REPARSE_TAG_NOT_HANDLED
            DeviceNotReady = unchecked((int)0xC00000A3L) | FacilityNtBit,        // STATUS_DEVICE_NOT_READY
        }

        // HRESULT_FROM_WIN32(unsigned long x) { return (HRESULT)(x) <= 0 ? (HRESULT)(x) : (HRESULT) (((x) & 0x0000FFFF) | (FACILITY_WIN32 << 16) | 0x80000000);}
        public static HResult HResultFromWin32(int win32error)
        {
            return win32error <= 0 ? (HResult)win32error : (HResult)unchecked((win32error & 0x0000FFFF) | (FacilityWin32 << 16) | 0x80000000);
        }
    }
}
