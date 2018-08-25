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
sudo chown $USER:admin /GVFS.FT
chmod 775 /GVFS.FT
chmod +a "_projfsacl deny search,directory_inherit" /GVFS.FT

$SRCDIR/ProjFS.Mac/Scripts/LoadPrjFSKext.sh
$PUBLISHDIR/GVFS.FunctionalTests --full-suite $2
