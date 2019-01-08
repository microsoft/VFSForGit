using GVFS.Common;
using GVFS.Tests.Should;
using Newtonsoft.Json;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class JsonReleaseManifestTests
    {
        private static int manifestEntryCount = 0;

        [TestCase]
        public void CanReadExpectedJsonString()
        {
            string releaseManifestJsonString =
@"
{
  ""Version"" : ""1"",
  ""PlatformInstallManifests"" : {
    ""Windows"": {
      ""InstallActions"": [
        {
 	    ""Name"" : ""Git"",
        ""Version"" : ""2.19.0.1.34"",
        ""RelativePath"" : ""Installers\\Windows\\G4W\\Git-2.19.0.gvfs.1.34.gc7fb556-64-bit.exe"",
        ""Args"" : ""/VERYSILENT /CLOSEAPPLICATIONS""
      },
      {
 	    ""Name"" : ""PreGitInstaller"",
 	    ""Version"" : ""0.0.0.1"",
 	    ""RelativePath"" : ""Installers\\Windows\\GSD\\PreGitInstallerSetup.exe""
      },
      ]
    }
  }
}
";
            ReleaseManifest releaseManifest = ReleaseManifest.FromJsonString(releaseManifestJsonString);

            releaseManifest.ShouldNotBeNull();
            InstallManifestPlatform platformInstallManifest = releaseManifest.PlatformInstallManifests[ReleaseManifest.WindowsPlatformKey];
            platformInstallManifest.ShouldNotBeNull();
            platformInstallManifest.InstallActions.Count.ShouldEqual(2);
            platformInstallManifest.InstallActions[0].Name.ShouldEqual("Git");
            platformInstallManifest.InstallActions[1].Name.ShouldEqual("PreGitInstaller");
        }

        [TestCase]
        public void CanDeserializeAndSerializeReleaseManifest()
        {
            List<ManifestEntry> entries = new List<ManifestEntry>()
            {
                this.CreateManifestEntry(),
                this.CreateManifestEntry()
            };

            ReleaseManifest releaseManifest = new ReleaseManifest();
            releaseManifest.AddPlatformInstallManifest(ReleaseManifest.WindowsPlatformKey, entries);

            JsonSerializer serializer = new JsonSerializer();

            using (MemoryStream ms = new MemoryStream())
            using (StreamWriter streamWriter = new StreamWriter(ms))
            using (JsonWriter jsWriter = new JsonTextWriter(streamWriter))
            {
                string output = JsonConvert.SerializeObject(releaseManifest);
                serializer.Serialize(jsWriter, releaseManifest);
                jsWriter.Flush();

                ms.Seek(0, SeekOrigin.Begin);

                StreamReader streamReader = new StreamReader(ms);
                ReleaseManifest deserializedReleaseManifest = ReleaseManifest.FromJson(streamReader);

                this.VerifyReleaseManifestsAreEqual(releaseManifest, deserializedReleaseManifest);
            }
        }

        private ManifestEntry CreateManifestEntry()
        {
            int entrySuffix = manifestEntryCount++;
            return new ManifestEntry(
                $"Installer{entrySuffix}",
                $"1.{entrySuffix}.1.2",
                $"/nodowngrade{entrySuffix}",
                $"installers/installer1{entrySuffix}");
        }

        private void VerifyReleaseManifestsAreEqual(ReleaseManifest expected, ReleaseManifest actual)
        {
            actual.PlatformInstallManifests.Count.ShouldEqual(expected.PlatformInstallManifests.Count, $"The number of platforms ({actual.PlatformInstallManifests.Count}) do not match the expected number of platforms ({expected.PlatformInstallManifests.Count}).");

            foreach (KeyValuePair<string, InstallManifestPlatform> kvp in expected.PlatformInstallManifests)
            {
                this.VerifyPlatformManifestsAreEqual(kvp.Value, actual.PlatformInstallManifests[kvp.Key]);
            }
        }

        private void VerifyPlatformManifestsAreEqual(InstallManifestPlatform expected, InstallManifestPlatform actual)
        {
            actual.InstallActions.Count.ShouldEqual(expected.InstallActions.Count, $"The number of platforms ({actual.InstallActions.Count}) do not match the expected number of platforms ({expected.InstallActions.Count}).");

            for (int i = 0; i < actual.InstallActions.Count; i++)
            {
                actual.InstallActions[i].Version.ShouldEqual(expected.InstallActions[i].Version);
                actual.InstallActions[i].Args.ShouldEqual(expected.InstallActions[i].Args);
                actual.InstallActions[i].Name.ShouldEqual(expected.InstallActions[i].Name);
            }
        }
    }
}
