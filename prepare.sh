#!/bin/bash

set -ex

mount | grep GVFSTest/src && sudo umount /home/kivikakk/GVFSTest/src
rm -rf ~/GVFSTest
Scripts/Linux/GVFS_Clone.sh https://github.com/Microsoft/VFSForGit.git
setfattr -n user.projection.empty -v y ~/GVFSTest/.gvfs/lower
