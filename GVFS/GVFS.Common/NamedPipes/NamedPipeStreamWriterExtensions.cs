using System.IO;

namespace GVFS.Common.NamedPipes
{
    public static class NamedPipeStreamWriterExtensions
    {
        public const int Foo = 0;

        public static void WritePlatformIndependentLine(this StreamWriter writer, string value)
        {
            // WriteLine is not platform independent as on some platforms it terminates lines with \r\n
            // and on others it uses \n
            writer.Write(value + "\n");
        }
    }
}
