#!/bin/bash

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

$SRCDIR/ProjFS.Mac/Scripts/LoadPrjFSKext.sh
$PUBLISHDIR/GVFS.FunctionalTests --full-suite
