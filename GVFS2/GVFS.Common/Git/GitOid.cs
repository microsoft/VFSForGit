using System.Runtime.InteropServices;

namespace GVFS.Common.Git
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GitOid
    {
        // OIDs are 20 bytes long
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] Id;

        public override string ToString()
        {
            return SHA1Util.HexStringFromBytes(this.Id);
        }
    }
}
