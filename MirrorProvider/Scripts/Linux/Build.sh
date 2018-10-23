#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})

SRCDIR=$SCRIPTDIR/../../..
ROOTDIR=$SRCDIR/..
SLN=$SRCDIR/MirrorProvider/MirrorProvider.sln

# Build the ProjFS library interface
$SRCDIR/ProjFS.Linux/Scripts/Build.sh $CONFIGURATION

# Build the MirrorProvider
dotnet restore $SLN /p:Configuration="$CONFIGURATION.Linux" --packages $ROOTDIR/packages
dotnet build $SLN --configuration $CONFIGURATION.Linux
