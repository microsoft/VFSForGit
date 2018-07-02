using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Common.Prefetch.Git
{
    public class GitPackIndex
    {
        private const uint PackIndexSignature = 0xff744f63;
        private const int Sha1ByteLength = 20;

        public static IEnumerable<string> GetShas(string filePath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            using (BigEndianReader binReader = new BigEndianReader(stream))
            {
                VerifyHeader(binReader);

                // Fanout table has 256 4-byte buckets corresponding to the number of objects prefixed by the bucket number
                // Number is cumulative, so the total is always the last bucket value.
                stream.Position += 255 * sizeof(uint);
                uint totalObjects = binReader.ReadUInt32();
                for (int i = 0; i < totalObjects; ++i)
                {
                    yield return BitConverter.ToString(binReader.ReadBytes(Sha1ByteLength)).Replace("-", string.Empty);
                }
            }
        }

        private static void VerifyHeader(BinaryReader binReader)
        {
            uint signature = binReader.ReadUInt32();
            if (signature != PackIndexSignature)
            {
                throw new InvalidDataException("Bad pack header");
            }

            uint version = binReader.ReadUInt32();
            if (version != 2)
            {
                throw new InvalidDataException("Unsupported pack index version");
            }
        }
    }
}