using GVFS.FunctionalTests.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.FunctionalTests.Tests
{
    public static class TestResultsHelper
    {
        public static void OutputGVFSLogs(GVFSFunctionalTestEnlistment enlistment)
        {
            if (enlistment == null)
            {
                return;
            }

            Console.WriteLine("GVFS logs output attached below.\n\n");

            foreach (string filename in GetAllFilesInDirectory(enlistment.GVFSLogsRoot))
            {
                OutputFileContents(filename);
            }
        }

        public static void OutputFileContents(string filename)
        {
            try
            {
                using (StreamReader reader = new StreamReader(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    Console.WriteLine("----- {0} -----", filename);
                    Console.WriteLine(reader.ReadToEnd() + "\n\n");
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine("Unable to read logfile at {0}: {1}", filename, ex.ToString());
            }
        }

        public static IEnumerable<string> GetAllFilesInDirectory(string folderName)
        {
            DirectoryInfo directory = new DirectoryInfo(folderName);
            if (!directory.Exists)
            {
                return Enumerable.Empty<string>();
            }

            return directory.GetFiles().Select(file => file.FullName);
        }
    }
}
