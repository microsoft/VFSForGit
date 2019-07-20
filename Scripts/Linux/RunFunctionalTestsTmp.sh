#!/bin/bash

. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

rm -rf ~/GVFS.FT
mkdir ~/GVFS.FT

LD_LIBRARY_PATH=/home/chrisd/work/github/github/libprojfs/lib/.libs \
PATH=/home/chrisd/GVFS.GIT/bin:$PATH \
$VFS_PUBLISHDIR/GVFS.FunctionalTests --full-suite $2 | \
  tee ~/GVFS.FT/RunFunctionalTestsTmp.log
