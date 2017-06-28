using Newtonsoft.Json;
using System;

namespace GVFS.Common
{
    public class RepoRegistration
    {
        public RepoRegistration()
        {
        }

        public RepoRegistration(string enlistmentRoot)
        {
            this.EnlistmentRoot = enlistmentRoot;
            this.IsActive = true;
        }

        public string EnlistmentRoot { get; set; }
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
            return string.Format("({0}) {1}", this.IsActive ? "Active" : "Inactive", this.EnlistmentRoot);
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}