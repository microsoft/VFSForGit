. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

BUILDDIR=$VFS_OUTPUTDIR/GVFS.Build
cp $VFS_SRCDIR/nuget.config $BUILDDIR
dotnet new classlib -n GVFS.Restore -o $BUILDDIR --force
dotnet add $BUILDDIR/GVFS.Restore.csproj package --package-directory $VFS_PACKAGESDIR SharedDataQueueDylib --version '1.0.0'

# DYLD_LIBRARY_PATH contains /usr/local/lib by default, so we'll copy this library there.
cp $VFS_PACKAGESDIR/shareddataqueuedylib/1.0.0/libSharedDataQueue.dylib /usr/local/lib