#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})
BUILDDIR=$SCRIPTDIR/../../../BuildOutput/ProjFS.Mac/Native/$CONFIGURATION

# Copy the kext, because we have to chown it, which will cause a subsequent build
# to fail when trying to overwrite the kext. Instead we chown the copy and install that. 
sudo rm -Rf $BUILDDIR/debug.PrjFSKext.kext 
cp -R $BUILDDIR/PrjFSKext.kext $BUILDDIR/debug.PrjFSKext.kext
sudo chown -R root:wheel $BUILDDIR/debug.PrjFSKext.kext

kextstat | grep PrjFSKext
if [ $? -eq 0 ]; then
  sudo kextunload -b org.vfsforgit.PrjFSKext 
fi

mkdir -p $BUILDDIR/Symbols
sudo kextutil $BUILDDIR/debug.PrjFSKext.kext -symbols $BUILDDIR/Symbols
if [ $? -ne 0 ]; then
  echo
  echo The kext is invalid, check the output of kextutil
  exit $?    
fi

kextstat | grep PrjFSKext
if [ $? -ne 0 ]; then
  echo
  echo The kext claimed to load, but wasn’t found by kextstat
  exit $?    
fi

echo
echo PrjFSKext is now loaded! 
