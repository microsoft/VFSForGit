using GVFS.Common;
using System.Linq;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        /// <summary>
        /// Data for an entry in the git index 
        /// </summary>
        /// <remarks>
        /// GitIndexEntry should not be used for storing projection data. It's designed for 
        /// temporary storage of a single entry from the index.
        /// </remarks>
        internal class GitIndexEntry
        {
            private const int MaxPathBufferSize = 4096;
            private const int MaxParts = MaxPathBufferSize / 2;
            private const byte PathSeparatorCode = 0x2F;

            private int previousFinalSeparatorIndex = int.MaxValue;

            public GitIndexEntry()
            {
                this.PathParts = new LazyUTF8String[MaxParts];
            }

            public byte[] Sha { get; } = new byte[20];
            public bool SkipWorktree { get; set; }
            public FileTypeAndMode TypeAndMode { get; set; }
            public GitIndexParser.MergeStage MergeState { get; set; }
            public int ReplaceIndex { get; set; }

            /// <summary>
            /// Number of bytes for the path in the PathBuffer
            /// </summary>
            public int PathLength { get; set; }
            public byte[] PathBuffer { get; } = new byte[MaxPathBufferSize];
            public FolderData LastParent { get; set; }

            public LazyUTF8String[] PathParts
            {
                get; private set;
            }

            public int NumParts
            {
                get; private set;
            }

            public bool HasSameParentAsLastEntry
            {
                get; private set;
            }

            public unsafe void ParsePath()
            {
                this.PathBuffer[this.PathLength] = 0;

                // The index of that path part that is after the path separator
                int currentPartStartIndex = 0;

                // The index to start looking for the next path separator
                // Because the previous final separator is stored and we know where the previous path will be replaced
                // the code can use the previous final separator to start looking from that point instead of having to 
                // run through the entire path to break it apart
                /* Example:
                 * Previous path = folder/where/previous/separator/is/used/file.txt
                 * This path     = folder/where/previous/separator/is/used/file2.txt
                 *                                                        ^    ^
                 *                         this.previousFinalSeparatorIndex    |
                 *                                                             this.ReplaceIndex
                 *
                 *   folder/where/previous/separator/is/used/file2.txt
                 *                                           ^^
                 *                       currentPartStartIndex|
                 *                                            forLoopStartIndex
                 */
                int forLoopStartIndex = 0;

                fixed (byte* pathPtr = this.PathBuffer)
                {
                    if (this.previousFinalSeparatorIndex < this.ReplaceIndex &&
                        !this.RangeContains(pathPtr + this.ReplaceIndex, this.PathLength - this.ReplaceIndex, PathSeparatorCode))
                    {
                        // Only need to parse the last part, because the rest of the string is unchanged

                        // The logical thing to do would be to start the for loop at previousFinalSeparatorIndex+1, but two 
                        // repeated / characters would make an invalid path, so we'll assume that git would not have stored that path
                        forLoopStartIndex = this.previousFinalSeparatorIndex + 2;

                        // we still do need to start the current part's index at the correct spot, so subtract one for that
                        currentPartStartIndex = forLoopStartIndex - 1;

                        this.NumParts--;

                        this.HasSameParentAsLastEntry = true;
                    }
                    else
                    {
                        this.NumParts = 0;
                        this.ClearLastParent();
                    }

                    int partIndex = this.NumParts;

                    byte* forLoopPtr = pathPtr + forLoopStartIndex;
                    for (int i = forLoopStartIndex; i < this.PathLength + 1; i++)
                    {
                        if (*forLoopPtr == PathSeparatorCode)
                        {
                            this.PathParts[partIndex] = LazyUTF8String.FromByteArray(pathPtr + currentPartStartIndex, i - currentPartStartIndex);

                            partIndex++;
                            currentPartStartIndex = i + 1;

                            this.NumParts++;
                            this.previousFinalSeparatorIndex = i;
                        }

                        ++forLoopPtr;
                    }

                    // We unrolled the final part calculation to after the loop, to avoid having to do a 0-byte check inside the for loop
                    this.PathParts[partIndex] = LazyUTF8String.FromByteArray(pathPtr + currentPartStartIndex, this.PathLength - currentPartStartIndex);

                    this.NumParts++;
                }
            }

            public void ClearLastParent()
            {
                this.previousFinalSeparatorIndex = int.MaxValue;
                this.HasSameParentAsLastEntry = false;
                this.LastParent = null;
            }

            public LazyUTF8String GetChildName()
            {
                return this.PathParts[this.NumParts - 1];
            }

            public string GetFullPath()
            {
                return string.Join(GVFSConstants.GitPathSeparatorString, this.PathParts.Take(this.NumParts).Select(x => x.GetString()));
            }
            
            private unsafe bool RangeContains(byte* bufferPtr, int count, byte value)
            {
                byte* indexPtr = bufferPtr;
                while (indexPtr - bufferPtr < count)
                {
                    if (*indexPtr == value)
                    {
                        return true;
                    }

                    ++indexPtr;
                }

                return false;
            }
        }
    }
}
