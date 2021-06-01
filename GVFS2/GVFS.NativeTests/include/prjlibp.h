// prjlibp.h
//
// Contains a subset of the contents of the PrjLib header files:


#pragma once

#define GV_FLAG_IMMUTABLE                 0x00000001
#define GV_FLAG_DIRTY                     0x00000002
#define GV_FLAG_FULLY_POPULATED           0x00000004
#define GV_FLAG_SAW_PRE_RENAME            0x00000008
#define PRJ_FLAG_VIRTUALIZATION_ROOT      0x00000010
#define GV_FLAG_FULL_DATA                 0x00000020

//
// Length of ContentID and EpochID in bytes
//

#define PRJ_PLACEHOLDER_ID_LENGTH      128
//
// Structure that uniquely identifies the version of the attributes, file streams etc for a placeholder file
//

typedef struct _PRJ_PLACEHOLDER_VERSION_INFO {

    UCHAR                               ProviderID[PRJ_PLACEHOLDER_ID_LENGTH];

    UCHAR                               ContentID[PRJ_PLACEHOLDER_ID_LENGTH];

} PRJ_PLACEHOLDER_VERSION_INFO, *PPRJ_PLACEHOLDER_VERSION_INFO;


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

	PRJ_PLACEHOLDER_VERSION_INFO versionInfo;

    //
    // Virtual (i.e. relative to the Virtualization Instance root) name of the fully expanded file
    // The name does not include trailing zero
    // The length is in bytes
    //
    USHORT NameLength;

    WCHAR Name[ANYSIZE_ARRAY];

} GV_REPARSE_INFO, *PGV_REPARSE_INFO;