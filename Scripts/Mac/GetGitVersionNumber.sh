SCRIPTDIR="$(dirname ${BASH_SOURCE[0]})"
GVFSPROPS=$SCRIPTDIR/../../GVFS/GVFS.Build/GVFS.props
GITVERSION="$(cat $GVFSPROPS | grep GitPackageVersion | grep -Eo '[0-9.]+(-\w+)?')"
echo $GITVERSION