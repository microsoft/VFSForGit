using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GVFS.UnitTests.Mock.Upgrader
{
    public class MockTextWriter : TextWriter
    {
        private StringBuilder stringBuilder;

        public MockTextWriter() : base()
        {
            this.AllLines = new List<string>();
            this.stringBuilder = new StringBuilder();
        }

        public List<string> AllLines { get; private set; }

        public override Encoding Encoding
        {
            get { return Encoding.Default; }
        }

        public override void Write(char value)
        {
            if (value.Equals('\r'))
            {
                return;
            }

            if (value.Equals('\n'))
            {
                this.AllLines.Add(this.stringBuilder.ToString());
                this.stringBuilder.Clear();
                return;
            }

            this.stringBuilder.Append(value);
        }

        public bool ContainsLine(string line)
        {
            return this.AllLines.Exists(x => x.Equals(line, StringComparison.Ordinal));
        }
    }
}
