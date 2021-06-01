using System.IO;
using System.Text;

namespace GVFS.Common.NamedPipes
{
    public class NamedPipeStreamWriter
    {
        private const byte TerminatorByte = 0x3;
        private const string TerminatorByteString = "\x3";
        private Stream stream;

        public NamedPipeStreamWriter(Stream stream)
        {
            this.stream = stream;
        }

        public void WriteMessage(string message)
        {
            byte[] byteBuffer = Encoding.UTF8.GetBytes(message + TerminatorByteString);
            this.stream.Write(byteBuffer, 0, byteBuffer.Length);
            this.stream.Flush();
        }
    }
}
