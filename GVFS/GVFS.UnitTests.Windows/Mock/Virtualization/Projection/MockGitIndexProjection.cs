using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Virtualization.Background;
using GVFS.Virtualization.BlobSize;
using GVFS.Virtualization.Projection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace GVFS.UnitTests.Mock.Virtualization.Projection
{
    public class MockGitIndexProjection : GitIndexProjection
    {
        private ConcurrentHashSet<string> projectedFiles;

        private ManualResetEvent unblockGetProjectedItems;
        private ManualResetEvent waitForGetProjectedItems;

        private ManualResetEvent unblockIsPathProjected;
        private ManualResetEvent waitForIsPathProjected;

        private ManualResetEvent unblockGetProjectedFileInfo;
        private ManualResetEvent waitForGetProjectedFileInfo;

        private AutoResetEvent placeholderCreated;

        public MockGitIndexProjection(IEnumerable<string> projectedFiles)
        {
            this.projectedFiles = new ConcurrentHashSet<string>();
            foreach (string entry in projectedFiles)
            {
                this.projectedFiles.Add(entry);
            }

            this.PlaceholdersCreated = new ConcurrentHashSet<string>();
            this.ExpandedFolders = new ConcurrentHashSet<string>();
            this.MockFileTypesAndModes = new ConcurrentDictionary<string, ushort>();
            this.SparseEntries = new ConcurrentHashSet<string>();

            this.unblockGetProjectedItems = new ManualResetEvent(true);
            this.waitForGetProjectedItems = new ManualResetEvent(true);

            this.unblockIsPathProjected = new ManualResetEvent(true);
            this.waitForIsPathProjected = new ManualResetEvent(true);

            this.unblockGetProjectedFileInfo = new ManualResetEvent(true);
            this.waitForGetProjectedFileInfo = new ManualResetEvent(true);

            this.placeholderCreated = new AutoResetEvent(false);
        }

        public bool EnumerationInMemory { get; set; }

        public ConcurrentHashSet<string> PlaceholdersCreated { get; }

        public ConcurrentHashSet<string> ExpandedFolders { get; }

        public ConcurrentDictionary<string, ushort> MockFileTypesAndModes { get; }

        public ConcurrentHashSet<string> SparseEntries { get; }

        public bool ThrowOperationCanceledExceptionOnProjectionRequest { get; set; }

        public bool ProjectionParseComplete { get; set; }

        public PathSparseState GetFolderPathSparseStateValue { get; set; } = PathSparseState.Included;
        public bool TryAddSparseFolderReturnValue { get; set; } = true;

        public override bool IsProjectionParseComplete()
        {
            return this.ProjectionParseComplete;
        }

        public override PathSparseState GetFolderPathSparseState(string virtualPath)
        {
            return this.GetFolderPathSparseStateValue;
        }

        public override bool TryAddSparseFolder(string virtualPath)
        {
            if (this.TryAddSparseFolderReturnValue)
            {
                this.SparseEntries.Add(virtualPath);
            }

            return this.TryAddSparseFolderReturnValue;
        }

        public void BlockGetProjectedItems(bool willWaitForRequest)
        {
            if (willWaitForRequest)
            {
                this.waitForGetProjectedItems.Reset();
            }

            this.unblockGetProjectedItems.Reset();
        }

        public void UnblockGetProjectedItems()
        {
            this.unblockGetProjectedItems.Set();
        }

        public void WaitForGetProjectedItems()
        {
            this.waitForIsPathProjected.WaitOne();
        }

        public override FileSystemTaskResult OpenIndexForRead()
        {
            return FileSystemTaskResult.Success;
        }

        public void BlockIsPathProjected(bool willWaitForRequest)
        {
            if (willWaitForRequest)
            {
                this.waitForIsPathProjected.Reset();
            }

            this.unblockIsPathProjected.Reset();
        }

        public void UnblockIsPathProjected()
        {
            this.unblockIsPathProjected.Set();
        }

        public void WaitForIsPathProjected()
        {
            this.waitForIsPathProjected.WaitOne();
        }

        public void BlockGetProjectedFileInfo(bool willWaitForRequest)
        {
            if (willWaitForRequest)
            {
                this.waitForGetProjectedFileInfo.Reset();
            }

            this.unblockGetProjectedFileInfo.Reset();
        }

        public void UnblockGetProjectedFileInfo()
        {
            this.unblockGetProjectedFileInfo.Set();
        }

        public void WaitForGetProjectedFileInfo()
        {
            this.waitForGetProjectedFileInfo.WaitOne();
        }

        public void WaitForPlaceholderCreate()
        {
            this.placeholderCreated.WaitOne();
        }

        public override void Initialize(BackgroundFileSystemTaskRunner backgroundQueue)
        {
        }

        public override void Shutdown()
        {
        }

        public override void InvalidateProjection()
        {
        }

        public override bool TryGetProjectedItemsFromMemory(string folderPath, out List<ProjectedFileInfo> projectedItems)
        {
            if (this.EnumerationInMemory)
            {
                projectedItems = this.projectedFiles.Select(name => new ProjectedFileInfo(name, size: 0, isFolder: false, sha: new Sha1Id(1, 1, 1))).ToList();
                return true;
            }

            projectedItems = null;
            return false;
        }

        public override void GetFileTypeAndMode(string path, out FileType fileType, out ushort fileMode)
        {
            fileType = FileType.Invalid;
            fileMode = 0;

            ushort mockFileTypeAndMode;
            if (this.MockFileTypesAndModes.TryGetValue(path, out mockFileTypeAndMode))
            {
                FileTypeAndMode typeAndMode = new FileTypeAndMode(mockFileTypeAndMode);
                fileType = typeAndMode.Type;
                fileMode = typeAndMode.Mode;
            }
        }

        public override List<ProjectedFileInfo> GetProjectedItems(
            CancellationToken cancellationToken,
            BlobSizes.BlobSizesConnection blobSizesConnection,
            string folderPath)
        {
            this.waitForGetProjectedItems.Set();

            if (this.ThrowOperationCanceledExceptionOnProjectionRequest)
            {
                throw new OperationCanceledException();
            }

            this.unblockGetProjectedItems.WaitOne();
            return this.projectedFiles.Select(name => new ProjectedFileInfo(name, size: 0, isFolder: false, sha: new Sha1Id(1, 1, 1))).ToList();
        }

        public override bool IsPathProjected(string virtualPath, out string fileName, out bool isFolder)
        {
            this.waitForIsPathProjected.Set();
            this.unblockIsPathProjected.WaitOne();

            if (this.projectedFiles.Contains(virtualPath))
            {
                isFolder = false;
                string parentKey;
                this.GetChildNameAndParentKey(virtualPath, out fileName, out parentKey);
                return true;
            }

            fileName = string.Empty;
            isFolder = false;
            return false;
        }

        public override ProjectedFileInfo GetProjectedFileInfo(
            CancellationToken cancellationToken,
            BlobSizes.BlobSizesConnection blobSizesConnection,
            string virtualPath,
            out string parentFolderPath)
        {
            this.waitForGetProjectedFileInfo.Set();

            if (this.ThrowOperationCanceledExceptionOnProjectionRequest)
            {
                throw new OperationCanceledException();
            }

            this.unblockGetProjectedFileInfo.WaitOne();

            if (this.projectedFiles.Contains(virtualPath))
            {
                string childName;
                string parentKey;
                this.GetChildNameAndParentKey(virtualPath, out childName, out parentKey);
                parentFolderPath = parentKey;
                return new ProjectedFileInfo(childName, size: 0, isFolder: false, sha: new Sha1Id(1, 1, 1));
            }

            parentFolderPath = null;
            return null;
        }

        public override void OnPlaceholderFolderExpanded(string relativePath)
        {
            this.ExpandedFolders.Add(relativePath);
        }

        public override void OnPlaceholderFileCreated(string virtualPath, string sha)
        {
            this.PlaceholdersCreated.Add(virtualPath);
            this.placeholderCreated.Set();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.unblockGetProjectedItems != null)
                {
                    this.unblockGetProjectedItems.Dispose();
                    this.unblockGetProjectedItems = null;
                }

                if (this.waitForGetProjectedItems != null)
                {
                    this.waitForGetProjectedItems.Dispose();
                    this.waitForGetProjectedItems = null;
                }

                if (this.unblockIsPathProjected != null)
                {
                    this.unblockIsPathProjected.Dispose();
                    this.unblockIsPathProjected = null;
                }

                if (this.waitForIsPathProjected != null)
                {
                    this.waitForIsPathProjected.Dispose();
                    this.waitForIsPathProjected = null;
                }

                if (this.unblockGetProjectedFileInfo != null)
                {
                    this.unblockGetProjectedFileInfo.Dispose();
                    this.unblockGetProjectedFileInfo = null;
                }

                if (this.waitForGetProjectedFileInfo != null)
                {
                    this.waitForGetProjectedFileInfo.Dispose();
                    this.waitForGetProjectedFileInfo = null;
                }

                if (this.placeholderCreated != null)
                {
                    this.placeholderCreated.Dispose();
                    this.placeholderCreated = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
