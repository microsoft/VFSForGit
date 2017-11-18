using RGFS.GVFlt;
namespace RGFS.UnitTests.Mock.RGFS.GvFlt
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
    }
}
