using System.Collections.Generic;

namespace GVFS.Common.Git
{
    public class GitConfigSetting
    {
        public const string CoreVirtualizeObjectsName = "core.virtualizeobjects";
        public const string CoreVirtualFileSystemName = "core.virtualfilesystem";
        public const string CredentialUseHttpPath = "credential.useHttpPath";

        public const string SslCert = "http.sslcert";
        public const string SslKey = "http.sslkey";
        public const string SslVerify = "http.sslverify";
        public const string SslCertPasswordProtected = "http.sslcertpasswordprotected";

        public GitConfigSetting(string name, params string[] values)
        {
            this.Name = name;
            this.Values = new HashSet<string>(values);
        }

        public string Name { get; }
        public HashSet<string> Values { get; }

        public bool HasValue(string value)
        {
            return this.Values.Contains(value);
        }

        public void Add(string value)
        {
            this.Values.Add(value);
        }
    }
}
