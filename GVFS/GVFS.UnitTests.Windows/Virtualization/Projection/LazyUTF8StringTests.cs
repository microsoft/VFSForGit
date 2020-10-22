using GVFS.Tests.Should;
using GVFS.UnitTests.Mock.Common;
using NUnit.Framework;
using System.Text;
using static GVFS.Virtualization.Projection.GitIndexProjection;

namespace GVFS.UnitTests.Virtualization.Git
{
    [TestFixture]
    public class LazyUTF8StringTests
    {
        private const int DefaultIndexEntryCount = 10;

        private unsafe delegate void RunUsingPointer(byte* buffer);

        [OneTimeSetUp]
        public void Setup()
        {
            LazyUTF8String.InitializePools(new MockTracer(), DefaultIndexEntryCount);
        }

        [SetUp]
        public void TestSetup()
        {
            LazyUTF8String.ResetPool(new MockTracer(), DefaultIndexEntryCount);
        }

        [TestCase]
        public unsafe void GetString()
        {
            UseASCIIBytePointer(
                "folderonefile.txt",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    firstFolder.GetString().ShouldEqual("folder");
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 6, 3);
                    secondFolder.GetString().ShouldEqual("one");
                    LazyUTF8String file = LazyUTF8String.FromByteArray(bufferPtr + 9, 8);
                    file.GetString().ShouldEqual("file.txt");
                });
        }

        [TestCase]
        public unsafe void GetString_NonASCII()
        {
            UseUTF8BytePointer(
                "folderoneريلٌأكتوبرfile.txt",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    firstFolder.GetString().ShouldEqual("folder");
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 6, 3);
                    secondFolder.GetString().ShouldEqual("one");
                    LazyUTF8String utf8 = LazyUTF8String.FromByteArray(bufferPtr + 9, 20);
                    utf8.GetString().ShouldEqual("ريلٌأكتوبر");
                    LazyUTF8String file = LazyUTF8String.FromByteArray(bufferPtr + 29, 8);
                    file.GetString().ShouldEqual("file.txt");
                });
        }

        [TestCase]
        public unsafe void CaseInsensitiveEquals_SameName_EqualsTrue()
        {
            UseASCIIBytePointer(
                "folderonefile.txtfolder",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 6);
                    firstFolder.CaseInsensitiveEquals(secondFolder).ShouldBeTrue(nameof(firstFolder.CaseInsensitiveEquals));
                });
        }

        [TestCase]
        public unsafe void CaseSensitiveEquals_SameName_EqualsTrue()
        {
            UseASCIIBytePointer(
                "folderonefile.txtfolder",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 6);
                    firstFolder.CaseSensitiveEquals(secondFolder).ShouldBeTrue(nameof(firstFolder.CaseSensitiveEquals));
                });
        }

        [TestCase]
        public unsafe void CaseInsensitiveEquals_SameNameDifferentCase1_EqualsTrue()
        {
            UseASCIIBytePointer(
                "folderonefile.txtFolder",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 6);
                    firstFolder.CaseInsensitiveEquals(secondFolder).ShouldBeTrue(nameof(firstFolder.CaseInsensitiveEquals));
                });
        }

        [TestCase]
        public unsafe void CaseSensitiveEquals_SameNameDifferentCase1_EqualsFalse()
        {
            UseASCIIBytePointer(
                "folderonefile.txtFolder",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 6);
                    firstFolder.CaseSensitiveEquals(secondFolder).ShouldBeFalse(nameof(firstFolder.CaseSensitiveEquals));
                });
        }

        [TestCase]
        public unsafe void CaseInsensitiveEquals_SameNameDifferentCase2_EqualsTrue()
        {
            UseASCIIBytePointer(
                "FOlderonefile.txtFolder",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 6);
                    firstFolder.CaseInsensitiveEquals(secondFolder).ShouldBeTrue(nameof(firstFolder.CaseInsensitiveEquals));
                });
        }

        [TestCase]
        public unsafe void CaseSensitiveEquals_SameNameDifferentCase2_EqualsFalse()
        {
            UseASCIIBytePointer(
                "FOlderonefile.txtFolder",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 6);
                    firstFolder.CaseSensitiveEquals(secondFolder).ShouldBeFalse(nameof(firstFolder.CaseSensitiveEquals));
                });
        }

        [TestCase]
        public unsafe void CaseInsensitiveEquals_OneNameLongerEqualsFalse()
        {
            UseASCIIBytePointer(
                "folderonefile.txtFolderTest",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 10);
                    firstFolder.CaseInsensitiveEquals(secondFolder).ShouldBeFalse(nameof(firstFolder.CaseInsensitiveEquals));
                });
        }

        [TestCase]
        public unsafe void CaseSensitiveEquals_OneNameLongerEqualsFalse()
        {
            UseASCIIBytePointer(
                "folderonefile.txtfoldertest",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 10);
                    firstFolder.CaseSensitiveEquals(secondFolder).ShouldBeFalse(nameof(firstFolder.CaseSensitiveEquals));
                });
        }

        [TestCase]
        public unsafe void CaseInsensitiveEquals_OneNameShorterEqualsFalse()
        {
            UseASCIIBytePointer(
                "folderonefile.txtFold",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 4);
                    firstFolder.CaseInsensitiveEquals(secondFolder).ShouldBeFalse(nameof(firstFolder.CaseInsensitiveEquals));
                });
        }

        [TestCase]
        public unsafe void CaseSensitiveEquals_OneNameShorterEqualsFalse()
        {
            UseASCIIBytePointer(
                "folderonefile.txtfold",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 4);
                    firstFolder.CaseSensitiveEquals(secondFolder).ShouldBeFalse(nameof(firstFolder.CaseSensitiveEquals));
                });
        }

        [TestCase]
        public unsafe void Compare_EqualsZero()
        {
            UseASCIIBytePointer(
                "folderonefile.txtfolder",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 6);
                    firstFolder.CaseInsensitiveCompare(secondFolder).ShouldEqual(0, nameof(firstFolder.CaseInsensitiveCompare));
                    firstFolder.CaseSensitiveCompare(secondFolder).ShouldEqual(0, nameof(firstFolder.CaseSensitiveCompare));
                });
        }

        [TestCase]
        public unsafe void Compare_EqualsLessThanZero()
        {
            UseASCIIBytePointer(
                "folderonefile.txtfolders",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 7);
                    firstFolder.CaseInsensitiveCompare(secondFolder).ShouldBeAtMost(-1, nameof(firstFolder.CaseInsensitiveCompare));
                    firstFolder.CaseSensitiveCompare(secondFolder).ShouldBeAtMost(-1, nameof(firstFolder.CaseSensitiveCompare));
                });
        }

        [TestCase]
        public unsafe void Compare_EqualsLessThanZero2()
        {
            UseASCIIBytePointer(
                "folderDKfile.txtSDKfolders",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 6, 2);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 16, 3);
                    firstFolder.CaseInsensitiveCompare(secondFolder).ShouldBeAtMost(-1, nameof(firstFolder.CaseInsensitiveCompare));
                    firstFolder.CaseSensitiveCompare(secondFolder).ShouldBeAtMost(-1, nameof(firstFolder.CaseSensitiveCompare));
                });
        }

        [TestCase]
        public unsafe void Compare_EqualsGreaterThanZero()
        {
            UseASCIIBytePointer(
                "folderonefile.txtfold",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 0, 6);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 4);
                    firstFolder.CaseInsensitiveCompare(secondFolder).ShouldBeAtLeast(1, nameof(firstFolder.CaseInsensitiveCompare));
                    firstFolder.CaseSensitiveCompare(secondFolder).ShouldBeAtLeast(1, nameof(firstFolder.CaseSensitiveCompare));
                });
        }

        [TestCase]
        public unsafe void Compare_EqualsGreaterThanZero2()
        {
            UseASCIIBytePointer(
                "folderSDKfile.txtDKfolders",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 6, 3);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 2);
                    firstFolder.CaseInsensitiveCompare(secondFolder).ShouldBeAtLeast(1, nameof(firstFolder.CaseInsensitiveCompare));
                    firstFolder.CaseSensitiveCompare(secondFolder).ShouldBeAtLeast(1, nameof(firstFolder.CaseSensitiveCompare));
                });
        }

        [TestCase]
        public unsafe void PoolSizeCheck()
        {
            UseASCIIBytePointer(
                "folderSDKfile.txtDKfolders",
                bufferPtr =>
                {
                    int bytePoolSizeBeforeFreePool = LazyUTF8String.BytePoolSize();
                    int stringPoolSizeBeforeFreePool = LazyUTF8String.StringPoolSize();
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 6, 3);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 2);
                    CheckPoolSizes(bytePoolSizeBeforeFreePool, stringPoolSizeBeforeFreePool);
                });
        }

        [TestCase]
        public unsafe void FreePool_KeepsPoolSize()
        {
            UseASCIIBytePointer(
                "folderSDKfile.txtDKfolders",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 6, 3);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 2);
                    int bytePoolSizeBeforeFreePool = LazyUTF8String.BytePoolSize();
                    int stringPoolSizeBeforeFreePool = LazyUTF8String.StringPoolSize();
                    LazyUTF8String.FreePool();
                    CheckPoolSizes(bytePoolSizeBeforeFreePool, stringPoolSizeBeforeFreePool);
                });
        }

        [TestCase]
        public unsafe void ShrinkPool_DecreasesPoolSize()
        {
            LazyUTF8String.ResetPool(new MockTracer(), DefaultIndexEntryCount);
            string fileAndFolderNames = "folderSDKfile.txtDKfolders";
            UseASCIIBytePointer(
                fileAndFolderNames,
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 6, 3);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 2);
                    LazyUTF8String.ShrinkPool();
                    CheckPoolSizes(expectedBytePoolSize: 6, expectedStringPoolSize: 2);
                });
        }

        [TestCase]
        public unsafe void ExpandAfterShrinkPool_AllocatesDefault()
        {
            LazyUTF8String.ResetPool(new MockTracer(), DefaultIndexEntryCount);
            string fileAndFolderNames = "folderSDKfile.txtDKfolders";
            UseASCIIBytePointer(
                fileAndFolderNames,
                bufferPtr =>
                {
                    int initialBytePoolSize = LazyUTF8String.BytePoolSize();
                    int initialStringPoolSize = LazyUTF8String.StringPoolSize();

                    LazyUTF8String.FromByteArray(bufferPtr + 6, 3);
                    LazyUTF8String.FromByteArray(bufferPtr + 17, 2);
                    LazyUTF8String.ShrinkPool();
                    CheckPoolSizes(expectedBytePoolSize: 6, expectedStringPoolSize: 2);
                    LazyUTF8String.FreePool();
                    CheckPoolSizes(expectedBytePoolSize: 6, expectedStringPoolSize: 2);
                    LazyUTF8String.ShrinkPool();
                    CheckPoolSizes(expectedBytePoolSize: 0, expectedStringPoolSize: 0);
                    LazyUTF8String.FromByteArray(bufferPtr + 6, 3);
                    CheckPoolSizes(expectedBytePoolSize: initialBytePoolSize, expectedStringPoolSize: initialStringPoolSize);
                    LazyUTF8String.ShrinkPool();
                    CheckPoolSizes(expectedBytePoolSize: 3, expectedStringPoolSize: 1);
                });
        }

        [TestCase]
        public unsafe void PoolSizeIncreasesAfterShrinking()
        {
            LazyUTF8String.ResetPool(new MockTracer(), DefaultIndexEntryCount);
            string fileAndFolderNames = "folderSDKfile.txtDKfolders";
            UseASCIIBytePointer(
                fileAndFolderNames,
                bufferPtr =>
                {
                    int initialBytePoolSize = LazyUTF8String.BytePoolSize();
                    int initialStringPoolSize = LazyUTF8String.StringPoolSize();
                    LazyUTF8String.FromByteArray(bufferPtr + 6, 3);
                    LazyUTF8String.FromByteArray(bufferPtr + 17, 2);
                    LazyUTF8String.ShrinkPool();
                    CheckPoolSizes(expectedBytePoolSize: 6, expectedStringPoolSize: 2);
                    LazyUTF8String.FromByteArray(bufferPtr + 6, 3);
                    LazyUTF8String.FromByteArray(bufferPtr + 17, 2);
                    LazyUTF8String.FromByteArray(bufferPtr, 6);

                    CheckPoolSizes(expectedBytePoolSize: initialBytePoolSize, expectedStringPoolSize: initialStringPoolSize);
                });
        }

        [TestCase]
        public unsafe void NonASCIICharacters_Compare()
        {
            UseUTF8BytePointer(
                "folderSDKfile.txtريلٌأكتوبرDKfolders",
                bufferPtr =>
                {
                    LazyUTF8String firstFolder = LazyUTF8String.FromByteArray(bufferPtr + 6, 3);
                    LazyUTF8String secondFolder = LazyUTF8String.FromByteArray(bufferPtr + 17, 20);
                    firstFolder.CaseInsensitiveCompare(secondFolder).ShouldBeAtMost(-1, nameof(firstFolder.CaseInsensitiveCompare));
                    firstFolder.CaseSensitiveCompare(secondFolder).ShouldBeAtMost(-1, nameof(firstFolder.CaseSensitiveCompare));
                });
        }

        [TestCase]
        public void MinimumPoolSize()
        {
            LazyUTF8String.ResetPool(new MockTracer(), 0);

            LazyUTF8String.FreePool();
            LazyUTF8String.BytePoolSize().ShouldBeAtLeast(1);

            LazyUTF8String.InitializePools(new MockTracer(), 0);
            LazyUTF8String.BytePoolSize().ShouldBeAtLeast(1);
        }

        private static void CheckPoolSizes(int expectedBytePoolSize, int expectedStringPoolSize)
        {
            LazyUTF8String.BytePoolSize().ShouldEqual(expectedBytePoolSize, $"{nameof(LazyUTF8String.BytePoolSize)} should be {expectedBytePoolSize}");
            LazyUTF8String.StringPoolSize().ShouldEqual(expectedStringPoolSize, $"{nameof(LazyUTF8String.StringPoolSize)} should be {expectedStringPoolSize}");
        }

        private static unsafe void UseUTF8BytePointer(string fileAndFolderNames, RunUsingPointer action)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(fileAndFolderNames);
            fixed (byte* bufferPtr = buffer)
            {
                action(bufferPtr);
            }
        }

        private static unsafe void UseASCIIBytePointer(string fileAndFolderNames, RunUsingPointer action)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(fileAndFolderNames);
            fixed (byte* bufferPtr = buffer)
            {
                action(bufferPtr);
            }
        }
    }
}
