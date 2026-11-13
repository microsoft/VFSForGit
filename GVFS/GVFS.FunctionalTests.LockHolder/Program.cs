using System;
using System.CommandLine;
using System.Threading;
using System.IO;

namespace GVFS.FunctionalTests.LockHolder
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--read-placeholder")
            {
                string path = Environment.GetEnvironmentVariable("GVFS_TEST_PLACEHOLDER_PATH");
                string eventName = Environment.GetEnvironmentVariable("GVFS_TEST_PLACEHOLDER_EVENT_NAME");
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(eventName))
                {
                    Console.Error.WriteLine("The test did not supply the placeholder path or result event.");
                    Environment.Exit(1);
                }

                try
                {
                    File.ReadAllText(path);
                    Console.Error.WriteLine("The child unexpectedly opened the placeholder.");
                    Environment.Exit(1);
                }
                catch (FileNotFoundException)
                {
                    using (EventWaitHandle rejected = EventWaitHandle.OpenExisting(eventName))
                    {
                        rejected.Set();
                    }
                }
                catch (IOException e)
                {
                    Console.Error.WriteLine("Child placeholder rejection: 0x" + e.HResult.ToString("X8"));
                    Environment.Exit(1);
                }

                return;
            }

            if (args.Length > 0 && args[0] == "--echo-arguments")
            {
                Console.Write(string.Join("\0", args, 1, args.Length - 1));
                return;
            }

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
