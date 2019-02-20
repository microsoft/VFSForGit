#!/bin/bash
. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

REPOURL=$1

CONFIGURATION=$2
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

$VFS_PUBLISHDIR/gvfs clone $REPOURL ~/GVFSTest --local-cache-path ~/GVFSTest/.gvfsCache --no-mount --no-prefetch
