#ifndef FsidInode_h
#define FsidInode_h

#include <sys/_types/_fsid_t.h>
#include <stdint.h>

struct FsidInode
{
    fsid_t fsid;
    uint64_t inode;
};

#endif /* FsidInode_h */
