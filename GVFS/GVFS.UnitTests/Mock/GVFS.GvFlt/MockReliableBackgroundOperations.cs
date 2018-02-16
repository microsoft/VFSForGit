using GVFS.GVFlt;
namespace GVFS.UnitTests.Mock.GVFS.GvFlt
{
    public class MockReliableBackgroundOperations : ReliableBackgroundOperations
    {
        public MockReliableBackgroundOperations()
        {
        }

        public override int Count
        {
            get
            {
                return 0;
            }
        }

        public override void Start()
        {
        }

        public override void Enqueue(GVFltCallbacks.BackgroundGitUpdate backgroundOperation)
        {
        }

        public override void Shutdown()
        {
        }
    }
}
