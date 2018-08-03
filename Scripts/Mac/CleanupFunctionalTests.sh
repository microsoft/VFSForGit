PATURL=$1
if [[ -z $PATURL ]] ; then
    exit 1
fi
security delete-generic-password -s "gcm4ml:git:$PATURL"
