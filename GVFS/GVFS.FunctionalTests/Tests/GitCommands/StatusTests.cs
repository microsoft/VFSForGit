using GVFS.Tests.Should;
using NUnit.Framework;
using System.IO;
using System.Threading;

namespace GVFS.FunctionalTests.Tests.GitCommands
{
    [TestFixtureSource(typeof(GitRepoTests), nameof(GitRepoTests.ValidateWorkingTree))]
    [Category(Categories.GitCommands)]
    public class StatusTests : GitRepoTests
    {
        public StatusTests(bool validateWorkingTree)
            : base(enlistmentPerTest: true, validateWorkingTree: validateWorkingTree)
        {
        }

        [TestCase]
        [Category(Categories.MacTODO.FlakyTest)]
        public void MoveFileIntoDotGitDirectory()
        {
            string srcPath = @"Readme.md";
            string dstPath = Path.Combine(".git", "destination.txt");

            this.MoveFile(srcPath, dstPath);
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void DeleteThenCreateThenDeleteFile()
        {
            string srcPath = @"Readme.md";

            this.DeleteFile(srcPath);
            this.ValidateGitCommand("status");
            this.CreateFile("Testing", srcPath);
            this.ValidateGitCommand("status");
            this.DeleteFile(srcPath);
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void CreateFileWithoutClose()
        {
            string internalParameter = $"\"{{\\\"ServiceName\\\":\\\"Test\\\"," +
                    "\\\"StartedByService\\\":false," +
                    $"\\\"MaintenanceJob\\\":\\\"PackfileMaintenance\\\"," +
                    $"\\\"PackfileMaintenanceBatchSize\\\":50000}}\"";

            for (int i = 0; i < 1000; i++)
            {
                string srcPath = @"CreateFileWithoutClose.md" + i;
                this.CreateFileWithoutClose(srcPath);
                this.ValidateGitCommand("status");
            }
        }

        [TestCase]
        public void WriteWithoutClose1()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose2()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose3()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose4()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose5()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose6()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose7()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose8()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose9()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose10()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose11()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose12()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose13()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose14()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose15()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose16()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose17()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose18()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose19()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose20()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose21()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose22()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose23()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose24()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose25()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose26()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose27()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose28()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose29()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose30()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose31()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose32()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose33()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose34()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose35()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose36()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose37()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose38()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose39()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose40()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose41()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose42()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose43()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose44()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose45()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose46()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose47()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose48()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose49()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose50()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose51()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose52()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose53()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose54()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose55()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose56()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose57()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose58()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose59()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose60()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose61()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose62()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose63()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose64()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose65()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose153()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose66()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose67()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose68()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose69()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose70()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose71()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose72()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose73()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose74()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose75()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose76()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose77()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose78()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose79()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose80()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose81()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose82()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose83()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose84()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose85()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose86()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose87()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose88()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose89()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose91()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose152()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose92()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose93()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose151()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose94()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose95()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose96()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose97()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose98()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose99()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose100()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose101()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose102()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose103()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose104()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose105()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose106()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose107()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose108()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose109()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose110()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose111()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose112()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose113()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose114()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose115()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose116()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose117()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose118()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose119()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose120()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose121()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose122()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose123()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose124()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose125()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose126()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose127()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose128()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose129()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose130()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose131()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose132()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose133()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose134()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose135()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose136()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose137()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose138()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose139()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose140()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose141()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose142()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose143()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose144()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose145()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose146()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose147()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose148()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose149()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        public void WriteWithoutClose150()
        {
            string srcPath = @"Readme.md";
            this.ReadFileAndWriteWithoutClose(srcPath, "More Stuff");
            this.ValidateGitCommand("status");
        }

        [TestCase]
        [Category(Categories.MacTODO.M4)]
        public void ModifyingAndDeletingRepositoryExcludeFileInvalidatesCache()
        {
            string repositoryExcludeFile = Path.Combine(".git", "info", "exclude");

            this.RepositoryIgnoreTestSetup();

            // Add ignore pattern to existing exclude file
            this.EditFile("*.ign", repositoryExcludeFile);

            // The exclude file has been modified, verify this status
            // excludes the "test.ign" file as expected.
            this.ValidateGitCommand("status");

            // Wait for status cache
            this.WaitForStatusCacheToBeGenerated();

            // Delete repository exclude file
            this.DeleteFile(repositoryExcludeFile);

            // The exclude file has been deleted, verify this status
            // includes the "test.ign" file as expected.
            this.ValidateGitCommand("status");
        }

        [TestCase]
        [Category(Categories.MacTODO.M4)]
        public void NewRepositoryExcludeFileInvalidatesCache()
        {
            string repositoryExcludeFileRelativePath = Path.Combine(".git", "info", "exclude");
            string repositoryExcludeFilePath = Path.Combine(this.Enlistment.EnlistmentRoot, repositoryExcludeFileRelativePath);

            this.DeleteFile(repositoryExcludeFileRelativePath);

            this.RepositoryIgnoreTestSetup();

            File.Exists(repositoryExcludeFilePath).ShouldBeFalse("Repository exclude path should not exist");

            // Create new exclude file with ignore pattern
            this.CreateFile("*.ign", repositoryExcludeFileRelativePath);

            // The exclude file has been modified, verify this status
            // excludes the "test.ign" file as expected.
            this.ValidateGitCommand("status");
        }

        [TestCase]
        [Category(Categories.MacTODO.M4)]
        public void ModifyingHeadSymbolicRefInvalidatesCache()
        {
            this.ValidateGitCommand("status");

            this.WaitForStatusCacheToBeGenerated(waitForNewFile: false);

            this.ValidateGitCommand("branch other_branch");

            this.WaitForStatusCacheToBeGenerated();
            this.ValidateGitCommand("status");

            this.ValidateGitCommand("symbolic-ref HEAD refs/heads/other_branch");
        }

        [TestCase]
        [Category(Categories.MacTODO.M4)]
        public void ModifyingHeadRefInvalidatesCache()
        {
            this.ValidateGitCommand("status");

            this.WaitForStatusCacheToBeGenerated(waitForNewFile: false);

            this.ValidateGitCommand("update-ref HEAD HEAD~1");

            this.WaitForStatusCacheToBeGenerated();
            this.ValidateGitCommand("status");
        }

        private void RepositoryIgnoreTestSetup()
        {
            this.WaitForUpToDateStatusCache();

            string statusCachePath = Path.Combine(this.Enlistment.DotGVFSRoot, "GitStatusCache", "GitStatusCache.dat");
            File.Delete(statusCachePath);

            // Create a new file with an extension that will be ignored later in the test.
            this.CreateFile("file to be ignored", "test.ign");

            this.WaitForStatusCacheToBeGenerated();

            // Verify that status from the status cache includes the "test.ign" entry
            this.ValidateGitCommand("status");
        }

        /// <summary>
        /// Wait for an up-to-date status cache file to exist on disk.
        /// </summary>
        private void WaitForUpToDateStatusCache()
        {
            // Run "git status" for the side effect that it will delete any stale status cache file.
            this.ValidateGitCommand("status");

            // Wait for a new status cache to be generated.
            this.WaitForStatusCacheToBeGenerated(waitForNewFile: false);
        }

        private void WaitForStatusCacheToBeGenerated(bool waitForNewFile = true)
        {
            string statusCachePath = Path.Combine(this.Enlistment.DotGVFSRoot, "GitStatusCache", "GitStatusCache.dat");

            if (waitForNewFile)
            {
                File.Exists(statusCachePath).ShouldEqual(false, "Status cache file should not exist at this point - it should have been deleted by previous status command.");
            }

            // Wait for the status cache file to be regenerated
            for (int i = 0; i < 10; i++)
            {
                if (File.Exists(statusCachePath))
                {
                    break;
                }

                Thread.Sleep(1000);
            }

            // The cache file should exist by now. We want the next status to come from the
            // cache and include the "test.ign" entry.
            File.Exists(statusCachePath).ShouldEqual(true, "Status cache file should be regenerated by this point.");
        }
    }
}
