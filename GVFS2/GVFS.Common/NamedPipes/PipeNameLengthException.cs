using System;

namespace GVFS.Common.NamedPipes
{
    public class PipeNameLengthException : Exception
    {
        public PipeNameLengthException(string message)
            : base(message)
        {
        }
    }
}
