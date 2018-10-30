. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

BUILDDIR=$VFS_OUTPUTDIR/GVFS.Build
cp $VFS_SRCDIR/nuget.config $BUILDDIR
dotnet new classlib -n Restore.SharedDataQueueStallWorkaround -o $BUILDDIR --force
dotnet add $BUILDDIR/Restore.SharedDataQueueStallWorkaround.csproj package --package-directory $VFS_PACKAGESDIR SharedDataQueueStallWorkaround --version '1.0.0'

# DYLD_LIBRARY_PATH contains /usr/local/lib by default, so we'll copy this library there.
cp $VFS_PACKAGESDIR/shareddataqueuestallworkaround/1.0.0/libSharedDataQueue.dylib /usr/local/lib
