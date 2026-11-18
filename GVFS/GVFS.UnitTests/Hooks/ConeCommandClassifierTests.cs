using GVFS.Hooks;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Linq;

namespace GVFS.UnitTests.Hooks
{
    [TestFixture]
    public class ConeCommandClassifierTests
    {
        [TestCase("add -- a/b.txt")]
        [TestCase("add .")]
        [TestCase("add -A")]
        [TestCase("add -u")]
        [TestCase("stage -u")]
        [TestCase("am patch.mbox")]
        [TestCase("apply --index fix.patch")]
        [TestCase("apply --cached fix.patch")]
        [TestCase("apply -3 fix.patch")]
        [TestCase("apply -v3 fix.patch")]
        [TestCase("apply --3way fix.patch")]
        [TestCase("apply --check --apply --index fix.patch")]
        [TestCase("checkout -- a/b.txt")]
        [TestCase("checkout topic")]
        [TestCase("cherry-pick topic")]
        [TestCase("commit -a -m message")]
        [TestCase("commit -am message")]
        [TestCase("commit --all -m message")]
        [TestCase("commit -m message -- a/b.txt")]
        [TestCase("commit -m message a/b.txt")]
        [TestCase("commit -i -m message a/b.txt")]
        [TestCase("commit --only -m message a/b.txt")]
        [TestCase("commit -p")]
        [TestCase("commit --interactive")]
        [TestCase("commit --include -m message a/b.txt")]
        [TestCase("commit --patch")]
        [TestCase("commit -o -m message a/b.txt")]
        [TestCase("commit --pathspec-from-file=paths.txt -m message")]
        [TestCase("commit -qam message")]
        [TestCase("commit --message message a/b.txt")]
        [TestCase("merge topic")]
        [TestCase("mv a/b.txt a/c.txt")]
        [TestCase("pull")]
        [TestCase("rebase topic")]
        [TestCase("reset --hard")]
        [TestCase("reset --keep HEAD~1")]
        [TestCase("reset --merge")]
        [TestCase("restore a/b.txt")]
        [TestCase("revert HEAD")]
        [TestCase("rm a/b.txt")]
        [TestCase("stash")]
        [TestCase("stash push -- a/b.txt")]
        [TestCase("stash pop")]
        [TestCase("stash -- list")]
        [TestCase("stash -m list")]
        [TestCase("stash -q apply")]
        [TestCase("stash create")]
        [TestCase("switch topic")]
        public void CommandsThatWriteTheIndexFromTheWorkingTree(string commandLine)
        {
            Classify(commandLine).ShouldEqual(ConeCommandKind.WritesIndex);
        }

        [TestCase("status")]
        [TestCase("diff -- a/b.txt")]
        [TestCase("log -- a/b.txt")]
        [TestCase("apply fix.patch")]
        [TestCase("apply --check fix.patch")]
        [TestCase("commit -m message")]
        [TestCase("commit -m -a")]
        [TestCase("commit --message=-a")]
        [TestCase("commit -F message.txt")]
        [TestCase("commit -qm message")]
        [TestCase("commit -Sdeadbeef -m message")]
        [TestCase("commit -uno -m message")]
        [TestCase("commit --author author -m message")]
        [TestCase("apply --check --index fix.patch")]
        [TestCase("apply --stat --cached fix.patch")]
        [TestCase("apply -p3 fix.patch")]
        [TestCase("commit --amend --no-edit")]
        [TestCase("reset --soft HEAD~1")]
        [TestCase("stash list")]
        [TestCase("stash show -p")]
        [TestCase("stash drop")]
        [TestCase("stash clear")]
        [TestCase("stash store abc123")]
        [TestCase("update-index --refresh")]
        [TestCase("read-tree HEAD")]
        [TestCase("sparse-checkout list")]
        public void OtherCommands(string commandLine)
        {
            Classify(commandLine).ShouldEqual(ConeCommandKind.Other);
        }

        [TestCase("restore --staged a/b.txt")]
        [TestCase("checkout HEAD -- a/b.txt")]
        [TestCase("reset -- a/b.txt")]
        public void UnstageCommandsKeepTheirOwnRules(string commandLine)
        {
            Classify(commandLine).ShouldEqual(ConeCommandKind.Unstage);
        }

        [TestCase]
        public void ResetWithoutAManagedConeIsNotAnUnstageCommand()
        {
            // A mixed reset needs the unstage preparation only for a sparse-index cone.
            Classify("reset -- a/b.txt", isConeManaged: false).ShouldEqual(ConeCommandKind.Other);
        }

        private static ConeCommandKind Classify(string commandLine, bool isConeManaged = true)
        {
            string[] userArgs = commandLine.Split(' ');
            string[] args = new[] { "pre-command" }.Concat(userArgs).Concat(new[] { "--git-pid=1234" }).ToArray();
            return ConeCommandClassifier.Classify(userArgs[0], args, () => isConeManaged);
        }
    }
}
