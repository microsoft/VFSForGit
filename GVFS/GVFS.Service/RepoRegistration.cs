using GVFS.Common;

namespace GVFS.Service
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
            return GVFSJsonOptions.Deserialize<RepoRegistration>(json);
        }

        public override string ToString()
        {
            return
                string.Format(
                    "({0} - {1}) {2}",
                    this.IsActive ? "Active" : "Inactive",
                    this.OwnerSID,
                    this.EnlistmentRoot);
        }

        public string ToJson()
        {
            return GVFSJsonOptions.Serialize(this);
        }
    }
}