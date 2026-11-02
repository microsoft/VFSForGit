using System;
using System.CommandLine;
using System.Threading;

namespace GVFS.FunctionalTests.LockHolder
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--wait-for-event")
            {
                string eventName = Environment.GetEnvironmentVariable("GVFS_TEST_HOOK_WAIT_EVENT_NAME");
                if (string.IsNullOrEmpty(eventName))
                {
                    Console.Error.WriteLine("The test did not supply the hook event name.");
                    Environment.Exit(1);
                }

                using (EventWaitHandle ready = EventWaitHandle.OpenExisting(eventName + ".Ready"))
                using (EventWaitHandle release = EventWaitHandle.OpenExisting(eventName + ".Release"))
                {
                    ready.Set();
                    if (!release.WaitOne(TimeSpan.FromSeconds(30)))
                    {
                        Console.Error.WriteLine("Timed out before the test released the command hook.");
                        Environment.Exit(1);
                    }
                }

                return;
            }

            RootCommand rootCommand = AcquireGVFSLockVerb.BuildRootCommand();
            rootCommand.Parse(args).Invoke();
        }
    }
}
