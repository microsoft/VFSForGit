. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

GVFSPROPS=$VFS_SRCDIR/GVFS/GVFS.Build/GVFS.props
GITVERSION="$(cat $GVFSPROPS | grep GitPackageVersion | grep -Eo '[0-9.]+(-\w+)*')"
echo $GITVERSION
