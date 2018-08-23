#!/bin/bash

REPOURL=$1

CONFIGURATION=$2
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})

ROOTDIR=$SCRIPTDIR/../../..
PUBLISHDIR=$ROOTDIR/Publish 

$PUBLISHDIR/gvfs clone $REPOURL ~/GVFSTest --local-cache-path ~/GVFSTest/.gvfsCache --no-mount --no-prefetch
