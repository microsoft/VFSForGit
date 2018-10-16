#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR="$(dirname ${BASH_SOURCE[0]})"

# convert to an absolute path because it is required by `dotnet publish`
pushd $SCRIPTDIR
SCRIPTDIR="$(pwd)"
popd

SRCDIR=$SCRIPTDIR/../..
ROOTDIR=$SRCDIR/..
BUILDOUTPUT=$ROOTDIR/BuildOutput
PUBLISHDIR=$ROOTDIR/Publish

if [ ! -d $BUILDOUTPUT ]; then
  mkdir $BUILDOUTPUT
fi

PACKAGES=$ROOTDIR/packages

# Build the ProjFS kext and libraries
$SRCDIR/ProjFS.Mac/Scripts/Build.sh $CONFIGURATION || exit 1

# Create the directory where we'll do pre build tasks
BUILDDIR=$BUILDOUTPUT/GVFS.Build
if [ ! -d $BUILDDIR ]; then
  mkdir $BUILDDIR || exit 1
fi

$SCRIPTDIR/DownloadGVFSGit.sh || exit 1
GITVERSION="$($SCRIPTDIR/GetGitVersionNumber.sh)"
GITPATH="$(find $PACKAGES/gitformac.gvfs.installer/$GITVERSION -type f -name *.dmg)" || exit 1
# Now that we have a path containing the version number, generate GVFSConstants.GitVersion.cs
$SCRIPTDIR/GenerateGitVersionConstants.sh "$GITPATH" $BUILDDIR || exit 1

# If we're building the Profiling(Release) configuration, remove Profiling() for building .NET code
if [ "$CONFIGURATION" == "Profiling(Release)" ]; then
  CONFIGURATION=Release
fi

dotnet restore $SRCDIR/GVFS.sln /p:Configuration=$CONFIGURATION.Mac --packages $PACKAGES || exit 1
dotnet build $SRCDIR/GVFS.sln --runtime osx-x64 --framework netcoreapp2.1 --configuration $CONFIGURATION.Mac /maxcpucount:1 || exit 1
dotnet publish $SRCDIR/GVFS.sln /p:Configuration=$CONFIGURATION.Mac /p:Platform=x64 --runtime osx-x64 --framework netcoreapp2.1 --self-contained --output $PUBLISHDIR /maxcpucount:1 || exit 1

NATIVEDIR=$SRCDIR/GVFS/GVFS.Native.Mac
xcodebuild -configuration $CONFIGURATION -workspace $NATIVEDIR/GVFS.Native.Mac.xcworkspace build -scheme GVFS.Native.Mac -derivedDataPath $ROOTDIR/BuildOutput/GVFS.Native.Mac || exit 1

echo 'Copying native binaries to Publish directory'
cp $BUILDOUTPUT/GVFS.Native.Mac/Build/Products/$CONFIGURATION/GVFS.ReadObjectHook $PUBLISHDIR || exit 1
cp $BUILDOUTPUT/GVFS.Native.Mac/Build/Products/$CONFIGURATION/GVFS.VirtualFileSystemHook $PUBLISHDIR || exit 1

$PUBLISHDIR/GVFS.UnitTests || exit 1
