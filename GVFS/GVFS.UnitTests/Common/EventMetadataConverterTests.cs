using System.Collections.Generic;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using NUnit.Framework;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class EventMetadataConverterTests
    {
        [TestCase]
        public void NestedEventMetadataSerializesAsObject()
        {
            EventMetadata inner = new EventMetadata();
            inner.Add("ProcessName1", "git.exe");
            inner.Add("ProcessCount1", 42);

            EventMetadata outer = new EventMetadata();
            outer.Add("FilePlaceholderCreation", inner);

            string json = EventMetadataConverter.SerializeToString(outer);

            json.ShouldContain("\"FilePlaceholderCreation\":{");
            json.ShouldContain("\"ProcessName1\":\"git.exe\"");
            json.ShouldContain("\"ProcessCount1\":42");
            json.ShouldNotContain(false, "GVFS.Common.Tracing.EventMetadata");
        }

        [TestCase]
        public void DictionaryStringStringSerializesAsObject()
        {
            Dictionary<string, string> diskInfo = new Dictionary<string, string>
            {
                ["DriveLetter"] = "D",
                ["VolumeDriveType"] = "Fixed",
                ["VolumeFileSystem"] = "ReFS",
            };

            EventMetadata metadata = new EventMetadata();
            metadata.Add("PhysicalDiskInfo", diskInfo);

            string json = EventMetadataConverter.SerializeToString(metadata);

            json.ShouldContain("\"PhysicalDiskInfo\":{");
            json.ShouldContain("\"DriveLetter\":\"D\"");
            json.ShouldContain("\"VolumeDriveType\":\"Fixed\"");
            json.ShouldContain("\"VolumeFileSystem\":\"ReFS\"");
            json.ShouldNotContain(false, "System.Collections.Generic.Dictionary");
        }

        [TestCase]
        public void EmptyNestedEventMetadataSerializesAsEmptyObject()
        {
            EventMetadata inner = new EventMetadata();
            EventMetadata outer = new EventMetadata();
            outer.Add("FilePlaceholderCreation", inner);

            string json = EventMetadataConverter.SerializeToString(outer);

            json.ShouldContain("\"FilePlaceholderCreation\":{}");
        }

        [TestCase]
        public void PrimitiveValuesStillSerializeCorrectly()
        {
            EventMetadata metadata = new EventMetadata();
            metadata.Add("StringVal", "hello");
            metadata.Add("IntVal", 123);
            metadata.Add("LongVal", 999999999999L);
            metadata.Add("BoolVal", true);

            string json = EventMetadataConverter.SerializeToString(metadata);

            json.ShouldContain("\"StringVal\":\"hello\"");
            json.ShouldContain("\"IntVal\":123");
            json.ShouldContain("\"LongVal\":999999999999");
            json.ShouldContain("\"BoolVal\":true");
        }
    }
}
