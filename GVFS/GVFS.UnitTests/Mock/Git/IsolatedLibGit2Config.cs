using GVFS.Common;
using GVFS.Common.Git;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GVFS.UnitTests.Mock.Git
{
    /// <summary>
    /// Points libgit2's global, XDG, system, and ProgramData config search paths at an empty
    /// temporary directory, so a test does not read the machine's config. The search paths last
    /// only while libgit2 stays initialized, so this object holds a libgit2 reference until it
    /// is disposed. Unit tests run sequentially, so other tests do not observe the change.
    /// </summary>
    public sealed class IsolatedLibGit2Config : IDisposable
    {
        private const int SetSearchPathOption = 5;

        // GIT_CONFIG_LEVEL_PROGRAMDATA, SYSTEM, XDG, and GLOBAL.
        private static readonly int[] ConfigLevels = { 1, 2, 3, 4 };

        public IsolatedLibGit2Config()
        {
            this.Directory = Path.Combine(Path.GetTempPath(), "IsolatedLibGit2Config_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(this.Directory);

            LibGit2Repo.Native.Init();
            foreach (int level in ConfigLevels)
            {
                if (SetSearchPath(SetSearchPathOption, level, this.Directory) != 0)
                {
                    throw new InvalidOperationException("git_libgit2_opts failed: " + LibGit2Repo.Native.GetLastError());
                }
            }
        }

        public string Directory { get; }

        public string GlobalConfigPath => Path.Combine(this.Directory, ".gitconfig");

        public void Dispose()
        {
            foreach (int level in ConfigLevels)
            {
                SetSearchPath(SetSearchPathOption, level, null);
            }

            LibGit2Repo.Native.Shutdown();
            System.IO.Directory.Delete(this.Directory, recursive: true);
        }

        [DllImport(GVFSConstants.LibGit2LibraryName, EntryPoint = "git_libgit2_opts")]
        private static extern int SetSearchPath(int option, int level, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    }
}
