using System;

namespace GVFS.Common
{
    public interface IBackgroundOperation
    {
        Guid Id { get; set; }
    }
}
