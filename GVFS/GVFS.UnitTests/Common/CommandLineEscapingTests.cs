using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System.Runtime.InteropServices;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class CommandLineEscapingTests
    {
        [TestCase("simple", "simple", Description = "No special characters: no quoting")]
        [TestCase("with space", "\"with space\"")]
        [TestCase("with\ttab", "\"with\ttab\"")]
        [TestCase("with\"quote", "\"with\\\"quote\"")]
        [TestCase("ends_with_backslash\\", "ends_with_backslash\\")]
        [TestCase("path\\with\\backslashes", "path\\with\\backslashes")]
        [TestCase("path with\\backslashes", "\"path with\\backslashes\"")]
        [TestCase("trailing_backslash_with quote\\", "\"trailing_backslash_with quote\\\\\"")]
        [TestCase("a\\\\b c", "\"a\\\\b c\"", Description = "Internal double-backslash not before quote: kept verbatim")]
        [TestCase("a\\\\\"b", "\"a\\\\\\\\\\\"b\"", Description = "Two backslashes before quote: doubled to four plus escaped quote")]
        [TestCase("", "\"\"", Description = "Empty argument: must be quoted so it isn't lost")]
        public void EscapeArgument_ProducesExpectedString(string input, string expected)
        {
            CommandLineEscaping.EscapeArgument(input).ShouldEqual(expected);
        }

        [TestCase]
        public void EscapeArgument_NullInputIsEmptyQuotedString()
        {
            CommandLineEscaping.EscapeArgument(null).ShouldEqual("\"\"");
        }

        [TestCase("simple")]
        [TestCase("with space")]
        [TestCase("with\"quote")]
        [TestCase("ends_with_backslash\\")]
        [TestCase("a\\\\\"b")]
        [TestCase("path\\with\\backslashes")]
        [TestCase("path with\\backslashes")]
        [TestCase("trailing_backslash_with quote\\")]
        [TestCase("{\"ServiceName\":null,\"StartedByService\":true,\"MaintenanceJob\":null,\"PackfileMaintenanceBatchSize\":null}",
            Description = "The exact InternalVerbParameters.ToJson() shape that triggered the automount regression")]
        [TestCase("")]
        public void EscapeArgument_RoundTripsThroughCommandLineToArgvW(string input)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("CommandLineToArgvW is a Windows-only API.");
            }

            // Build a command line that begins with a fake program name so
            // CommandLineToArgvW has a leading token to chew on, then the
            // escaped argument we care about.
            string commandLine = "stub.exe " + CommandLineEscaping.EscapeArgument(input);

            string[] argv = CommandLineToArgs(commandLine);

            argv.Length.ShouldEqual(2, "Expected exactly one argument after the program name");
            argv[1].ShouldEqual(input ?? string.Empty);
        }

        private static string[] CommandLineToArgs(string commandLine)
        {
            int argc;
            System.IntPtr argv = NativeMethods.CommandLineToArgvW(commandLine, out argc);
            if (argv == System.IntPtr.Zero)
            {
                throw new System.ComponentModel.Win32Exception();
            }

            try
            {
                string[] args = new string[argc];
                for (int i = 0; i < argc; i++)
                {
                    System.IntPtr p = Marshal.ReadIntPtr(argv, i * System.IntPtr.Size);
                    args[i] = Marshal.PtrToStringUni(p);
                }

                return args;
            }
            finally
            {
                NativeMethods.LocalFree(argv);
            }
        }

        private static class NativeMethods
        {
            [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern System.IntPtr CommandLineToArgvW(
                [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine,
                out int pNumArgs);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern System.IntPtr LocalFree(System.IntPtr hMem);
        }
    }
}
