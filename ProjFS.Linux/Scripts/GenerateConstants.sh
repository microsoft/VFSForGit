#!/bin/bash

set -e

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})
NOTIFY=""

for f in /usr/{local/,}include/projfs/projfs_notify.h; do
	if [ -f "$f" ]; then
		NOTIFY="$f"
		break
	fi
done

if [ -z "$NOTIFY" ]; then
	echo >&2 "could not find projfs_notify.h"
	exit 1
fi

# find all #defines that have a value
CONSTS=($(grep '#define PROJFS\S\+\s\+\S' "$NOTIFY" | awk '{ print $2 }'))

t=$(mktemp --tmpdir vfsforgitXXXXX.c) || exit
trap "rm -f -- '$t'{,.o}" EXIT

cat >"$t" <<EOF
#include <stdint.h>
#include <stdio.h>

#include <projfs/projfs_notify.h>

int main(int arg, char **argv)
{
EOF

for const in "${CONSTS[@]}"; do
	cat >>"$t" <<EOF
	printf("            public const ulong $const = 0x%08lx;\n", (uint64_t)$const);
EOF
done

cat >>"$t" <<EOF
	return 0;
}
EOF

gcc -o "$t".o "$t" -Wall

exec >"$SCRIPTDIR"/../PrjFSLib.Linux.Managed/Interop/ProjFS.Constants.cs
cat <<EOF
namespace PrjFSLib.Linux.Interop
{
    internal partial class ProjFS
    {
        internal static class Constants
        {
EOF

"$t".o

cat <<EOF
        }
    }
}
EOF

rm -f -- "$t"{,.o}
trap - EXIT
exit
