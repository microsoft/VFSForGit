namespace GVFS.Common.Git
{
    public static class EndianHelper
    {
        public static short Swap(short source)
        {
            return (short)Swap((ushort)source);
        }

        public static int Swap(int source)
        {
            return (int)Swap((uint)source);
        }

        public static long Swap(long source)
        {
            return (long)((ulong)source);
        }

        public static ushort Swap(ushort source)
        {
            return (ushort)(((source & 0x000000FF) << 8) |
                ((source & 0x0000FF00) >> 8));
        }

        public static uint Swap(uint source)
        {
            return
                ((source & 0x000000FF) << 24) |
                ((source & 0x0000FF00) << 8)  |
                ((source & 0x00FF0000) >> 8)  |
                ((source & 0xFF000000) >> 24);
        }

        public static ulong Swap(ulong source)
        {
            return
                ((source & 0x00000000000000FF) << 56) |
                ((source & 0x000000000000FF00) << 40) |
                ((source & 0x0000000000FF0000) << 24) |
                ((source & 0x00000000FF000000) << 8)  |
                ((source & 0x000000FF00000000) >> 8)  |
                ((source & 0x0000FF0000000000) >> 24) |
                ((source & 0x00FF000000000000) >> 40) |
                ((source & 0xFF00000000000000) >> 56);
        }
    }
}
