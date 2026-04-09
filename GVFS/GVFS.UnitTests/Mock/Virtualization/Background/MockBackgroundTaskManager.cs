using GVFS.Virtualization.Background;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Mock.Virtualization.Background
{
    public class MockBackgroundFileSystemTaskRunner : BackgroundFileSystemTaskRunner
    {
        private Func<FileSystemTaskResult> preCallback;
        private Func<FileSystemTask, FileSystemTaskResult> callback;
        private Func<FileSystemTaskResult> postCallback;

        public MockBackgroundFileSystemTaskRunner()
        {
            this.BackgroundTasks = new List<FileSystemTask>();
        }

        public List<FileSystemTask> BackgroundTasks { get; private set; }

        public override bool IsEmpty => this.BackgroundTasks.Count == 0;

        public override int Count
        {
            get
            {
                return this.BackgroundTasks.Count;
            }
        }

        public override void SetCallbacks(
            Func<FileSystemTaskResult> preCallback,
            Func<FileSystemTask, FileSystemTaskResult> callback,
            Func<FileSystemTaskResult> postCallback)
        {
            this.preCallback = preCallback;
            this.callback = callback;
            this.postCallback = postCallback;
        }

        public override void Start()
        {
        }

        public override void Enqueue(FileSystemTask backgroundTask)
        {
            this.BackgroundTasks.Add(backgroundTask);
        }

        public override void Shutdown()
        {
        }

        public void ProcessTasks()
        {
            this.preCallback();

            foreach (FileSystemTask task in this.BackgroundTasks)
            {
                this.callback(task);
            }

            this.postCallback();
            this.BackgroundTasks.Clear();
        }
    }
}
