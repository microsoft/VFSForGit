using GVFS.Common;
using System.Collections.Generic;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockLocalGVFSConfig : LocalGVFSConfig
    {
        public MockLocalGVFSConfig()
        {
            this.Settings = new Dictionary<string, string>();
        }

        private Dictionary<string, string> Settings { get; set; }

        public override bool TryGetAllConfig(out Dictionary<string, string> allConfig, out string error)
        {
            allConfig = new Dictionary<string, string>(this.Settings);
            error = null;

            return true;
        }

        public override bool TryGetConfig(
            string name,
            out string value,
            out string error)
        {
            error = null;

            this.Settings.TryGetValue(name, out value);
            return true;
        }

        public override bool TrySetConfig(
            string name,
            string value,
            out string error)
        {
            error = null;
            this.Settings[name] = value;

            return true;
        }

        public override bool TryRemoveConfig(string name, out string error)
        {
            error = null;
            this.Settings.Remove(name);

            return true;
        }
    }
}
