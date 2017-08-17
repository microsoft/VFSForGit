using GVFS.Common.NetworkStreams;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.UnitTests.Prefetch
{
    [TestFixture]
    public class PrefetchPacksDeserializerTests
    {
        private static readonly byte[] PrefetchPackExpectedHeader
            = new byte[]
            {
                (byte)'G', (byte)'P', (byte)'R', (byte)'E', (byte)' ',
                1 // Version
            };

        [TestCase]
        public void PrefetchPacksDeserializer_No_Packs_Succeeds()
        {
            this.RunPrefetchPacksDeserializerTest(0, false);
        }

        [TestCase]
        public void PrefetchPacksDeserializer_Single_Pack_With_Index_Receives_Both()
        {
            this.RunPrefetchPacksDeserializerTest(1, true);
        }

        [TestCase]
        public void PrefetchPacksDeserializer_Single_Pack_Without_Index_Receives_Only_Pack()
        {
            this.RunPrefetchPacksDeserializerTest(1, false);
        }

        [TestCase]
        public void PrefetchPacksDeserializer_Multiple_Packs_With_Indexes()
        {
            this.RunPrefetchPacksDeserializerTest(10, true);
        }

        [TestCase]
        public void PrefetchPacksDeserializer_Multiple_Packs_Without_Indexes()
        {
            this.RunPrefetchPacksDeserializerTest(10, false);
        }

        /// <summary>
        /// A deterministic way to create somewhat unique packs
        /// </summary>
        private static byte[] PackForTimestamp(long timestamp)
        {
            unchecked
            {
                Random rand = new Random((int)timestamp);
                byte[] data = new byte[100];
                rand.NextBytes(data);
                return data;
            }
        }

        /// <summary>
        /// A deterministic way to create somewhat unique indexes
        /// </summary>
        private static byte[] IndexForTimestamp(long timestamp)
        {
            unchecked
            {
                Random rand = new Random((int)-timestamp);
                byte[] data = new byte[50];
                rand.NextBytes(data);
                return data;
            }
        }

        /// <summary>
        /// Implementation of the PrefetchPack spec to generate data for tests
        /// </summary>
        private void WriteToSpecs(Stream stream, long[] packTimestamps, bool withIndexes)
        {
            // Header
            stream.Write(PrefetchPackExpectedHeader, 0, PrefetchPackExpectedHeader.Length);

            // PackCount
            stream.Write(BitConverter.GetBytes((ushort)packTimestamps.Length), 0, 2);

            for (int i = 0; i < packTimestamps.Length; i++)
            {
                byte[] packContents = PackForTimestamp(packTimestamps[i]);
                byte[] indexContents = IndexForTimestamp(packTimestamps[i]);

                // Pack Header
                // Timestamp
                stream.Write(BitConverter.GetBytes(packTimestamps[i]), 0, 8);

                // Pack length
                stream.Write(BitConverter.GetBytes((long)packContents.Length), 0, 8);

                // Pack index length
                if (withIndexes)
                {
                    stream.Write(BitConverter.GetBytes((long)indexContents.Length), 0, 8);
                }
                else
                {
                    stream.Write(BitConverter.GetBytes(-1L), 0, 8);
                }

                // Pack data
                stream.Write(packContents, 0, packContents.Length);

                if (withIndexes)
                {
                    stream.Write(indexContents, 0, indexContents.Length);
                }
            }
        }

        private void RunPrefetchPacksDeserializerTest(int packCount, bool withIndexes)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                long[] packTimestamps = Enumerable.Range(0, packCount).Select(x => (long)x).ToArray();

                // Write the data to the memory stream.
                this.WriteToSpecs(ms, packTimestamps, withIndexes);
                ms.Position = 0;

                Dictionary<string, List<Tuple<string, long>>> receivedPacksAndIndexes = new Dictionary<string, List<Tuple<string, long>>>();

                foreach (PrefetchPacksDeserializer.PackAndIndex pack in new PrefetchPacksDeserializer(ms).EnumeratePacks())
                {
                    List<Tuple<string, long>> packsAndIndexesByUniqueId;
                    if (!receivedPacksAndIndexes.TryGetValue(pack.UniqueId, out packsAndIndexesByUniqueId))
                    {
                        packsAndIndexesByUniqueId = new List<Tuple<string, long>>();
                        receivedPacksAndIndexes.Add(pack.UniqueId, packsAndIndexesByUniqueId);
                    }

                    using (MemoryStream packContent = new MemoryStream())
                    using (MemoryStream idxContent = new MemoryStream())
                    {
                        pack.PackStream.CopyTo(packContent);
                        byte[] packData = packContent.ToArray();
                        packData.ShouldMatchInOrder(PackForTimestamp(pack.Timestamp));
                        packsAndIndexesByUniqueId.Add(Tuple.Create("pack", pack.Timestamp));

                        if (pack.IndexStream != null)
                        {
                            pack.IndexStream.CopyTo(idxContent);
                            byte[] idxData = idxContent.ToArray();
                            idxData.ShouldMatchInOrder(IndexForTimestamp(pack.Timestamp));
                            packsAndIndexesByUniqueId.Add(Tuple.Create("idx", pack.Timestamp));
                        }
                    }
                }

                receivedPacksAndIndexes.Count.ShouldEqual(packCount, "UniqueId count");

                foreach (List<Tuple<string, long>> groupedByUniqueId in receivedPacksAndIndexes.Values)
                {
                    if (withIndexes)
                    {
                        groupedByUniqueId.Count.ShouldEqual(2, "Both Pack and Index for UniqueId");

                        // Should only contain 1 index file
                        groupedByUniqueId.ShouldContainSingle(x => x.Item1 == "idx");
                    }

                    // should only contain 1 pack file
                    groupedByUniqueId.ShouldContainSingle(x => x.Item1 == "pack");

                    groupedByUniqueId.Select(x => x.Item2).Distinct().Count().ShouldEqual(1, "Same timestamps for a uniqueId");
                }
            }
        }
    }
}
