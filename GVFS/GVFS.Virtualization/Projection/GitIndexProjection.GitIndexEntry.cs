using GVFS.Common;
using System;
using System.IO;
using System.Linq;
using System.Text;

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

            private static readonly string PathSeparatorString = Path.DirectorySeparatorChar.ToString();

            private int previousFinalSeparatorIndex = int.MaxValue;

            private LazyUTF8String[] lazyPathParts;
            private string[] utf16PathParts;

            public GitIndexEntry(bool useLazyPaths)
            {
                if (useLazyPaths)
                {
                    this.lazyPathParts = new LazyUTF8String[MaxParts];
                }
                else
                {
                    this.utf16PathParts = new string[MaxParts];
                }
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

            public int NumParts
            {
                get; private set;
            }

            public bool HasSameParentAsLastEntry
            {
                get; private set;
            }

            public string GetPathPart(int index)
            {
                if (this.lazyPathParts != null)
                {
                    return this.lazyPathParts[index].GetString();
                }

                return this.utf16PathParts[index];
            }

            public LazyUTF8String GetLazyPathPart(int index)
            {
                if (this.lazyPathParts == null)
                {
                    throw new InvalidOperationException(
                        $"{nameof(GetLazyPathPart)} can only be called when useLazyPaths is set to true when creating {nameof(GitIndexEntry)}");
                }

                return this.lazyPathParts[index];
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
                    byte* bufferPtr;
                    int bufferLength;
                    for (int i = forLoopStartIndex; i < this.PathLength + 1; i++)
                    {
                        if (*forLoopPtr == PathSeparatorCode)
                        {
                            bufferPtr = pathPtr + currentPartStartIndex;
                            bufferLength = i - currentPartStartIndex;
                            if (this.lazyPathParts != null)
                            {
                                this.lazyPathParts[partIndex] = LazyUTF8String.FromByteArray(bufferPtr, bufferLength);
                            }
                            else
                            {
                                this.utf16PathParts[partIndex] = Encoding.UTF8.GetString(bufferPtr, bufferLength);
                            }

                            partIndex++;
                            currentPartStartIndex = i + 1;

                            this.NumParts++;
                            this.previousFinalSeparatorIndex = i;
                        }

                        ++forLoopPtr;
                    }

                    // We unrolled the final part calculation to after the loop, to avoid having to do a 0-byte check inside the for loop
                    bufferPtr = pathPtr + currentPartStartIndex;
                    bufferLength = this.PathLength - currentPartStartIndex;
                    if (this.lazyPathParts != null)
                    {
                        this.lazyPathParts[partIndex] = LazyUTF8String.FromByteArray(bufferPtr, bufferLength);
                    }
                    else
                    {
                        this.utf16PathParts[partIndex] = Encoding.UTF8.GetString(bufferPtr, bufferLength);
                    }

                    this.NumParts++;
                }
            }

            public void ClearLastParent()
            {
                this.previousFinalSeparatorIndex = int.MaxValue;
                this.HasSameParentAsLastEntry = false;
                this.LastParent = null;
            }

            public LazyUTF8String GetLazyChildName()
            {
                return this.GetLazyPathPart(this.NumParts - 1);
            }

            public string GetGitPath()
            {
                return this.GetPath(GVFSConstants.GitPathSeparatorString);
            }

            public string GetRelativePath()
            {
                return this.GetPath(PathSeparatorString);
            }

            private string GetPath(string separator)
            {
                if (this.lazyPathParts != null)
                {
                    return string.Join(separator, this.lazyPathParts.Take(this.NumParts).Select(x => x.GetString()));
                }

                return string.Join(separator, this.utf16PathParts.Take(this.NumParts));
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
