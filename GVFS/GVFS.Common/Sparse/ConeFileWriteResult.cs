namespace GVFS.Common.Sparse
{
    /// <summary>
    /// The result of <see cref="ConeFileWriter.Write"/>.
    /// </summary>
    public enum ConeFileWriteResult
    {
        /// <summary>The file already had the serialized content. Nothing was written.</summary>
        Unchanged,

        /// <summary>The file now has the new serialized content.</summary>
        Written,

        /// <summary>The write failed. The file keeps its previous content.</summary>
        Failed,
    }
}
