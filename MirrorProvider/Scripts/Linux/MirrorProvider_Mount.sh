#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})
BUILDDIR=$SCRIPTDIR/../../../../BuildOutput/MirrorProvider.Linux/bin/$CONFIGURATION/x64/netcoreapp2.1

dotnet $BUILDDIR/MirrorProvider.Linux.dll mount ~/TestRoot
