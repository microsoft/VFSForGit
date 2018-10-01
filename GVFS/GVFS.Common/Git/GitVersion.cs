using System;

namespace GVFS.Common.Git
{
    public class GitVersion
    {
        public GitVersion(int major, int minor, int build, string platform, int revision, int minorRevision)
        {
            this.Major = major;
            this.Minor = minor;
            this.Build = build;
            this.Platform = platform;
            this.Revision = revision;
            this.MinorRevision = minorRevision;
        }

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public string Platform { get; private set; }
        public int Build { get; private set; }
        public int Revision { get; private set; }
        public int MinorRevision { get; private set; }

        public static bool TryParseGitVersionCommandResult(string input, out GitVersion version)
        {
            // git version output is of the form
            // git version 2.17.0.gvfs.1.preview.3

            const string GitVersionExpectedPrefix = "git version ";

            if (input.StartsWith(GitVersionExpectedPrefix))
            {
                input = input.Substring(GitVersionExpectedPrefix.Length);
            }

            return TryParseVersion(input, out version);
        }

        public static bool TryParseInstallerName(string input, string installerExtension, out GitVersion version)
        {
            // Installer name is of the form
            // Git-2.14.1.gvfs.1.1.gb16030b-64-bit.exe

            version = null;

            if (!input.StartsWith("Git-", StringComparison.InvariantCultureIgnoreCase))
            {
                return false;
            }

            if (!input.EndsWith("-64-bit" + installerExtension, StringComparison.InvariantCultureIgnoreCase))
            {
                return false;
            }

            return TryParseVersion(input.Substring(4, input.Length - 15), out version);
        }

        public static bool TryParseVersion(string input, out GitVersion version)
        {
            version = null;
            int major, minor, build, revision, minorRevision;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string[] parsedComponents = input.Split(new char[] { '.' });
            int parsedComponentsLength = parsedComponents.Length;
            if (parsedComponentsLength < 5)
            {
                return false;
            }

            if (!TryParseComponent(parsedComponents[0], out major))
            {
                return false;
            }

            if (!TryParseComponent(parsedComponents[1], out minor))
            {
                return false;
            }

            if (!TryParseComponent(parsedComponents[2], out build))
            {
                return false;
            }

            if (!TryParseComponent(parsedComponents[4], out revision))
            {
                return false;
            }

            if (parsedComponentsLength < 6 || !TryParseComponent(parsedComponents[5], out minorRevision))
            {
                minorRevision = 0;
            }

            string platform = parsedComponents[3];

            version = new GitVersion(major, minor, build, platform, revision, minorRevision);
            return true;
        }

        public bool IsEqualTo(GitVersion other)
        {
            if (this.Platform != other.Platform)
            {
                return false;
            }

            return this.CompareVersionNumbers(other) == 0;
        }

        public bool IsLessThan(GitVersion other)
        {
            return this.CompareVersionNumbers(other) < 0;
        }

        public override string ToString()
        {
            return string.Format("{0}.{1}.{2}.{3}.{4}.{5}", this.Major, this.Minor, this.Build, this.Platform, this.Revision, this.MinorRevision);
        }

        private static bool TryParseComponent(string component, out int parsedComponent)
        {
            if (!int.TryParse(component, out parsedComponent))
            {
                return false;
            }

            if (parsedComponent < 0)
            {
                return false;
            }

            return true;
        }

        private int CompareVersionNumbers(GitVersion other)
        {
            if (other == null)
            {
                return -1;
            }

            if (this.Major != other.Major)
            {
                return this.Major.CompareTo(other.Major);
            }

            if (this.Minor != other.Minor)
            {
                return this.Minor.CompareTo(other.Minor);
            }

            if (this.Build != other.Build)
            {
                return this.Build.CompareTo(other.Build);
            }

            if (this.Revision != other.Revision)
            {
                return this.Revision.CompareTo(other.Revision);
            }

            if (this.MinorRevision != other.MinorRevision)
            {
                return this.MinorRevision.CompareTo(other.MinorRevision);
            }

            return 0;
        }
    }
}
