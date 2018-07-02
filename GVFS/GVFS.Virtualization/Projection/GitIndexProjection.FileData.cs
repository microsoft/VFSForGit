using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Virtualization.BlobSize;
using System.Collections.Generic;

namespace GVFS.Virtualization.Projection
{
    public partial class GitIndexProjection
    {
        internal class FileData : FolderEntryData
        {
            // Special values that can be stored in Size
            // Use the Size field rather than additional fields to save on memory
            private const long MinValidSize = 0;
            private const long InvalidSize = -1;

            private ulong shaBytes1through8;
            private ulong shaBytes9Through16;
            private uint shaBytes17Through20;

            public override bool IsFolder => false;

            public long Size { get; set; }
            public Sha1Id Sha
            {
                get
                {
                    return new Sha1Id(this.shaBytes1through8, this.shaBytes9Through16, this.shaBytes17Through20);
                }
            }

            public bool IsSizeSet()
            {
                return this.Size >= MinValidSize;
            }

            public string ConvertShaToString()
            {
                return this.Sha.ToString();
            }

            public void ResetData(LazyUTF8String name, byte[] shaBytes)
            {
                this.Name = name;
                this.Size = InvalidSize;
                Sha1Id.ShaBufferToParts(shaBytes, out this.shaBytes1through8, out this.shaBytes9Through16, out this.shaBytes17Through20);
            }

            public bool TryPopulateSizeLocally(
                ITracer tracer,
                GVFSGitObjects gitObjects,
                BlobSizes.BlobSizesConnection blobSizesConnection,
                Dictionary<string, long> availableSizes,
                out string missingSha)
            {
                missingSha = null;
                long blobLength = 0;

                Sha1Id sha1Id = new Sha1Id(this.shaBytes1through8, this.shaBytes9Through16, this.shaBytes17Through20);
                string shaString = null;

                if (availableSizes != null)
                {
                    shaString = this.ConvertShaToString();
                    if (availableSizes.TryGetValue(shaString, out blobLength))
                    {
                        this.Size = blobLength;
                        return true;
                    }
                }

                try
                {
                    if (blobSizesConnection.TryGetSize(sha1Id, out blobLength))
                    {
                        this.Size = blobLength;
                        return true;
                    }
                }
                catch (BlobSizesException e)
                {
                    EventMetadata metadata = CreateEventMetadata(e);
                    missingSha = this.ConvertShaToString();
                    metadata.Add(nameof(missingSha), missingSha);
                    tracer.RelatedWarning(metadata, $"{nameof(this.TryPopulateSizeLocally)}: Exception while trying to get file size", Keywords.Telemetry);
                }

                if (missingSha == null)
                {
                    missingSha = (shaString == null) ? this.ConvertShaToString() : shaString;
                }

                if (gitObjects.TryGetBlobSizeLocally(missingSha, out blobLength))
                {
                    this.Size = blobLength;

                    // There is no flush for this value because it's already local, so there's little loss if it doesn't get persisted
                    // But it's faster to wait for some remote call to batch this value into a different flush
                    blobSizesConnection.BlobSizesDatabase.AddSize(sha1Id, blobLength);
                    return true;
                }

                return false;
            }
        }
    }
}
