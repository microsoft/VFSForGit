using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Http;
using GVFS.Common.Tracing;
using System.Diagnostics;
using System.IO;

namespace GVFS.UnitTests.Mock.Common
{
    public class MockPhysicalGitObjects : GitObjects
    {
        public MockPhysicalGitObjects(ITracer tracer, PhysicalFileSystem fileSystem, Enlistment enlistment, GitObjectsHttpRequestor objectRequestor)
            : base(tracer, enlistment, objectRequestor, fileSystem)
        {
        }
        
        public override string WriteLooseObject(Stream responseStream, string sha, byte[] sharedBuf = null)
        {
            using (StreamReader reader = new StreamReader(responseStream))
            {
                // Return "file contents" as "file name". Weird, but proves we got the right thing.
                return reader.ReadToEnd();
            }
        }

        public override string WriteTempPackFile(GitEndPointResponseData response)
        {
            Debug.Assert(response.Stream != null, "WriteTempPackFile should not receive a null stream");

            using (response.Stream)
            using (StreamReader reader = new StreamReader(response.Stream))
            {
                // Return "file contents" as "file name". Weird, but proves we got the right thing.
                return reader.ReadToEnd();
            }
        }

        public override GitProcess.Result IndexTempPackFile(string tempPackPath)
        {
            return new GitProcess.Result(string.Empty, "TestFailure", GitProcess.Result.GenericFailureCode);
        }
    }
}
