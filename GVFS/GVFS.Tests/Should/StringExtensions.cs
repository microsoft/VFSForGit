using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GVFS.Tests.Should
{
    public static class StringExtensions
    {
        public static string Repeat(this string self, int count)
        {
            return string.Join(string.Empty, Enumerable.Range(0, count).Select(x => self).ToArray());
        }
    }
}
