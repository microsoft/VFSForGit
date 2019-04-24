#!/bin/bash

CONFIGURATION=$1
if [ -z $CONFIGURATION ]; then
  CONFIGURATION=Debug
fi

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})

SRCDIR=$SCRIPTDIR/../../..
ROOTDIR=$SRCDIR/..
SLN=$SRCDIR/MirrorProvider/MirrorProvider.sln

ARCH=$(uname -m)
if test "$ARCH" != "x86_64"; then
  >&2 echo "architecture must be x86_64 for struct stat; stopping"
  exit 1
fi

CC=${CC:-cc}

echo 'main(){int i=1;const char *n="n";struct stat b;i=__lxstat64(i,n,&b);}' | \
  cc -xc -include sys/stat.h -o /dev/null - 2>/dev/null

if test $? != 0; then
  >&2 echo "__lxstat64() not found in libc ABI; stopping"
  exit 1
fi

# Build the ProjFS library interface
$SRCDIR/ProjFS.Linux/Scripts/Build.sh $CONFIGURATION || exit 1

# If we're building the Profiling(Release) configuration, remove Profiling() for building .NET code
if [ "$CONFIGURATION" == "Profiling(Release)" ]; then
  CONFIGURATION=Release
fi

# Build the MirrorProvider
dotnet restore $SLN /p:Configuration="$CONFIGURATION.Linux" --packages $ROOTDIR/packages
dotnet build $SLN --configuration $CONFIGURATION.Linux

exit 0
