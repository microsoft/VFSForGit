#!/bin/bash

. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

sudo rm -rf /GVFS.FT
sudo mkdir /GVFS.FT
sudo chown $USER /GVFS.FT

$VFS_PUBLISHDIR/GVFS.FunctionalTests --full-suite $2 | \
  tee /GVFS.FT/RunFunctionalTests.log
