#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})

ROOTDIR=$SCRIPTDIR/../../..
PUBLISHDIR=$ROOTDIR/Publish 

$PUBLISHDIR/gvfs clone https://gvfs.visualstudio.com/ci/_git/ForTests ~/GVFSTest --local-cache-path ~/GVFSTest/.gvfsCache --no-mount --no-prefetch
