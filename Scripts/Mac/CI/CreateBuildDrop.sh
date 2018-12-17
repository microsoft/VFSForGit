#!/bin/bash

. "$(dirname ${BASH_SOURCE[0]})/../InitializeEnvironment.sh"

CONFIGURATION=$1
BUILDDROP_ROOT=$2
if [ -z $BUILDDROP_ROOT ] || [ -z $CONFIGURATION ]; then
  echo 'ERROR: Usage: CreateBuildDrop.sh [configuration] [build drop root directory]'
  exit 1
fi

# Set up some paths
BUILDDROP_BUILDOUTPUT=$BUILDDROP_ROOT/BuildOutput
BUILDDROP_SRC=$BUILDDROP_ROOT/src
BUILDDROP_PROJFS=$BUILDDROP_SRC/ProjFS.Mac
BUILDDROP_KEXT=$BUILDDROP_BUILDOUTPUT/ProjFS.Mac/Native/Build/Products

# Set up the build drop directory structure
rm -rf $BUILDDROP_ROOT
mkdir -p $BUILDDROP_BUILDOUTPUT
mkdir -p $BUILDDROP_SRC
mkdir -p $BUILDDROP_PROJFS
mkdir -p $BUILDDROP_KEXT

# Copy to the build drop, retaining directory structure.
rsync -avm $VFS_OUTPUTDIR/Git $BUILDDROP_BUILDOUTPUT
rsync -avm $VFS_PUBLISHDIR $BUILDDROP_ROOT
rsync -avm $VFS_SCRIPTDIR $BUILDDROP_SRC/Scripts
rsync -avm $VFS_SRCDIR/ProjFS.Mac/Scripts $BUILDDROP_PROJFS
rsync -avm $VFS_OUTPUTDIR/ProjFS.Mac/Native/Build/Products/$CONFIGURATION $BUILDDROP_KEXT
cp $VFS_SRCDIR/nuget.config $BUILDDROP_SRC
