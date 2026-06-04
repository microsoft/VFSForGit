using GVFS.Common.Tracing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GVFS.Common.Git
{
    /// <summary>
    /// Object existence checker that reads MIDX and pack .idx files directly
    /// in managed code. Falls back to loose-object file existence checks.
    /// Thread-safe — all reads are against read-only memory-mapped files.
    /// </summary>
    public class PackIndexObjectExistenceChecker : IObjectExistenceChecker
    {
        private readonly MidxReader[] midxReaders;
        private readonly PackIndexReader[] supplementalPacks;
        private readonly string[] objectRoots;
        private readonly ITracer tracer;

        /// <summary>
        /// Creates a checker that scans packs and loose objects under the given object roots.
        /// Multiple roots are supported (e.g. LocalObjectsRoot and GitObjectsRoot) and
        /// are de-duplicated by normalized path.
        /// </summary>
        public PackIndexObjectExistenceChecker(ITracer tracer, params string[] objectRoots)
        {
            this.tracer = tracer;

            // De-duplicate roots (LocalObjectsRoot == GitObjectsRoot in non-cache scenarios)
            this.objectRoots = objectRoots
                .Where(r => !string.IsNullOrEmpty(r))
                .Select(r => Path.GetFullPath(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            List<MidxReader> midxList = new List<MidxReader>();
            List<PackIndexReader> supplementalList = new List<PackIndexReader>();

            foreach (string root in this.objectRoots)
            {
                string packDir = Path.Combine(root, "pack");
                if (!Directory.Exists(packDir))
                {
                    continue;
                }

                HashSet<string> midxPackStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string midxPath = Path.Combine(packDir, "multi-pack-index");

                if (File.Exists(midxPath))
                {
                    try
                    {
                        MidxReader reader = new MidxReader(midxPath);
                        midxList.Add(reader);
                        midxPackStems = reader.GetPackStems();

                        tracer.RelatedInfo(
                            "PackIndexChecker: Loaded MIDX from {0} ({1:N0} objects, {2} packs)",
                            packDir,
                            reader.TotalObjects,
                            midxPackStems.Count);
                    }
                    catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
                    {
                        tracer.RelatedWarning("PackIndexChecker: Failed to load MIDX at {0}: {1}", midxPath, ex.Message);
                    }
                }

                // Find .idx files not covered by MIDX
                try
                {
                    foreach (string idxFile in Directory.GetFiles(packDir, "*.idx"))
                    {
                        string stem = Path.GetFileNameWithoutExtension(idxFile);
                        if (!midxPackStems.Contains(stem))
                        {
                            try
                            {
                                PackIndexReader reader = new PackIndexReader(idxFile);
                                supplementalList.Add(reader);

                                tracer.RelatedInfo(
                                    "PackIndexChecker: Loaded supplemental idx {0} ({1:N0} objects)",
                                    Path.GetFileName(idxFile),
                                    reader.TotalObjects);
                            }
                            catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
                            {
                                tracer.RelatedWarning(
                                    "PackIndexChecker: Failed to load idx {0}: {1}",
                                    idxFile,
                                    ex.Message);
                            }
                        }
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    // Pack directory disappeared between check and enumeration
                }
            }

            this.midxReaders = midxList.ToArray();
            this.supplementalPacks = supplementalList.ToArray();

            tracer.RelatedInfo(
                "PackIndexChecker: Initialized with {0} MIDX reader(s), {1} supplemental pack(s), {2} object root(s)",
                this.midxReaders.Length,
                this.supplementalPacks.Length,
                this.objectRoots.Length);
        }

        public bool ObjectExists(string sha)
        {
            // Check MIDX readers first (covers the vast majority of objects)
            for (int i = 0; i < this.midxReaders.Length; i++)
            {
                if (this.midxReaders[i].Exists(sha))
                {
                    return true;
                }
            }

            // Check supplemental pack indexes (packs not yet in MIDX)
            for (int i = 0; i < this.supplementalPacks.Length; i++)
            {
                if (this.supplementalPacks[i].Exists(sha))
                {
                    return true;
                }
            }

            // Loose object fallback: check objects/<ab>/<cd...> file existence
            if (sha != null && sha.Length >= GVFSConstants.ShaStringLength)
            {
                string prefix = sha.Substring(0, 2);
                string suffix = sha.Substring(2);
                for (int i = 0; i < this.objectRoots.Length; i++)
                {
                    string loosePath = Path.Combine(this.objectRoots[i], prefix, suffix);
                    if (File.Exists(loosePath))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void Dispose()
        {
            foreach (MidxReader reader in this.midxReaders)
            {
                reader.Dispose();
            }

            foreach (PackIndexReader reader in this.supplementalPacks)
            {
                reader.Dispose();
            }
        }
    }
}
