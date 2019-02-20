SCRIPTDIR="$(dirname ${BASH_SOURCE[0]})"

# convert to an absolute path because it is required by `dotnet publish`
pushd $SCRIPTDIR &>/dev/null
export VFS_SCRIPTDIR="$(pwd)"
popd &>/dev/null

export VFS_SRCDIR=$VFS_SCRIPTDIR/../..

VFS_ENLISTMENTDIR=$VFS_SRCDIR/..
export VFS_OUTPUTDIR=$VFS_ENLISTMENTDIR/BuildOutput
export VFS_PUBLISHDIR=$VFS_ENLISTMENTDIR/Publish
export VFS_PACKAGESDIR=$VFS_ENLISTMENTDIR/packages
