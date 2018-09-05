#!/bin/bash

REPOURL=$1

CONFIGURATION=$2
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})

ROOTDIR=$SCRIPTDIR/../../..
PUBLISHDIR=$ROOTDIR/Publish 

mkdir ~/GVFSTest
sudo chown $USER:admin ~/GVFSTest
chmod 775 ~/GVFSTest
chmod +a "_projfsacl deny search,directory_inherit" ~/GVFSTest 

$PUBLISHDIR/gvfs clone $REPOURL ~/GVFSTest/enlistment --local-cache-path ~/GVFSTest/.gvfsCache --no-mount --no-prefetch
