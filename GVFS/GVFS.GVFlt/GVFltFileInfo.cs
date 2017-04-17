using System;
using System.Collections.Generic;

namespace GVFS.GVFlt
{
    public class GVFltFileInfo
    {
        public GVFltFileInfo(string name, long size, bool isFolder)
        {
            this.Name = name;
            this.Size = size;
            this.IsFolder = isFolder;
        }

        public string Name { get; }
        public long Size { get; }
        public bool IsFolder { get; }

        public static IComparer<GVFltFileInfo> SortAlphabeticallyIgnoreCase()
        {
            return new SortFileInfoAlphabetically();
        }

        private class SortFileInfoAlphabetically : IComparer<GVFltFileInfo>
        {
            public int Compare(GVFltFileInfo a, GVFltFileInfo b)
            {
                if (a == null)
                {
                    if (b == null)
                    {
                        return 0;
                    }
                    else
                    {
                        return -1;
                    }
                }
                else
                {
                    if (b == null)
                    {
                        return 1;
                    }

                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }
}
