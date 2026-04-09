
namespace GVFS.Common
{
    public class VersionResponse
    {
        public string Version { get; set; }

        public static VersionResponse FromJsonString(string jsonString)
        {
            return GVFSJsonOptions.Deserialize<VersionResponse>(jsonString);
        }
    }
}
