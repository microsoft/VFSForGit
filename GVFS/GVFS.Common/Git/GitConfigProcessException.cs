using System;

namespace GVFS.Common.Git
{
    internal sealed class GitConfigProcessException : Exception
    {
        internal GitConfigProcessException(Exception innerException)
            : base("The Git configuration process failed.", innerException)
        {
        }
    }
}
