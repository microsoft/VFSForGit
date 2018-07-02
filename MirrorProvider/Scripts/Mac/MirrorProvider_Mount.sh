#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})
BUILDDIR=$SCRIPTDIR/../../../../BuildOutput/MirrorProvider.Mac/bin/$CONFIGURATION/x64/netcoreapp2.0

dotnet $BUILDDIR/MirrorProvider.Mac.dll mount ~/TestRoot
