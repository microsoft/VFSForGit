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

# TODO(Linux): use pkg-config to look for installed libprojfs;
#              if not found, retrieve latest stable from repo, build
#              into $ROOTDIR/BuildOutput/ProjFS.Linux/Native with
#              given $CONFIGURATION

dotnet restore $PROJFS/PrjFSLib.Linux.Managed/PrjFSLib.Linux.Managed.csproj /p:Configuration=$CONFIGURATION /p:Platform=x64 --packages $PACKAGES || exit 1
dotnet build $PROJFS/PrjFSLib.Linux.Managed/PrjFSLib.Linux.Managed.csproj /p:Configuration=$CONFIGURATION /p:Platform=x64 || exit 1
