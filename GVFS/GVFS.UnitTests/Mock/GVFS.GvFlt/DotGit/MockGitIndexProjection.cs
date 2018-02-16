using GVFS.Common;
using GVFS.GVFlt;
using GVFS.GVFlt.DotGit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace GVFS.UnitTests.Mock.GVFS.GvFlt.DotGit
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

            this.unblockGetProjectedItems = new ManualResetEvent(true);
            this.waitForGetProjectedItems = new ManualResetEvent(true);

            this.unblockIsPathProjected = new ManualResetEvent(true);
            this.waitForIsPathProjected = new ManualResetEvent(true);

            this.unblockGetProjectedFileInfo = new ManualResetEvent(true);
            this.waitForGetProjectedFileInfo = new ManualResetEvent(true);

            this.placeholderCreated = new AutoResetEvent(false);
        }

        public bool EnumerationInMemory { get; set; }

        public ConcurrentHashSet<string> PlaceholdersCreated { get; private set; }

        public bool ThrowOperationCanceledExceptionOnProjectionRequest { get; set; }

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

        public override void Initialize(ReliableBackgroundOperations backgroundQueue)
        {
        }

        public override void Shutdown()
        {
        }

        public override void InvalidateProjection()
        {
        }

        public override bool TryGetProjectedItemsFromMemory(string folderPath, out IEnumerable<GVFltFileInfo> projectedItems)
        {
            if (this.EnumerationInMemory)
            {
                projectedItems = this.projectedFiles.Select(name => new GVFltFileInfo(name, size: 0, isFolder: false)).ToList();
                return true;
            }

            projectedItems = null;
            return false;
        }

        public override IEnumerable<GVFltFileInfo> GetProjectedItems(string folderPath, CancellationToken cancellationToken)
        {
            this.waitForGetProjectedItems.Set();

            if (this.ThrowOperationCanceledExceptionOnProjectionRequest)
            {
                throw new OperationCanceledException();
            }

            this.unblockGetProjectedItems.WaitOne();
            return this.projectedFiles.Select(name => new GVFltFileInfo(name, size: 0, isFolder: false)).ToList();
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

        public override GVFltFileInfo GetProjectedGVFltFileInfoAndSha(CancellationToken cancellationToken, string virtualPath, out string parentFolderPath, out string sha)
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
                sha = "TestSha+" + virtualPath;
                return new GVFltFileInfo(childName, size: 0, isFolder: false);
            }

            parentFolderPath = null;
            sha = null;
            return null;
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
