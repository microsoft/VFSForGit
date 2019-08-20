#pragma once


// Xattrs used by both kernel and user component

// TODO: Issue #584, update xattr names
#define PrjFSVirtualizationRootXAttrName "org.vfsforgit.xattr.virtualizationroot"
#define PrjFSFileXAttrName "org.vfsforgit.xattr.file"
#define PrjFSDirectoryXAttrName "org.vfsforgit.xattr.directory"

#define PrjFS_PlaceholderIdLength 128

static const int32_t PlaceholderMagicNumber = 0x12345678;
static const int32_t PlaceholderFormatVersion = 1;

struct PrjFSXattrHeader
{
    int32_t magicNumber;
    int32_t formatVersion;
};

struct PrjFSVirtualizationRootXAttrData
{
    PrjFSXattrHeader header;
};

struct PrjFSFileXAttrData
{
    PrjFSXattrHeader header;
    
    unsigned char providerId[PrjFS_PlaceholderIdLength];
    unsigned char contentId[PrjFS_PlaceholderIdLength];
};

struct PrjFSDirectoryXAttrData
{
    PrjFSXattrHeader header;

    unsigned char providerId[PrjFS_PlaceholderIdLength];
};
