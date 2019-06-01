using GVFS.Common;
using GVFS.Tests.Should;
using PrjFSLib.Linux;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace GVFS.UnitTests.Mock.Linux
{
    public class MockVirtualizationInstance : VirtualizationInstance, IDisposable
    {
        private AutoResetEvent commandCompleted;

        public MockVirtualizationInstance()
        {
            this.commandCompleted = new AutoResetEvent(false);
            this.CreatedPlaceholders = new ConcurrentDictionary<string, uint>();
            this.UpdatedPlaceholders = new ConcurrentDictionary<string, uint>();
            this.CreatedSymLinks = new ConcurrentHashSet<string>();
            this.WriteFileReturnResult = Result.Success;
        }

        public Result CompletionResult { get; set; }
        public uint BytesWritten { get; private set; }
        public Result WriteFileReturnResult { get; set; }
        public Result UpdatePlaceholderIfNeededResult { get; set; }
        public UpdateFailureCause UpdatePlaceholderIfNeededFailureCause { get; set; }
        public Result DeleteFileResult { get; set; }
        public UpdateFailureCause DeleteFileUpdateFailureCause { get; set; }

        public ConcurrentDictionary<string, uint> CreatedPlaceholders { get; private set; }
        public ConcurrentDictionary<string, uint> UpdatedPlaceholders { get; private set; }

        public ConcurrentHashSet<string> CreatedSymLinks { get; }

        public override EnumerateDirectoryCallback OnEnumerateDirectory { get; set; }
        public override GetFileStreamCallback OnGetFileStream { get; set; }

        public override Result StartVirtualizationInstance(
            string storageRootFullPath,
            string virtualizationRootFullPath,
            uint poolThreadCount,
            bool initializeStorageRoot)
        {
            poolThreadCount.ShouldBeAtLeast(1U, "poolThreadCount must be greater than 0");
            return Result.Success;
        }

        public override void StopVirtualizationInstance()
        {
        }

        public override IEnumerable<string> EnumerateFileSystemEntries(string path)
        {
            yield break;
        }

        public override Result WriteFileContents(
            int fd,
            byte[] bytes,
            uint byteCount)
        {
            this.BytesWritten = byteCount;
            return this.WriteFileReturnResult;
        }

        public override Result DeleteFile(
            string relativePath,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            failureCause = this.DeleteFileUpdateFailureCause;
            return this.DeleteFileResult;
        }

        public override Result WritePlaceholderDirectory(
            string relativePath)
        {
            throw new NotImplementedException();
        }

        public override Result WritePlaceholderFile(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            uint fileMode)
        {
            this.CreatedPlaceholders.TryAdd(relativePath, fileMode);
            return Result.Success;
        }

        public override Result WriteSymLink(
            string relativePath,
            string symLinkTarget)
        {
            this.CreatedSymLinks.Add(relativePath);
            return Result.Success;
        }

        public override Result UpdatePlaceholderIfNeeded(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            uint fileMode,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            failureCause = this.UpdatePlaceholderIfNeededFailureCause;
            if (failureCause == UpdateFailureCause.NoFailure)
            {
                this.UpdatedPlaceholders[relativePath] = fileMode;
            }

            return this.UpdatePlaceholderIfNeededResult;
        }

        public override Result ReplacePlaceholderFileWithSymLink(
            string relativePath,
            string symLinkTarget,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            this.CreatedSymLinks.Add(relativePath);
            failureCause = this.UpdatePlaceholderIfNeededFailureCause;
            return this.UpdatePlaceholderIfNeededResult;
        }

        public override Result CompleteCommand(
            ulong commandId,
            Result result)
        {
            this.CompletionResult = result;
            this.commandCompleted.Set();
            return Result.Success;
        }

        public Result WaitForCompletionStatus()
        {
            this.commandCompleted.WaitOne();
            return this.CompletionResult;
        }

        public override Result ConvertDirectoryToPlaceholder(
            string relativeDirectoryPath)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            if (this.commandCompleted != null)
            {
                this.commandCompleted.Dispose();
                this.commandCompleted = null;
            }
        }
    }
}
