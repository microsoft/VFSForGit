using System;

namespace GVFS.Common.Git
{
    public class GitVersion
    {
        public GitVersion(int major, int minor, int build, string platform = null, int revision = 0, int minorRevision = 0)
            : this(major, minor, build, null, platform, revision, minorRevision) { }

        public GitVersion(int major, int minor, int build, int? releaseCandidate = null, string platform = null, int revision = 0, int minorRevision = 0)
        {
            this.Major = major;
            this.Minor = minor;
            this.Build = build;
            this.ReleaseCandidate = releaseCandidate;
            this.Platform = platform;
            this.Revision = revision;
            this.MinorRevision = minorRevision;
        }

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Build { get; private set; }
        public int? ReleaseCandidate { get; private set; }
        public string Platform { get; private set; }
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

            int major, minor, build, revision = 0, minorRevision = 0;
            int? releaseCandidate = null;
            string platform = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string[] parsedComponents = input.Split('.');
            int numComponents = parsedComponents.Length;

            // We minimally accept the official Git version number format which
            // consists of three components: "major.minor.build[.rcN]".
            //
            // The other supported formats are the Git for Windows and Microsoft Git
            // formats which look like: "major.minor.build[.rcN].platform.revision.minorRevision"
            //      0     1     2            3        4        5
            // len  1     2     3            4        5        6
            //
            if (numComponents < 3)
            {
                return false;
            }

            // Major version
            if (!TryParseComponent(parsedComponents[0], out major))
            {
                return false;
            }

            // Minor version
            if (!TryParseComponent(parsedComponents[1], out minor))
            {
                return false;
            }

            // Build number
            if (!TryParseComponent(parsedComponents[2], out build))
            {
                return false;
            }

            // Release candidate and/or platform
            // Both of these are optional, but the release candidate is expected to be of the format 'rcN'
            // where N is a number, helping us distinguish it from a platform string.
            int platformIdx = 3;
            if (numComponents >= 4)
            {
                string tag = parsedComponents[3];

                // Release candidate 'rcN'
                if (tag.StartsWith("rc", StringComparison.OrdinalIgnoreCase) &&
                    tag.Length > 2 && int.TryParse(tag.Substring(2), out int rc) && rc >= 0)
                {
                    releaseCandidate = rc;

                    // The next component will now be the (optional) platform.
                    // Subsequent components will be revision and minor revision so we need to adjust
                    // the platform index to account for the release candidate.
                    platformIdx = 4;
                    if (numComponents >= 5)
                    {
                        platform = parsedComponents[4];
                    }
                }
                else // Platform string only
                {
                    platform = tag;
                }
            }

            // Platform revision
            if (numComponents > platformIdx + 1)
            {
                if (!TryParseComponent(parsedComponents[platformIdx + 1], out revision))
                {
                    revision = 0;
                }
            }

            // Minor platform revision
            if (numComponents > platformIdx + 2)
            {
                if (!TryParseComponent(parsedComponents[platformIdx + 2], out minorRevision))
                {
                    minorRevision = 0;
                }
            }

            version = new GitVersion(major, minor, build, releaseCandidate, platform, revision, minorRevision);
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
            if (ReleaseCandidate is null)
            {
                return $"{Major}.{Minor}.{Build}.{Platform}.{Revision}.{MinorRevision}";
            }

            return $"{Major}.{Minor}.{Build}.rc{ReleaseCandidate}.{Platform}.{Revision}.{MinorRevision}";
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

            if (this.ReleaseCandidate != other.ReleaseCandidate)
            {
                if (this.ReleaseCandidate.HasValue && other.ReleaseCandidate.HasValue)
                {
                    return this.ReleaseCandidate.Value.CompareTo(other.ReleaseCandidate.Value);
                }

                // If one version has a release candidate and the other does not,
                // the one without a release candidate is considered "greater than" the one with.
                return other.ReleaseCandidate.HasValue ? 1 : -1;
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
