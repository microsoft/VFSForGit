# Third-Party Notices

VFS for Git incorporates third-party native libraries that are statically
linked into the final executable when building with NativeAOT. This file
documents those dependencies and their licenses.

## libgit2

**Source:** https://github.com/libgit2/libgit2
**Version:** 1.9.3 (via vcpkg overlay port in overlays/libgit2/)
**License:** GPLv2 with Linking Exception

libgit2 is used for local Git object lookups (commit parsing, tree walking,
blob size queries, config reading). VFS for Git calls libgit2 via P/Invoke
and, when building with NativeAOT, statically links the compiled library into
the executable.

The libgit2 COPYING file (https://github.com/libgit2/libgit2/blob/main/COPYING)
includes the following linking exception:

> In addition to the permissions in the GNU General Public License,
> the authors give you unlimited permission to link the compiled
> version of this library into combinations with other programs,
> and to distribute those combinations without any restriction
> coming from the use of this file. (The General Public License
> restrictions do apply in other respects; for example, they cover
> modification of the file, and distribution when not linked into
> a combined executable.)

This exception explicitly permits both dynamic and static linking of the
compiled libgit2 library into VFS for Git without imposing GPL obligations
on VFS for Git's own MIT-licensed source code. Modifications to libgit2
itself remain subject to the GPLv2.

The full GPLv2 license text is available at:
https://www.gnu.org/licenses/old-licenses/gpl-2.0.html

## SQLite

**Source:** https://www.sqlite.org/
**Version:** Bundled via SQLitePCLRaw.lib.e_sqlite3 NuGet package
**License:** Public Domain

SQLite is used for persistent storage of placeholder lists and blob size
caches. The SQLite C library is in the public domain and imposes no
restrictions on linking or distribution.

> The author disclaims copyright to this source code. In place of a
> legal notice, here is a blessing:
>
> May you do good and not evil.
> May you find forgiveness for yourself and forgive others.
> May you share freely, never taking more than you give.

https://www.sqlite.org/copyright.html

## SQLitePCLRaw / Microsoft.Data.Sqlite

**Source:** https://github.com/ericsink/SQLitePCL.raw (SQLitePCLRaw),
https://github.com/dotnet/efcore (Microsoft.Data.Sqlite)
**License:** Apache License 2.0

These managed libraries provide the .NET API surface for SQLite access.
SQLitePCLRaw handles the P/Invoke layer to the native SQLite library, and
Microsoft.Data.Sqlite provides the ADO.NET provider. Both are licensed under
the Apache License 2.0, which permits static linking without restriction.

The full Apache License 2.0 text is available at:
https://www.apache.org/licenses/LICENSE-2.0
