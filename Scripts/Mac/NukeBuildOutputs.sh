#!/bin/bash

SCRIPTDIR=$(dirname ${BASH_SOURCE[0]})

SRCDIR=$SCRIPTDIR/../..
ROOTDIR=$SRCDIR/..

sudo rm -Rf $ROOTDIR/BuildOutput
rm -Rf $ROOTDIR/packages
rm -Rf $ROOTDIR/Publish

echo git --work-tree=$SRCDIR clean -Xdf -n
git --work-tree=$SRCDIR clean -Xdf -n