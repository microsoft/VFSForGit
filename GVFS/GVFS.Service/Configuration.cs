using GVFS.Common;
using System;
using System.IO;

namespace GVFS.Service
{
    public class Configuration
    {
        /// <summary>
        /// Subdirectory name for the junction that points to the active version.
        /// When a versioned install layout is present, <c>{app}\Current</c> is a
        /// junction pointing to <c>{app}\Versions\{version}</c>.
        /// </summary>
        public const string CurrentVersionDirName = "Current";

        private static readonly Lazy<string> assemblyPath =
            new Lazy<string>(ProcessHelper.GetCurrentProcessLocation);

        private static Configuration instance = new Configuration();

        private Configuration()
        {
        }

        public static Configuration Instance
        {
            get
            {
                return instance;
            }
        }

        /// <summary>
        /// The directory containing <c>GVFS.Service.exe</c> (the install root).
        /// </summary>
        public static string AssemblyPath => assemblyPath.Value;

        /// <summary>
        /// The directory containing the current version's binaries.
        /// With versioned layout this resolves through the <c>Current</c> junction
        /// (e.g. <c>{app}\Current</c> → <c>{app}\Versions\1.0.X</c>).
        /// Falls back to <see cref="AssemblyPath"/> when no <c>Current</c>
        /// junction exists (flat/legacy layout).
        /// Evaluated on each access so that junction re-targeting is picked up
        /// without restarting the service.
        /// </summary>
        public static string CurrentVersionPath
        {
            get
            {
                string currentDir = Path.Combine(AssemblyPath, CurrentVersionDirName);
                if (Directory.Exists(currentDir))
                {
                    return currentDir;
                }

                // Legacy flat layout — binaries are siblings of the service.
                return AssemblyPath;
            }
        }

        /// <summary>
        /// Full path to the current version's <c>GVFS.exe</c>. Resolved
        /// dynamically so that junction re-targeting takes effect immediately.
        /// </summary>
        public string GVFSLocation => Path.Combine(CurrentVersionPath, GVFSPlatform.Instance.Constants.GVFSExecutableName);
    }
}
