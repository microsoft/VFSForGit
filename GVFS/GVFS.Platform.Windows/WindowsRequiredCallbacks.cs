using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Windows.ProjFS;

namespace GVFS.Platform.Windows
{
    public class WindowsRequiredCallbacks : IRequiredCallbacks
    {
        public delegate HResult EndDirectoryEnumerationDelegate(Guid enumerationId);
        public delegate HResult GetDirectoryEnumerationCallbackDelegate(int commandId, Guid enumerationId, string filterFileName, bool restartScan, IDirectoryEnumerationResults result);
        public delegate HResult GetFileDataCallbackDelegate(int commandId, string relativePath, ulong byteOffset, uint length, Guid dataStreamId, byte[] contentId, byte[] providerId, uint triggeringProcessId, string triggeringProcessImageFileName);
        public delegate HResult GetPlaceholderInfoCallbackDelegate(int commandId, string relativePath, uint triggeringProcessId, string triggeringProcessImageFileName);
        public delegate HResult StartDirectoryEnumerationDelegate(int commandId, Guid enumerationId, string relativePath, uint triggeringProcessId, string triggeringProcessImageFileName);

        public EndDirectoryEnumerationDelegate OnEndDirectoryEnumeration { get; set; }
        public GetDirectoryEnumerationCallbackDelegate OnGetDirectoryEnumeration { get; set; }
        public GetFileDataCallbackDelegate OnGetFileStream { get; set; }
        public GetPlaceholderInfoCallbackDelegate OnGetPlaceholderInformation { get; set; }
        public StartDirectoryEnumerationDelegate OnStartDirectoryEnumeration { get; set; }

        public HResult EndDirectoryEnumerationCallback(Guid enumerationId)
        {
            return this.OnEndDirectoryEnumeration(enumerationId);
        }

        public HResult GetDirectoryEnumerationCallback(int commandId, Guid enumerationId, string filterFileName, bool restartScan, IDirectoryEnumerationResults result)
        {
            return this.OnGetDirectoryEnumeration(commandId, enumerationId, filterFileName, restartScan, result);
        }

        public HResult GetFileDataCallback(int commandId, string relativePath, ulong byteOffset, uint length, Guid dataStreamId, byte[] contentId, byte[] providerId, uint triggeringProcessId, string triggeringProcessImageFileName)
        {
            return this.OnGetFileStream(commandId, relativePath, byteOffset, length, dataStreamId, contentId, providerId, triggeringProcessId, triggeringProcessImageFileName);
        }

        public HResult GetPlaceholderInfoCallback(int commandId, string relativePath, uint triggeringProcessId, string triggeringProcessImageFileName)
        {
            return this.OnGetPlaceholderInformation(commandId, relativePath, triggeringProcessId, triggeringProcessImageFileName);
        }

        public HResult StartDirectoryEnumerationCallback(int commandId, Guid enumerationId, string relativePath, uint triggeringProcessId, string triggeringProcessImageFileName)
        {
            return this.OnStartDirectoryEnumeration(commandId, enumerationId, relativePath, triggeringProcessId, triggeringProcessImageFileName);
        }
    }
}
