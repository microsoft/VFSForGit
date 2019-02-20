#!/bin/bash

. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

if [ ! -d $VFS_OUTPUTDIR ]; then
  mkdir $VFS_OUTPUTDIR
fi

echo 'Building Linux libraries...'
$VFS_SRCDIR/ProjFS.Linux/Scripts/Build.sh $CONFIGURATION || exit 1

# Create the directory where we'll do pre build tasks
BUILDDIR=$VFS_OUTPUTDIR/GVFS.Build
if [ ! -d $BUILDDIR ]; then
  mkdir $BUILDDIR || exit 1
fi

# TODO(Linux): download VFS-enabled version of upstream Git
#echo 'Downloading a VFS-enabled version of Git...'
#$VFS_SCRIPTDIR/DownloadGVFSGit.sh || exit 1
GITVERSION="$($VFS_SCRIPTDIR/GetGitVersionNumber.sh)"
#GITPATH="$(find $VFS_PACKAGESDIR/gitformac.gvfs.installer/$GITVERSION -type f -name *.dmg)" || exit 1
#echo "Downloaded Git $GITVERSION"

# TODO(Linux): git version based on .dmg filename:
#   packages/gitformac.gvfs.installer/2.20190724.1/tools/git-2.22.0.vfs.1.1.49.ge8fe4ef9d1-intel-universal-snow-leopard.dmg
#   downloaded from https://vfsforgit.myget.org/F/build-dependencies/api/v3/flatcontainer/gitformac.gvfs.installer/2.20190724.1/gitformac.gvfs.installer.2.20190724.1.nupkg
GITPATH="2.22.0.vfs.1.1.49"
# Now that we have a path containing the version number, generate GVFSConstants.GitVersion.cs
$VFS_SCRIPTDIR/GenerateGitVersionConstants.sh "$GITPATH" $BUILDDIR || exit 1

# If we're building the Profiling(Release) configuration, remove Profiling() for building .NET code
if [ "$CONFIGURATION" == "Profiling(Release)" ]; then
  CONFIGURATION=Release
fi

echo 'Generating CommonAssemblyVersion.cs...'
$VFS_SCRIPTDIR/GenerateCommonAssemblyVersion.sh || exit 1

echo 'Restoring packages...'
dotnet restore $VFS_SRCDIR/GVFS.sln /p:Configuration=$CONFIGURATION.Linux --packages $VFS_PACKAGESDIR /warnasmessage:MSB4011 || exit 1
dotnet build $VFS_SRCDIR/GVFS.sln --runtime linux-x64 --framework netcoreapp2.1 --configuration $CONFIGURATION.Linux -p:CopyPrjFS=true /maxcpucount:1 /warnasmessage:MSB4011 || exit 1

# TODO(Linux): build native hook programs
NATIVEDIR=$VFS_SRCDIR/GVFS/GVFS.Native.Linux
#xcodebuild -configuration $CONFIGURATION -workspace $NATIVEDIR/GVFS.Native.Linux.xcworkspace build -scheme GVFS.Native.Linux -derivedDataPath $VFS_OUTPUTDIR/GVFS.Native.Linux || exit 1

if [ ! -d $VFS_PUBLISHDIR ]; then
  mkdir $VFS_PUBLISHDIR || exit 1
fi

# TODO(Linux): copy native hook programs
#echo 'Copying native binaries to Publish directory...'
#cp $VFS_OUTPUTDIR/GVFS.Native.Linux/Build/Products/$CONFIGURATION/GVFS.ReadObjectHook $VFS_PUBLISHDIR || exit 1
#cp $VFS_OUTPUTDIR/GVFS.Native.Linux/Build/Products/$CONFIGURATION/GVFS.VirtualFileSystemHook $VFS_PUBLISHDIR || exit 1
#cp $VFS_OUTPUTDIR/GVFS.Native.Linux/Build/Products/$CONFIGURATION/GVFS.PostIndexChangedHook $VFS_PUBLISHDIR || exit 1

# Publish after native build, so installer package can include the native binaries.
dotnet publish $VFS_SRCDIR/GVFS.sln /p:Configuration=$CONFIGURATION.Linux /p:Platform=x64 -p:CopyPrjFS=true --runtime linux-x64 --framework netcoreapp2.1 --self-contained --output $VFS_PUBLISHDIR /maxcpucount:1 /warnasmessage:MSB4011 || exit 1

# TODO(Linux): copy installer (if any)
#echo 'Copying Git installer to the output directory...'
#$VFS_SCRIPTDIR/PublishGit.sh $GITPATH || exit 1

echo 'Running VFS for Git unit tests...'
$VFS_PUBLISHDIR/GVFS.UnitTests || exit 1
