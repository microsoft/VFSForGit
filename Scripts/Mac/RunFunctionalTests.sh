#!/bin/bash

. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})

SRCDIR=$SCRIPTDIR/../..
ROOTDIR=$SRCDIR/..
PUBLISHDIR=$ROOTDIR/Publish

sudo mkdir /GVFS.FT
sudo chown $USER /GVFS.FT

$VFS_SRCDIR/ProjFS.Mac/Scripts/LoadPrjFSKext.sh
$VFS_PUBLISHDIR/GVFS.FunctionalTests --full-suite $2
