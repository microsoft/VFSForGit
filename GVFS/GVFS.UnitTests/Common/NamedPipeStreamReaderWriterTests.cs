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
        private MemoryStream stream;
        private NamedPipeStreamWriter streamWriter;
        private NamedPipeStreamReader streamReader;

        [SetUp]
        public void Setup()
        {
            this.stream = new MemoryStream();
            this.streamWriter = new NamedPipeStreamWriter(this.stream);
            this.streamReader = new NamedPipeStreamReader(this.stream);
        }

        [Test]
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
        [Category(CategoryConstants.ExceptionExpected)]
        public void ReadingPartialMessgeThrows()
        {
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes("This is a partial message");

            this.stream.Write(bytes, 0, bytes.Length);
            this.stream.Seek(0, SeekOrigin.Begin);

            Assert.Throws<IOException>(() => this.streamReader.ReadMessage());
        }

        [Test]
        public void CanSendMessagesWithNewLines()
        {
            string messageWithNewLines = "This is a \nstringwith\nnewlines";
            this.TestTransmitMessage(messageWithNewLines);
        }

        [Test]
        public void CanSendMultipleMessagesSequentially()
        {
            string[] messages = new string[]
            {
                "This is a new message",
                "This is another message",
                "This is the third message in a series of messages"
            };

            this.TestTransmitMessages(messages);
        }

        private void TestTransmitMessage(string message)
        {
            long pos = this.ReadStreamPosition();
            this.streamWriter.WriteMessage(message);

            this.SetStreamPosition(pos);

            string readMessage = this.streamReader.ReadMessage();
            readMessage.ShouldEqual(message, "The message read from the stream reader is not the same as the message that was sent.");
        }

        private void TestTransmitMessages(string[] messages)
        {
            long pos = this.ReadStreamPosition();

            foreach (string message in messages)
            {
                this.streamWriter.WriteMessage(message);
            }

            this.SetStreamPosition(pos);

            foreach (string message in messages)
            {
                string readMessage = this.streamReader.ReadMessage();
                readMessage.ShouldEqual(message, "The message read from the stream reader is not the same as the message that was sent.");
            }
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
