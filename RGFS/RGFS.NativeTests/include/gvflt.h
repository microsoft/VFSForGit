// gvflt.h
//
// Contains a subset of the contents of:
// sdktools\CoreBuild\GVFlt\gvflt.h

#pragma once

#define GV_FLAG_IMMUTABLE                 0x00000001
#define GV_FLAG_DIRTY                     0x00000002
#define GV_FLAG_FULLY_POPULATED           0x00000004
#define GV_FLAG_RENAMED                   0x00000008
#define GV_FLAG_VIRTUALIZATION_ROOT       0x00000010
#define GV_FLAG_FULL_DATA                 0x00000020
#define GV_FLAG_PLACEHOLDER_AUTHORITATIVE 0x00000100

//
// Length of ContentID and EpochID in bytes
//

#define GVFLT_PLACEHOLDER_ID_LENGTH      128
//
// Structure that uniquely identifies the version of the attributes, file streams etc for a placeholder file
//

typedef struct _GVFLT_PLACEHOLDER_VERSION_INFO {

    UCHAR                               EpochID[GVFLT_PLACEHOLDER_ID_LENGTH];

    UCHAR                               ContentID[GVFLT_PLACEHOLDER_ID_LENGTH];

} GVFLT_PLACEHOLDER_VERSION_INFO, *PGVFLT_PLACEHOLDER_VERSION_INFO;


//
// Data written into on-disk reparse point
//

typedef struct _GV_REPARSE_INFO {

    //
    //  Version of this struct for future app compat issues
    //

    DWORD Version;

    //
    // Additional flags
    //

    ULONG Flags;

    //
    //  ID of the Virtualization Instance associated with the Virtualization Root that contains this reparse point
    //

    GUID VirtualizationInstanceID;

    //
    // Version info for the placeholder file
    //

    GVFLT_PLACEHOLDER_VERSION_INFO versionInfo;

    //
    // Virtual (i.e. relative to the Virtualization Instance root) name of the fully expanded file
    // The name does not include trailing zero
    // The length is in bytes
    //
    USHORT NameLength;

    WCHAR Name[ANYSIZE_ARRAY];

} GV_REPARSE_INFO, *PGV_REPARSE_INFO;