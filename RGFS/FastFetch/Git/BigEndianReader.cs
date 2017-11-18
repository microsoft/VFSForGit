using System.IO;
using System.Text;

namespace FastFetch.Git
{
    public class BigEndianReader : BinaryReader
    {
        public BigEndianReader(Stream input)
            : base(input, Encoding.Default, leaveOpen: true)
        {
        }

        public override short ReadInt16()
        {
            return EndianHelper.Swap(base.ReadInt16());
        }

        public override int ReadInt32()
        {
            return EndianHelper.Swap(base.ReadInt32());
        }

        public override long ReadInt64()
        {
            return EndianHelper.Swap(base.ReadInt64());
        }

        public override ushort ReadUInt16()
        {
            return EndianHelper.Swap(base.ReadUInt16());
        }

        public override uint ReadUInt32()
        {
            return EndianHelper.Swap(base.ReadUInt32());
        }

        public override ulong ReadUInt64()
        {
            return EndianHelper.Swap(base.ReadUInt64());
        }
    }
}