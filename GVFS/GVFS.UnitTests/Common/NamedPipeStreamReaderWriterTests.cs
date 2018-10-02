using GVFS.Common.NamedPipes;
using GVFS.Tests.Should;
using GVFS.UnitTests.Category;
using NUnit.Framework;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class NamedPipeStreamReaderWriterTests
    {
        private const int BufferSize = 256;

        private MemoryStream stream;
        private NamedPipeStreamWriter streamWriter;
        private NamedPipeStreamReader streamReader;

        [SetUp]
        public void Setup()
        {
            this.stream = new MemoryStream();
            this.streamWriter = new NamedPipeStreamWriter(this.stream);
            this.streamReader = new NamedPipeStreamReader(this.stream, BufferSize);
        }

        [Test]
        [Description("Verify that we can transmit multiple messages")]
        public void CanWriteAndReadMessages()
        {
            string firstMessage = @"This is a new message";
            this.TestTransmitMessage(firstMessage);

            string secondMessage = @"This is another message";
            this.TestTransmitMessage(secondMessage);

            string thirdMessage = @"This is the third message in a series of messages";
            this.TestTransmitMessage(thirdMessage);

            string longMessage = new string('T', 1024 * 5);
            this.TestTransmitMessage(longMessage);
        }

        [Test]
        [Description("Verify that we can transmit a message that contains content that is the size of a NamedPipeStreamReader's buffer")]
        public void CanSendBufferSizedContent()
        {
            string longMessage = new string('T', BufferSize);
            this.TestTransmitMessage(longMessage);
        }

        [Test]
        [Description("Verify that we can transmit message that is the same size a NamedPipeStreamReader's buffer")]
        public void CanSendBufferSizedMessage()
        {
            int numBytesInMessageTerminator = 1;
            string longMessage = new string('T', BufferSize - numBytesInMessageTerminator);
            this.TestTransmitMessage(longMessage);
        }

        [Test]
        [Description("Verify that the expected exception is thrown if message is not terminated with expected byte.")]
        [Category(CategoryConstants.ExceptionExpected)]
        public void ReadingPartialMessgeThrows()
        {
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes("This is a partial message");

            this.stream.Write(bytes, 0, bytes.Length);
            this.stream.Seek(0, SeekOrigin.Begin);

            Assert.Throws<IOException>(() => this.streamReader.ReadMessage());
        }

        [Test]
        [Description("Verify that we can transmit message that is larger than the buffer")]
        public void CanSendMultiBufferSizedMessage()
        {
            string longMessage = new string('T', BufferSize * 3);
            this.TestTransmitMessage(longMessage);
        }

        [Test]
        [Description("Verify that we can transmit message that newline characters")]
        public void CanSendNewLines()
        {
            string messageWithNewLines = "This is a \nstringwith\nnewlines";
            this.TestTransmitMessage(messageWithNewLines);
        }

        private void TestTransmitMessage(string message)
        {
            long pos = this.ReadStreamPosition();
            this.streamWriter.WriteMessage(message);

            this.SetStreamPosition(pos);

            string readMessage = this.streamReader.ReadMessage();
            readMessage.ShouldEqual(message, "The message read from the stream reader is not the same as the message that was sent.");
        }

        private long ReadStreamPosition()
        {
            return this.stream.Position;
        }

        private void SetStreamPosition(long position)
        {
            this.stream.Seek(position, SeekOrigin.Begin);
        }
    }
}
