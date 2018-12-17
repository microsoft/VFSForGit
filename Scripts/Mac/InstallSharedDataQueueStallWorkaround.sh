. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

# DYLD_LIBRARY_PATH contains /usr/local/lib by default, so we'll copy this library there.
BUILDDIR=$VFS_OUTPUTDIR/GVFS.Build
cp $BUILDDIR/libSharedDataQueue.dylib /usr/local/lib
