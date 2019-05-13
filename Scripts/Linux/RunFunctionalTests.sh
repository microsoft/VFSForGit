#!/bin/bash

. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

sudo mkdir /GVFS.FT
sudo chown $USER /GVFS.FT

$VFS_PUBLISHDIR/GVFS.FunctionalTests --full-suite $2
