using System;
using System.Runtime.InteropServices;

namespace GVFS.Common.Git
{
    [StructLayout(LayoutKind.Explicit, Size = ShaBufferLength, Pack = 1)]
    public struct Sha1Id
    {
        public static readonly Sha1Id None = new Sha1Id();

        private const int ShaBufferLength = (2 * sizeof(ulong)) + sizeof(uint);
        private const int ShaStringLength = 2 * ShaBufferLength;

        [FieldOffset(0)]
        private ulong shaBytes1Through8;

        [FieldOffset(8)]
        private ulong shaBytes9Through16;

        [FieldOffset(16)]
        private uint shaBytes17Through20;

        public Sha1Id(ulong shaBytes1Through8, ulong shaBytes9Through16, uint shaBytes17Through20)
        {
            this.shaBytes1Through8 = shaBytes1Through8;
            this.shaBytes9Through16 = shaBytes9Through16;
            this.shaBytes17Through20 = shaBytes17Through20;
        }

        public Sha1Id(string sha)
        {
            if (sha == null)
            {
                throw new ArgumentNullException(nameof(sha));
            }

            if (sha.Length != ShaStringLength)
            {
                throw new ArgumentException($"Must be length {ShaStringLength}", nameof(sha));
            }

            this.shaBytes1Through8 = ShaSubStringToULong(sha.Substring(0, 16));
            this.shaBytes9Through16 = ShaSubStringToULong(sha.Substring(16, 16));
            this.shaBytes17Through20 = ShaSubStringToUInt(sha.Substring(32, 8));
        }

        public static bool TryParse(string sha, out Sha1Id sha1, out string error)
        {
            error = null;

            try
            {
                sha1 = new Sha1Id(sha);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
            }

            sha1 = new Sha1Id(0, 0, 0);
            return false;
        }

        public static void ShaBufferToParts(
            byte[] shaBuffer,
            out ulong shaBytes1Through8,
            out ulong shaBytes9Through16,
            out uint shaBytes17Through20)
        {
            if (shaBuffer == null)
            {
                throw new ArgumentNullException(nameof(shaBuffer));
            }

            if (shaBuffer.Length != ShaBufferLength)
            {
                throw new ArgumentException($"Must be length {ShaBufferLength}", nameof(shaBuffer));
            }

            unsafe
            {
                fixed (byte* firstChunk = &shaBuffer[0], secondChunk = &shaBuffer[sizeof(ulong)], thirdChunk = &shaBuffer[sizeof(ulong) * 2])
                {
                    shaBytes1Through8 = *(ulong*)firstChunk;
                    shaBytes9Through16 = *(ulong*)secondChunk;
                    shaBytes17Through20 = *(uint*)thirdChunk;
                }
            }
        }

        public void ToBuffer(byte[] shaBuffer)
        {
            unsafe
            {
                fixed (byte* firstChunk = &shaBuffer[0], secondChunk = &shaBuffer[sizeof(ulong)], thirdChunk = &shaBuffer[sizeof(ulong) * 2])
                {
                    *(ulong*)firstChunk = this.shaBytes1Through8;
                    *(ulong*)secondChunk = this.shaBytes9Through16;
                    *(uint*)thirdChunk = this.shaBytes17Through20;
                }
            }
        }

        public override string ToString()
        {
            char[] shaString = new char[ShaStringLength];
            BytesToCharArray(shaString, 0, this.shaBytes1Through8, sizeof(ulong));
            BytesToCharArray(shaString, 2 * sizeof(ulong), this.shaBytes9Through16, sizeof(ulong));
            BytesToCharArray(shaString, 2 * (2 * sizeof(ulong)), this.shaBytes17Through20, sizeof(uint));
            return new string(shaString, 0, shaString.Length);
        }

        private static void BytesToCharArray(char[] shaString, int startIndex, ulong shaBytes, int numBytes)
        {
            byte b;
            int firstArrayIndex;
            for (int i = 0; i < numBytes; ++i)
            {
                b = (byte)(shaBytes >> (i * 8));
                firstArrayIndex = startIndex + (i * 2);
                shaString[firstArrayIndex] = GetHexValue(b / 16);
                shaString[firstArrayIndex + 1] = GetHexValue(b % 16);
            }
        }

        private static ulong ShaSubStringToULong(string shaSubString)
        {
            if (shaSubString == null)
            {
                throw new ArgumentNullException(nameof(shaSubString));
            }

            if (shaSubString.Length != sizeof(ulong) * 2)
            {
                throw new ArgumentException($"Must be length {sizeof(ulong) * 2}", nameof(shaSubString));
            }

            ulong bytes = 0;
            string upperCaseSha = shaSubString.ToUpper();
            int stringIndex = 0;
            for (int i = 0; i < sizeof(ulong); ++i)
            {
                stringIndex = i * 2;
                char firstChar = shaSubString[stringIndex];
                char secondChar = shaSubString[stringIndex + 1];
                byte nextByte = (byte)(CharToByte(firstChar) << 4 | CharToByte(secondChar));
                bytes = bytes | ((ulong)nextByte << (i * 8));
            }

            return bytes;
        }

        private static uint ShaSubStringToUInt(string shaSubString)
        {
            if (shaSubString == null)
            {
                throw new ArgumentNullException(nameof(shaSubString));
            }

            if (shaSubString.Length != sizeof(uint) * 2)
            {
                throw new ArgumentException($"Must be length {sizeof(uint) * 2}", nameof(shaSubString));
            }

            uint bytes = 0;
            string upperCaseSha = shaSubString.ToUpper();
            int stringIndex = 0;
            for (int i = 0; i < sizeof(uint); ++i)
            {
                stringIndex = i * 2;
                char firstChar = shaSubString[stringIndex];
                char secondChar = shaSubString[stringIndex + 1];
                byte nextByte = (byte)(CharToByte(firstChar) << 4 | CharToByte(secondChar));
                bytes = bytes | ((uint)nextByte << (i * 8));
            }

            return bytes;
        }

        private static char GetHexValue(int i)
        {
            if (i < 10)
            {
                return (char)(i + '0');
            }

            return (char)(i - 10 + 'A');
        }

        private static byte CharToByte(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return (byte)(c - '0');
            }

            if (c >= 'A' && c <= 'F')
            {
                return (byte)(10 + (c - 'A'));
            }

            throw new ArgumentException($"Invalid character {c}", nameof(c));
        }
    }
}
