using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GVFS.Common
{
    public static class SHA1Util
    {
        public static bool IsValidShaFormat(string sha)
        {
            return sha != null && sha.Length == 40 && sha.All(c => Uri.IsHexDigit(c));
        }

        /// <summary>
        /// Returns a log-safe rendering of a value that was expected to be a
        /// 40-character hex SHA but is not. Non-hex characters (for example the
        /// NUL bytes of a corrupt placeholder content-id) are escaped as \uXXXX
        /// so the value stays greppable in telemetry and carries no control
        /// characters.
        /// </summary>
        public static string ToLoggableShaString(string sha)
        {
            if (sha == null)
            {
                return "(null)";
            }

            StringBuilder builder = new StringBuilder(sha.Length);
            foreach (char c in sha)
            {
                if (Uri.IsHexDigit(c))
                {
                    builder.Append(c);
                }
                else
                {
                    builder.AppendFormat("\\u{0:x4}", (int)c);
                }
            }

            return builder.ToString();
        }

        public static string SHA1HashStringForUTF8String(string s)
        {
            return HexStringFromBytes(SHA1ForUTF8String(s));
        }

        public static byte[] SHA1ForUTF8String(string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);

            using (SHA1 sha1 = SHA1.Create()) // CodeQL [SM02196] SHA-1 is acceptable here because this is Git's hashing algorithm, not used for cryptographic purposes
            {
                return sha1.ComputeHash(bytes);
            }
        }

        /// <summary>
        /// Returns a string representation of a byte array from the first
        /// <param name="numBytes"/> bytes of the buffer.
        /// </summary>
        public static string HexStringFromBytes(byte[] buf, int numBytes = -1)
        {
            unsafe
            {
                numBytes = numBytes == -1 ? buf.Length : numBytes;

                fixed (byte* unsafeBuf = buf)
                {
                    int charIndex = 0;
                    byte* currentByte = unsafeBuf;
                    char[] chars = new char[numBytes * 2];
                    for (int i = 0; i < numBytes; i++)
                    {
                        char first = (char)(((*currentByte >> 4) & 0x0F) + 0x30);
                        char second = (char)((*currentByte & 0x0F) + 0x30);
                        chars[charIndex++] = first >= 0x3A ? (char)(first + 0x27) : first;
                        chars[charIndex++] = second >= 0x3A ? (char)(second + 0x27) : second;

                        currentByte++;
                    }

                    return new string(chars);
                }
            }
        }

        public static byte[] BytesFromHexString(string sha)
        {
            byte[] arr = new byte[sha.Length / 2];

            for (int i = 0; i < arr.Length; ++i)
            {
                arr[i] = (byte)((GetHexVal(sha[i << 1]) << 4) + GetHexVal(sha[(i << 1) + 1]));
            }

            return arr;
        }

        private static int GetHexVal(char hex)
        {
            int val = (int)hex;
            return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }
    }
}
