using GVFS.Virtualization.Background;
using System.Collections.Generic;

namespace GVFS.UnitTests.Mock.Virtualization.Background
{
    public class MockBackgroundFileSystemTaskRunner : BackgroundFileSystemTaskRunner
    {
        public MockBackgroundFileSystemTaskRunner()
        {
            this.BackgroundTasks = new List<FileSystemTask>();
        }

        public List<FileSystemTask> BackgroundTasks { get; private set; }

        public override int Count
        {
            get
            {
                return this.BackgroundTasks.Count;
            }
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
    }
}
