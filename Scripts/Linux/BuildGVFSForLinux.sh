#!/bin/bash

. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

VERSION=$2
if [ -z $VERSION ]; then
  VERSION="0.2.173.2"
fi

if [ ! -d $VFS_OUTPUTDIR ]; then
  mkdir $VFS_OUTPUTDIR
fi

ARCH=$(uname -m)
if test "$ARCH" != "x86_64"; then
  >&2 echo "architecture must be x86_64 for struct stat; stopping"
  exit 1
fi

CC=${CC:-cc}

echo 'main(){int i=1;const char *n="n";struct stat b;i=__xstat64(i,n,&b);}' | \
  cc -xc -include sys/stat.h -o /dev/null - 2>/dev/null

if test $? != 0; then
  >&2 echo "__xstat64() not found in libc ABI; stopping"
  exit 1
fi

echo 'Building Linux libraries...'
$VFS_SRCDIR/ProjFS.Linux/Scripts/Build.sh $CONFIGURATION || exit 1

# Create the directory where we'll do pre build tasks
BUILDDIR=$VFS_OUTPUTDIR/GVFS.Build
if [ ! -d $BUILDDIR ]; then
  mkdir $BUILDDIR || exit 1
fi

echo 'Downloading a VFS-enabled version of Git...'
$VFS_SCRIPTDIR/DownloadGVFSGit.sh || exit 1
GITVERSION="$($VFS_SCRIPTDIR/GetGitVersionNumber.sh)"
GITPATH="$(find $VFS_PACKAGESDIR/gitforlinux.gvfs.installer/$GITVERSION -type f -name *.deb)" || exit 1
echo "Downloaded Git $GITVERSION"
# Now that we have a path containing the version number, generate GVFSConstants.GitVersion.cs
$VFS_SCRIPTDIR/GenerateGitVersionConstants.sh "$GITPATH" $BUILDDIR || exit 1

# If we're building the Profiling(Release) configuration, remove Profiling() for building .NET code
if [ "$CONFIGURATION" == "Profiling(Release)" ]; then
  CONFIGURATION=Release
fi

echo "Generating CommonAssemblyVersion.cs as $VERSION..."
$VFS_SCRIPTDIR/GenerateCommonAssemblyVersion.sh $VERSION || exit 1

echo 'Restoring packages...'
dotnet restore $VFS_SRCDIR/GVFS.sln /p:Configuration=$CONFIGURATION.Linux --packages $VFS_PACKAGESDIR /warnasmessage:MSB4011 || exit 1
dotnet build $VFS_SRCDIR/GVFS.sln --runtime linux-x64 --framework netcoreapp2.1 --configuration $CONFIGURATION.Linux -p:CopyPrjFS=true /maxcpucount:1 /warnasmessage:MSB4011 || exit 1

# build and copy native hook programs
if [ ! -d "$VFS_PUBLISHDIR" ]; then
  mkdir -p "$VFS_PUBLISHDIR" || exit 1
fi

HOOK_BUILDDIR="$VFS_OUTPUTDIR/GVFS.Native.Linux/Build/Products/$CONFIGURATION"
if [ ! -d "$HOOK_BUILDDIR" ]; then
  mkdir -p "$HOOK_BUILDDIR" || exit 1
fi

echo 'Building and copying native binaries to Publish directory...'
for hook in PostIndexChanged ReadObject VirtualFileSystem; do
  hook="GVFS.${hook}Hook"
  rm -rf "$HOOK_BUILDDIR/$hook"
  meson "$HOOK_BUILDDIR/$hook" "$VFS_SRCDIR/GVFS/$hook" || exit 1
  ninja -C "$HOOK_BUILDDIR/$hook" || exit 1
  cp "$HOOK_BUILDDIR/$hook/$hook" "$VFS_PUBLISHDIR" || exit 1
done

# Publish after native build, so installer package can include the native binaries.
dotnet publish $VFS_SRCDIR/GVFS.sln /p:Configuration=$CONFIGURATION.Linux /p:Platform=x64 -p:CopyPrjFS=true --runtime linux-x64 --framework netcoreapp2.1 --self-contained --output $VFS_PUBLISHDIR /maxcpucount:1 /warnasmessage:MSB4011 || exit 1

echo 'Copying Git installer to the output directory...'
$VFS_SCRIPTDIR/PublishGit.sh $GITPATH || exit 1

echo 'Running VFS for Git unit tests...'
$VFS_PUBLISHDIR/GVFS.UnitTests || exit 1
