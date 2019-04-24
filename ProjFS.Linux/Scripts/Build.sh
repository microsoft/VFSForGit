#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})
SRCDIR=$SCRIPTDIR/../..
ROOTDIR=$SRCDIR/..
PACKAGES=$ROOTDIR/packages

PROJFS=$SRCDIR/ProjFS.Linux

echo "Generating ProjFS.Linux constants..."
"$SCRIPTDIR"/GenerateConstants.sh || exit 1

# If we're building the Profiling(Release) configuration, remove Profiling() for building .NET code
if [ "$CONFIGURATION" == "Profiling(Release)" ]; then
  CONFIGURATION=Release
fi

echo "Restoring and building ProjFS.Linux packages..."
dotnet restore $PROJFS/PrjFSLib.Linux.Managed/PrjFSLib.Linux.Managed.csproj /p:Configuration=$CONFIGURATION /p:Platform=x64 --packages $PACKAGES || exit 1
dotnet build $PROJFS/PrjFSLib.Linux.Managed/PrjFSLib.Linux.Managed.csproj /p:Configuration=$CONFIGURATION /p:Platform=x64 || exit 1
