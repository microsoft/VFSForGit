using Newtonsoft.Json;

namespace GVFS.Common
{
    public class RepoRegistration
    {
        public RepoRegistration()
        {
        }

        public RepoRegistration(string enlistmentRoot, string ownerSID)
        {
            this.EnlistmentRoot = enlistmentRoot;
            this.OwnerSID = ownerSID;
            this.IsActive = true;
        }

        public string EnlistmentRoot { get; set; }
        public string OwnerSID { get; set; }
        public bool IsActive { get; set; }

        public static RepoRegistration FromJson(string json)
        {
            return JsonConvert.DeserializeObject<RepoRegistration>(
                json,
                new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore
                });
        }

        public override string ToString()
        {
            return 
                string.Format(
                    "({0} - {1},{2}) {3}", 
                    this.IsActive ? "Active" : "Inactive", 
                    this.OwnerSID,
                    this.EnlistmentRoot);
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}