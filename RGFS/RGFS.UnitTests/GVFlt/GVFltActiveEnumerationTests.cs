using RGFS.GVFlt;
using RGFS.Tests.Should;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RGFS.UnitTests.GVFlt
{
    [TestFixture]
    public class GVFltActiveEnumerationTests
    {
        [TestCase]
        public void EnumerationHandlesEmptyList()
        {
            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(new List<GVFltFileInfo>()))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(false);
                activeEnumeration.MoveNext().ShouldEqual(false);
                activeEnumeration.RestartEnumeration(string.Empty);
                activeEnumeration.IsCurrentValid.ShouldEqual(false);
            }
        }

        [TestCase]
        public void EnumerateSingleEntryList()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false)
            };

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }
        }

        [TestCase]
        public void EnumerateMultipleEntries()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false),
                new GVFltFileInfo("B", size: 0, isFolder:true),
                new GVFltFileInfo("c", size: 0, isFolder:false),
                new GVFltFileInfo("D.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.bat", size: 0, isFolder:false),
            };

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }
        }

        [TestCase]
        public void EnumerateSingleEntryListWithEmptyFilter()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false)
            };

            // Test empty string ("") filter
            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.TrySaveFilterString(string.Empty).ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }

            // Test null filter
            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.TrySaveFilterString(null).ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }
        }

        [TestCase]
        public void EnumerateSingleEntryListWithWildcardFilter()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false)
            };

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.TrySaveFilterString("*").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.TrySaveFilterString("?").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.TrySaveFilterString("*.*").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }
        }

        [TestCase]
        public void EnumerateSingleEntryListWithMatchingFilter()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false)
            };

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.TrySaveFilterString("a").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.TrySaveFilterString("A").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }
        }

        [TestCase]
        public void EnumerateSingleEntryListWithNonMatchingFilter()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false)
            };

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                string filter = "b";
                activeEnumeration.TrySaveFilterString(filter).ShouldEqual(true);
                activeEnumeration.IsCurrentValid.ShouldEqual(false);
                activeEnumeration.MoveNext().ShouldEqual(false);
                activeEnumeration.RestartEnumeration(filter);
                activeEnumeration.IsCurrentValid.ShouldEqual(false);
            }
        }

        [TestCase]
        public void CannotSetMoreThanOneFilter()
        {
            string filterString = "*.*";

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(new List<GVFltFileInfo>()))
            {
                activeEnumeration.TrySaveFilterString(filterString).ShouldEqual(true);
                activeEnumeration.TrySaveFilterString(null).ShouldEqual(false);
                activeEnumeration.TrySaveFilterString(string.Empty).ShouldEqual(false);
                activeEnumeration.TrySaveFilterString("?").ShouldEqual(false);
                activeEnumeration.GetFilterString().ShouldEqual(filterString);
            }
        }

        [TestCase]
        public void EnumerateMultipleEntryListWithEmptyFilter()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false),
                new GVFltFileInfo("B", size: 0, isFolder:true),
                new GVFltFileInfo("c", size: 0, isFolder:false),
                new GVFltFileInfo("D.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.bat", size: 0, isFolder:false),
            };

            // Test empty string ("") filter
            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString(string.Empty).ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }

            // Test null filter
            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString(null).ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }
        }

        [TestCase]
        public void EnumerateMultipleEntryListWithWildcardFilter()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false),
                new GVFltFileInfo("B", size: 0, isFolder:true),
                new GVFltFileInfo("c", size: 0, isFolder:false),
                new GVFltFileInfo("D.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.bat", size: 0, isFolder:false),
            };

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("*.*").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries);
            }

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("*.txt").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => entry.Name.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase)));
            }

            // '<' = DOS_STAR, matches 0 or more characters until encountering and matching
            //                 the final . in the name
            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("<.txt").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => entry.Name.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase)));
            }

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("?").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => entry.Name.Length == 1));
            }

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("?.txt").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => entry.Name.Length == 5 && entry.Name.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase)));
            }

            // '>' = DOS_QM, matches any single character, or upon encountering a period or
            //               end of name string, advances the expression to the end of the
            //               set of contiguous DOS_QMs.
            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString(">.txt").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => entry.Name.Length == 5 && entry.Name.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase)));
            }

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("E.???").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => entry.Name.Length == 5 && entry.Name.StartsWith("E.", System.StringComparison.OrdinalIgnoreCase)));
            }

            // '"' = DOS_DOT, matches either a . or zero characters beyond name string.
            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("E\"*").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => entry.Name.StartsWith("E.", System.StringComparison.OrdinalIgnoreCase) || entry.Name.Equals("E", System.StringComparison.OrdinalIgnoreCase)));
            }

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("e\"*").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => entry.Name.StartsWith("E.", System.StringComparison.OrdinalIgnoreCase) || entry.Name.Equals("E", System.StringComparison.OrdinalIgnoreCase)));
            }

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("B\"*").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => entry.Name.StartsWith("B.", System.StringComparison.OrdinalIgnoreCase) || entry.Name.Equals("B", System.StringComparison.OrdinalIgnoreCase)));
            }

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("e.???").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => entry.Name.Length == 5 && entry.Name.StartsWith("E.", System.StringComparison.OrdinalIgnoreCase)));
            }
        }

        [TestCase]
        public void EnumerateMultipleEntryListWithMatchingFilter()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false),
                new GVFltFileInfo("B", size: 0, isFolder:true),
                new GVFltFileInfo("c", size: 0, isFolder:false),
                new GVFltFileInfo("D.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.bat", size: 0, isFolder:false),
            };

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("E.bat").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => entry.Name == "E.bat"));
            }

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.TrySaveFilterString("e.bat").ShouldEqual(true);
                this.ValidateActiveEnumeratorReturnsAllEntries(activeEnumeration, entries.Where(entry => string.Compare(entry.Name, "e.bat", StringComparison.OrdinalIgnoreCase) == 0));
            }
        }

        [TestCase]
        public void EnumerateMultipleEntryListWithNonMatchingFilter()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false),
                new GVFltFileInfo("B", size: 0, isFolder:true),
                new GVFltFileInfo("c", size: 0, isFolder:false),
                new GVFltFileInfo("D.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.bat", size: 0, isFolder:false),
            };

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                string filter = "g";
                activeEnumeration.TrySaveFilterString(filter).ShouldEqual(true);
                activeEnumeration.IsCurrentValid.ShouldEqual(false);
                activeEnumeration.MoveNext().ShouldEqual(false);
                activeEnumeration.RestartEnumeration(filter);
                activeEnumeration.IsCurrentValid.ShouldEqual(false);
            }
        }

        [TestCase]
        public void SettingFilterAdvancesEnumeratorToMatchingEntry()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false),
                new GVFltFileInfo("B", size: 0, isFolder:true),
                new GVFltFileInfo("c", size: 0, isFolder:false),
                new GVFltFileInfo("D.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.bat", size: 0, isFolder:false),
            };

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.Current.ShouldBeSameAs(entries[0]);
                activeEnumeration.TrySaveFilterString("D.txt").ShouldEqual(true);
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.Current.Name.ShouldEqual("D.txt");
            }
        }

        [TestCase]
        public void RestartingScanWithFilterAdvancesEnumeratorToNewMatchingEntry()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("a", size: 0, isFolder:false),
                new GVFltFileInfo("B", size: 0, isFolder:true),
                new GVFltFileInfo("c", size: 0, isFolder:false),
                new GVFltFileInfo("D.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.bat", size: 0, isFolder:false),
            };

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.Current.ShouldBeSameAs(entries[0]);
                activeEnumeration.TrySaveFilterString("D.txt").ShouldEqual(true);
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.Current.Name.ShouldEqual("D.txt");

                activeEnumeration.RestartEnumeration("c");
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.Current.Name.ShouldEqual("c");
            }
        }

        [TestCase]
        public void RestartingScanWithFilterAdvancesEnumeratorToFirstMatchingEntry()
        {
            List<GVFltFileInfo> entries = new List<GVFltFileInfo>()
            {
                new GVFltFileInfo("C.TXT", size: 0, isFolder:false),
                new GVFltFileInfo("D.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.txt", size: 0, isFolder:false),
                new GVFltFileInfo("E.bat", size: 0, isFolder:false),
            };

            using (GVFltActiveEnumeration activeEnumeration = new GVFltActiveEnumeration(entries))
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.Current.ShouldBeSameAs(entries[0]);
                activeEnumeration.TrySaveFilterString("D.txt").ShouldEqual(true);
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.Current.Name.ShouldEqual("D.txt");

                activeEnumeration.RestartEnumeration("c*");
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.Current.Name.ShouldEqual("C.TXT");
            }
        }

        private void ValidateActiveEnumeratorReturnsAllEntries(GVFltActiveEnumeration activeEnumeration, IEnumerable<GVFltFileInfo> entries)
        {
            activeEnumeration.IsCurrentValid.ShouldEqual(true);

            // activeEnumeration should iterate over each entry in entries
            foreach (GVFltFileInfo entry in entries)
            {
                activeEnumeration.IsCurrentValid.ShouldEqual(true);
                activeEnumeration.Current.ShouldBeSameAs(entry);
                activeEnumeration.MoveNext();
            }

            // activeEnumeration should no longer be valid after iterating beyond the end of the list
            activeEnumeration.IsCurrentValid.ShouldEqual(false);

            // attempts to move beyond the end of the list should fail
            activeEnumeration.MoveNext().ShouldEqual(false);
        }
    }
}