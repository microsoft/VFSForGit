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

# If we're building the Profiling(Release) configuration, remove Profiling() for building .NET code
if [ "$CONFIGURATION" == "Profiling(Release)" ]; then
  CONFIGURATION=Release
fi

# Build the MirrorProvider
dotnet restore $SLN /p:Configuration="$CONFIGURATION.Mac" --packages $ROOTDIR/packages
dotnet build $SLN --configuration $CONFIGURATION.Mac
