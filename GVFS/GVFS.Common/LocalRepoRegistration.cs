using System.Text.Json;
using System.Text.Json.Serialization;

namespace GVFS.Common
{
    /// <summary>
    /// One entry in the user-level repo registry on disk. Field set and
    /// JSON shape MUST match GVFS.Service.RepoRegistration so that the
    /// user-level registry file (written by <see cref="LocalRepoRegistry"/>)
    /// is wire-compatible with any registry the legacy service has written
    /// in the past. If a new field is added here, the same field must also
    /// be added to GVFS.Service.RepoRegistration (and vice versa) along
    /// with a registry-format-version bump.
    /// </summary>
    public class LocalRepoRegistration
    {
        public LocalRepoRegistration()
        {
        }

        public LocalRepoRegistration(string enlistmentRoot, string ownerSID)
        {
            this.EnlistmentRoot = enlistmentRoot;
            this.OwnerSID = ownerSID;
            this.IsActive = true;
        }

        public string EnlistmentRoot { get; set; }
        public string OwnerSID { get; set; }
        public bool IsActive { get; set; }

        // Uses LocalRepoRegistrationJsonContext (assembly-local source generator)
        // rather than GVFSJsonContext. The service-side RepoRegistration uses
        // its own ServiceJsonContext for the same reason — neither type can be
        // registered in GVFSJsonContext because GVFSJsonContext lives in
        // GVFS.Common and the service-side type lives in GVFS.Service (wrong
        // dependency direction). Keeping symmetric local contexts here means
        // the on-disk JSON shape is governed by identical source-gen behavior
        // on both sides.
        public static LocalRepoRegistration FromJson(string json)
        {
            return JsonSerializer.Deserialize(json, LocalRepoRegistrationJsonContext.Default.LocalRepoRegistration);
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, LocalRepoRegistrationJsonContext.Default.LocalRepoRegistration);
        }

        public override string ToString()
        {
            return string.Format(
                "({0} - {1}) {2}",
                this.IsActive ? "Active" : "Inactive",
                this.OwnerSID,
                this.EnlistmentRoot);
        }
    }

    [JsonSerializable(typeof(LocalRepoRegistration))]
    internal partial class LocalRepoRegistrationJsonContext : JsonSerializerContext
    {
    }
}
