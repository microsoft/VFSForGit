#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})
BUILDDIR=$SCRIPTDIR/../../../../BuildOutput/MirrorProvider.Linux/bin/$CONFIGURATION/x64/netcoreapp2.1

PATH_TO_MIRROR="${PATH_TO_MIRROR:-$HOME/PathToMirror}"
TEST_ROOT="${TEST_ROOT:-$HOME/TestRoot}"

dotnet $BUILDDIR/MirrorProvider.Linux.dll clone "$PATH_TO_MIRROR" "$TEST_ROOT"
