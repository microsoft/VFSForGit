#!/bin/bash

. "$(dirname ${BASH_SOURCE[0]})/InitializeEnvironment.sh"

sudo rm -Rf $VFS_OUTPUTDIR
rm -Rf $VFS_PACKAGESDIR
rm -Rf $VFS_PUBLISHDIR

echo git --work-tree=$VFS_SRCDIR clean -Xdf -n
git --work-tree=$VFS_SRCDIR clean -Xdf -n
