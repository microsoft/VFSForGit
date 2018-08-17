#!/bin/bash

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})

CONFIGURATION=Debug
SRCDIR=$SCRIPTDIR/..
ROOTDIR=$SRCDIR/..
BUILDDIR=$ROOTDIR/BuildOutput/MirrorProvider.Mac/bin/$CONFIGURATION/x64/netcoreapp2.1

$SRCDIR/ProjFS.Mac/Scripts/Build.sh
$SRCDIR/ProjFS.Mac/Scripts/LoadPrjFSKext.sh

$SRCDIR/MirrorProvider/Scripts/Mac/Build.sh

mkdir ~/CachePoisonTest 
TESTDIR=~/CachePoisonTest/$1
rm -Rf $TESTDIR
mkdir $TESTDIR
mkdir $TESTDIR/Source
mkdir $TESTDIR/Source/A 
mkdir $TESTDIR/Source/A/B 
echo 1 > $TESTDIR/Source/A/1.txt
echo 2 > $TESTDIR/Source/A/B/2.txt 

dotnet $BUILDDIR/MirrorProvider.Mac.dll clone $TESTDIR/Source $TESTDIR/Target
sudo dotnet $BUILDDIR/MirrorProvider.Mac.dll mount $TESTDIR/Target

$SRCDIR/ProjFS.Mac/Scripts/UnloadPrjFSKext.sh
