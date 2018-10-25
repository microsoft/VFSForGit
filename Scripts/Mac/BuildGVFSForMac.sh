#!/bin/bash

. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

if [ ! -d $VFS_OUTPUTDIR ]; then
  mkdir $VFS_OUTPUTDIR
fi

echo 'Building ProjFS kext and libraries...'
$VFS_SRCDIR/ProjFS.Mac/Scripts/Build.sh $CONFIGURATION || exit 1

# Create the directory where we'll do pre build tasks
BUILDDIR=$VFS_OUTPUTDIR/GVFS.Build
if [ ! -d $BUILDDIR ]; then
  mkdir $BUILDDIR || exit 1
fi

echo 'Downloading a VFS-enabled version of Git...'
$VFS_SCRIPTDIR/DownloadGVFSGit.sh || exit 1
GITVERSION="$($VFS_SCRIPTDIR/GetGitVersionNumber.sh)"
GITPATH="$(find $VFS_PACKAGESDIR/gitformac.gvfs.installer/$GITVERSION -type f -name *.dmg)" || exit 1
echo "Downloaded Git $GITVERSION"
# Now that we have a path containing the version number, generate GVFSConstants.GitVersion.cs
$VFS_SCRIPTDIR/GenerateGitVersionConstants.sh "$GITPATH" $BUILDDIR || exit 1

# If we're building the Profiling(Release) configuration, remove Profiling() for building .NET code
if [ "$CONFIGURATION" == "Profiling(Release)" ]; then
  CONFIGURATION=Release
fi

echo 'Restoring packages...'
dotnet restore $VFS_SRCDIR/GVFS.sln /p:Configuration=$CONFIGURATION.Mac --packages $VFS_PACKAGESDIR || exit 1
dotnet build $VFS_SRCDIR/GVFS.sln --runtime osx-x64 --framework netcoreapp2.1 --configuration $CONFIGURATION.Mac /maxcpucount:1 || exit 1
dotnet publish $VFS_SRCDIR/GVFS.sln /p:Configuration=$CONFIGURATION.Mac /p:Platform=x64 --runtime osx-x64 --framework netcoreapp2.1 --self-contained --output $VFS_PUBLISHDIR /maxcpucount:1 || exit 1

NATIVEDIR=$VFS_SRCDIR/GVFS/GVFS.Native.Mac
xcodebuild -configuration $CONFIGURATION -workspace $NATIVEDIR/GVFS.Native.Mac.xcworkspace build -scheme GVFS.Native.Mac -derivedDataPath $VFS_OUTPUTDIR/GVFS.Native.Mac || exit 1

echo 'Copying native binaries to Publish directory...'
cp $VFS_OUTPUTDIR/GVFS.Native.Mac/Build/Products/$CONFIGURATION/GVFS.ReadObjectHook $VFS_PUBLISHDIR || exit 1
cp $VFS_OUTPUTDIR/GVFS.Native.Mac/Build/Products/$CONFIGURATION/GVFS.VirtualFileSystemHook $VFS_PUBLISHDIR || exit 1

echo 'Copying Git installer to the output directory...'
$VFS_SCRIPTDIR/PublishGit.sh $GITPATH || exit 1

echo 'Running VFS for Git unit tests...'
$VFS_PUBLISHDIR/GVFS.UnitTests || exit 1
