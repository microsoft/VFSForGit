#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})

SRCDIR=$SCRIPTDIR/../../..
ROOTDIR=$SRCDIR/..
SLN=$SRCDIR/MirrorProvider/MirrorProvider.sln

# Build the ProjFS kext and libraries
$SRCDIR/ProjFS.Mac/Scripts/Build.sh $CONFIGURATION

# Build the MirrorProvider
dotnet restore $SLN /p:Configuration="$CONFIGURATION.Mac" --packages $ROOTDIR/packages
dotnet build $SLN --configuration $CONFIGURATION.Mac
