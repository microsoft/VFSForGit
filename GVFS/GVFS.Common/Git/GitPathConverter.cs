using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Common.Git
{
    public static class GitPathConverter
    {
        private const int CharsInOctet = 3;
        private const char OctetIndicator = '\\';

        public static string ConvertPathOctetsToUtf8(string filePath)
        {
            if (filePath == null)
            {
                return null;
            }

            int octetIndicatorIndex = filePath.IndexOf(OctetIndicator);
            if (octetIndicatorIndex == -1)
            {
                return filePath;
            }

            StringBuilder converted = new StringBuilder();
            List<byte> octets = new List<byte>();
            int index = 0;
            while (octetIndicatorIndex != -1)
            {
                converted.Append(filePath.Substring(index, octetIndicatorIndex - index));
                while (octetIndicatorIndex < filePath.Length && filePath[octetIndicatorIndex] == OctetIndicator)
                {
                    string octet = filePath.Substring(octetIndicatorIndex + 1, CharsInOctet);
                    octets.Add(Convert.ToByte(octet, 8));
                    octetIndicatorIndex += CharsInOctet + 1;
                }

                AddOctetsAsUtf8(converted, octets);
                index = octetIndicatorIndex;
                octetIndicatorIndex = filePath.IndexOf(OctetIndicator, octetIndicatorIndex);
            }

            AddOctetsAsUtf8(converted, octets);
            converted.Append(filePath.Substring(index));

            return converted.ToString();
        }

        private static void AddOctetsAsUtf8(StringBuilder converted, List<byte> octets)
        {
            if (octets.Count > 0)
            {
                converted.Append(Encoding.UTF8.GetChars(octets.ToArray()));
                octets.Clear();
            }
        }
    }
}
