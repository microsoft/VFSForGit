SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})
SRCDIR=$SCRIPTDIR/../..
$SRCDIR/ProjFS.Mac/Scripts/UnloadPrjFSKext.sh

sudo rm -r /GVFS.FT

PATURL=$1
if [[ -z $PATURL ]] ; then
    exit 1
fi
security delete-generic-password -s "gcm4ml:git:$PATURL"