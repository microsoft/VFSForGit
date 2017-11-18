using RGFS.Common.Http;

namespace RGFS.UnitTests.Mock
{
    public class MockCacheServerInfo : CacheServerInfo
    {
        public MockCacheServerInfo() : base("https://mock", "mock")
        {
        }
    }
}
