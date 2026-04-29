using System.Text.Json;

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

        // Uses ServiceJsonContext (assembly-local source generator) instead of
        // GVFSJsonOptions because RepoRegistration cannot be registered in
        // GVFSJsonContext (GVFS.Common) — wrong assembly direction. The
        // reflection fallback in GVFSJsonOptions fails under native AOT trimming.
        public static RepoRegistration FromJson(string json)
        {
            return JsonSerializer.Deserialize(json, ServiceJsonContext.Default.RepoRegistration);
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
            return JsonSerializer.Serialize(this, ServiceJsonContext.Default.RepoRegistration);
        }
    }
}