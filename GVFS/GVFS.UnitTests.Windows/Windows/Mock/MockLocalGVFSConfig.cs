using GVFS.Common;
using System.Collections.Generic;

namespace GVFS.UnitTests.Windows.Mock.Upgrader
{
    public class MockLocalGVFSConfig : LocalGVFSConfig
    {
        public MockLocalGVFSConfig()
        {
            this.MockSettings = new Dictionary<string, string>();
        }

        private Dictionary<string, string> MockSettings { get; set; }

        public override bool TryGetAllConfig(out Dictionary<string, string> allConfig, out string error)
        {
            allConfig = new Dictionary<string, string>(this.MockSettings);
            error = null;

            return true;
        }

        public override bool TryGetConfig(
            string name,
            out string value,
            out string error)
        {
            error = null;

            return this.MockSettings.TryGetValue(name, out value);
        }

        public override bool TrySetConfig(
            string name,
            string value,
            out string error)
        {
            error = null;
            this.MockSettings[name] = value;

            return true;
        }

        public override bool TryRemoveConfig(string name, out string error)
        {
            error = null;
            this.MockSettings.Remove(name);

            return true;
        }
    }
}
