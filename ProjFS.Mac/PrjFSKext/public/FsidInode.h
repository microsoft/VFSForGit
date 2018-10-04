//
//  FsidInode.h
//  PrjFSKext
//
//  Created by William Baker on 10/4/18.
//  Copyright © 2018 GVFS. All rights reserved.
//

#ifndef FsidInode_h
#define FsidInode_h

#include <sys/_types/_fsid_t.h>

struct FsidInode
{
    fsid_t fsid;
    uint64_t inode;
};

#endif /* FsidInode_h */
