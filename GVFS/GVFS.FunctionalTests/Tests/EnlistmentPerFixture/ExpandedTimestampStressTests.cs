using GVFS.FunctionalTests.Tests.EnlistmentPerFixture;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.LongRunningEnlistment
{
    /// <summary>
    /// Diagnostic: determines which FileInfo property setters trigger ProjFS hydration
    /// and reports ProjFS driver versions. Each test uses a file in a SEPARATE
    /// directory subtree within the ForTests repo.
    /// </summary>
    [TestFixture]
    public class HydrationTriggerDiagnostic : TestsWithEnlistmentPerFixture
    {
        private const int FileAttributeRecallOnDataAccess = 0x00400000;

        // Files in separate directory subtrees within the ForTests repo
        private static readonly string CreationTimePath = Path.Combine("GVFS", "FastFetch", "Jobs", "Data", "BlobDownloadRequest.cs");
        private static readonly string LastAccessTimePath = Path.Combine("GVFS", "GVFS.GVFlt", "BlobSize", "BlobSizes.cs");
        private static readonly string LastWriteTimePath = Path.Combine("GVFS", "GVFS.GVFlt", "DotGit", "AlwaysExcludeFile.cs");
        private static readonly string AttributesPath = Path.Combine("GVFS", "GVFS.Hooks", "KnownGitCommands.cs");

        [TestCase]
        public void A_ReportProjFSVersion()
        {
            string[] paths = new[]
            {
                @"C:\Windows\System32\drivers\prjflt.sys",
                @"C:\Windows\System32\ProjectedFSLib.dll",
                @"C:\Program Files\VFS for Git\ProjFS\ProjectedFSLib.dll",
                @"C:\Program Files\VFS for Git\Filter\PrjFlt.sys",
            };

            string report = "ProjFS versions: ";
            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    report += $"{Path.GetFileName(path)}={vi.FileVersion}; ";
                }
            }

            report += $"OS={Environment.OSVersion}";

            // Use Assert.Pass to ensure the message appears in test results
            Assert.Pass(report);
        }

        [TestCase]
        public void B_CreationTime()
        {
            this.TestProperty(CreationTimePath, "CreationTime", fi => fi.CreationTime = DateTime.Now.AddDays(1));
        }

        [TestCase]
        public void C_LastAccessTime()
        {
            this.TestProperty(LastAccessTimePath, "LastAccessTime", fi => fi.LastAccessTime = DateTime.Now.AddDays(1));
        }

        [TestCase]
        public void D_LastWriteTime()
        {
            this.TestProperty(LastWriteTimePath, "LastWriteTime", fi => fi.LastWriteTime = DateTime.Now.AddDays(1));
        }

        [TestCase]
        public void E_Attributes()
        {
            this.TestProperty(AttributesPath, "Attributes", fi => fi.Attributes = FileAttributes.Hidden);
        }

        private void TestProperty(string relativePath, string propertyName, Action<FileInfo> setter)
        {
            string virtualFile = this.Enlistment.GetVirtualPathTo(relativePath);

            // Enumerate parent directory to ensure placeholder is created
            string dir = Path.GetDirectoryName(virtualFile);
            Assert.IsTrue(Directory.Exists(dir), $"Directory does not exist: {dir}");
            Directory.GetFiles(dir); // force enumeration

            Assert.IsTrue(File.Exists(virtualFile), $"File does not exist: {virtualFile}");

            FileInfo before = new FileInfo(virtualFile);
            int beforeAttrs = (int)before.Attributes;
            bool beforeIsPlaceholder = (beforeAttrs & FileAttributeRecallOnDataAccess) != 0;

            Assert.IsTrue(beforeIsPlaceholder,
                $"[{propertyName}] File is NOT a placeholder before set (attrs=0x{beforeAttrs:X}). Cannot run diagnostic.");

            // Set the single property
            setter(before);

            // Check immediately
            int immAttrs = (int)new FileInfo(virtualFile).Attributes;
            bool immPlaceholder = (immAttrs & FileAttributeRecallOnDataAccess) != 0;

            // Wait 10 seconds
            Thread.Sleep(10000);
            int finalAttrs = (int)new FileInfo(virtualFile).Attributes;
            bool finalPlaceholder = (finalAttrs & FileAttributeRecallOnDataAccess) != 0;

            string result = finalPlaceholder ? "does NOT trigger hydration" : "TRIGGERS hydration";
            string detail = $"[{propertyName}] {result}. "
                + $"before=0x{beforeAttrs:X}(placeholder={beforeIsPlaceholder}) "
                + $"immediate=0x{immAttrs:X}(placeholder={immPlaceholder}) "
                + $"after10s=0x{finalAttrs:X}(placeholder={finalPlaceholder})";

            Assert.Pass(detail);
        }
    }
}
