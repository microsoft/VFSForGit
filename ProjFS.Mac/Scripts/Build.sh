#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})
SRCDIR=$SCRIPTDIR/../..
ROOTDIR=$SRCDIR/..
PACKAGES=$ROOTDIR/packages

PROJFS=$SRCDIR/ProjFS.Mac
if [ -z "$DEVELOPMENT_TEAM" ]
then 
    echo "DEVELOPMENT_TEAM environment variable not set, using DEVELOPMENT_TEAM from project..."
    xcodebuild -configuration $CONFIGURATION -workspace $PROJFS/PrjFS.xcworkspace build -scheme PrjFS -derivedDataPath $ROOTDIR/BuildOutput/ProjFS.Mac/Native || exit 1
else
    echo "DEVELOPMENT_TEAM environment variable set to $DEVELOPMENT_TEAM, using it for build..."
    xcodebuild -configuration $CONFIGURATION -workspace $PROJFS/PrjFS.xcworkspace build -scheme PrjFS -derivedDataPath $ROOTDIR/BuildOutput/ProjFS.Mac/Native DEVELOPMENT_TEAM=$DEVELOPMENT_TEAM || exit 1
fi

# If we're building the Profiling(Release) configuration, remove Profiling() for building .NET code
if [ "$CONFIGURATION" == "Profiling(Release)" ]; then
  CONFIGURATION=Release
fi

dotnet restore $PROJFS/PrjFSLib.Mac.Managed/PrjFSLib.Mac.Managed.csproj /p:Configuration=$CONFIGURATION /p:Platform=x64 --packages $PACKAGES || exit 1
dotnet build $PROJFS/PrjFSLib.Mac.Managed/PrjFSLib.Mac.Managed.csproj /p:Configuration=$CONFIGURATION /p:Platform=x64 || exit 1
