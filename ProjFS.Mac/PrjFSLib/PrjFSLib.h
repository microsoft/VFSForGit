#ifndef PrjFSLib_h
#define PrjFSLib_h

#include "../PrjFSKext/public/PrjFSXattrs.h"
#include <stdbool.h>

#define _In_
#define _Out_

typedef struct _PrjFS_FileHandle PrjFS_FileHandle;

typedef struct _PrjFS_Callbacks PrjFS_Callbacks;

typedef enum
{
    PrjFS_Result_Invalid                            = 0x00000000,
    
    PrjFS_Result_Success                            = 0x00000001,
    PrjFS_Result_Pending                            = 0x00000002,
    
    // Bugs in the caller
    PrjFS_Result_EInvalidArgs                       = 0x10000001,
    PrjFS_Result_EInvalidOperation                  = 0x10000002,
    PrjFS_Result_ENotSupported                      = 0x10000004,
    
    // Runtime errors
    PrjFS_Result_EDriverNotLoaded                   = 0x20000001,
    PrjFS_Result_EOutOfMemory                       = 0x20000002,
    PrjFS_Result_EFileNotFound                      = 0x20000004,
    PrjFS_Result_EPathNotFound                      = 0x20000008,
    PrjFS_Result_EAccessDenied                      = 0x20000010,
    PrjFS_Result_EInvalidHandle                     = 0x20000020,
    PrjFS_Result_EIOError                           = 0x20000040,
    PrjFS_Result_ENotAVirtualizationRoot            = 0x20000080,
    PrjFS_Result_EVirtualizationRootAlreadyExists   = 0x20000100,
    PrjFS_Result_EDirectoryNotEmpty                 = 0x20000200,
    PrjFS_Result_EVirtualizationInvalidOperation    = 0x20000400,
    
    PrjFS_Result_ENotYetImplemented                 = 0xFFFFFFFF,
    
} PrjFS_Result;

typedef enum
{
    PrjFS_NotificationType_Invalid                  = 0x00000000,
    
    PrjFS_NotificationType_None                     = 0x00000001,
    PrjFS_NotificationType_NewFileCreated           = 0x00000004,
    PrjFS_NotificationType_PreDelete                = 0x00000010,
    PrjFS_NotificationType_PreDeleteFromRename      = 0x00000011,
    PrjFS_NotificationType_FileRenamed              = 0x00000080,
    PrjFS_NotificationType_HardLinkCreated          = 0x00000100,
    PrjFS_NotificationType_PreConvertToFull         = 0x00001000,
    
    PrjFS_NotificationType_PreModify                = 0x10000001,
    PrjFS_NotificationType_FileModified             = 0x10000002,
    PrjFS_NotificationType_FileDeleted              = 0x10000004,

} PrjFS_NotificationType;

extern "C" PrjFS_Result PrjFS_StartVirtualizationInstance(
    _In_    const char*                             virtualizationRootFullPath,
    _In_    PrjFS_Callbacks                         callbacks,
    _In_    unsigned int                            poolThreadCount);

PrjFS_Result PrjFS_StopVirtualizationInstance();

extern "C" PrjFS_Result PrjFS_ConvertDirectoryToVirtualizationRoot(
    _In_    const char*                             virtualizationRootFullPath);

PrjFS_Result PrjFS_ConvertDirectoryToPlaceholder(
    _In_    const char*                             relativePath);

extern "C" PrjFS_Result PrjFS_WritePlaceholderDirectory(
    _In_    const char*                             relativePath);

extern "C" PrjFS_Result PrjFS_WritePlaceholderFile(
    _In_    const char*                             relativePath,
    _In_    unsigned char                           providerId[PrjFS_PlaceholderIdLength],
    _In_    unsigned char                           contentId[PrjFS_PlaceholderIdLength],
    _In_    uint16_t                                fileMode);

extern "C" PrjFS_Result PrjFS_WriteSymLink(
    _In_    const char*                             relativePath,
    _In_    const char*                             symLinkTarget);

extern "C" PrjFS_Result PrjFS_RegisterForOfflineIO();
extern "C" PrjFS_Result PrjFS_UnregisterForOfflineIO();

typedef enum
{
    PrjFS_UpdateType_Invalid                        = 0x00000000,
    
    PrjFS_UpdateType_AllowReadOnly                  = 0x00000020,
    
} PrjFS_UpdateType;

typedef enum
{
    PrjFS_UpdateFailureCause_Invalid                = 0x00000000,
    
    PrjFS_UpdateFailureCause_FullFile               = 0x00000002,
    PrjFS_UpdateFailureCause_ReadOnly               = 0x00000008,
    
} PrjFS_UpdateFailureCause;

extern "C" PrjFS_Result PrjFS_UpdatePlaceholderFileIfNeeded(
    _In_    const char*                             relativePath,
    _In_    unsigned char                           providerId[PrjFS_PlaceholderIdLength],
    _In_    unsigned char                           contentId[PrjFS_PlaceholderIdLength],
    _In_    uint16_t                                fileMode,
    _In_    PrjFS_UpdateType                        updateFlags,
    _Out_   PrjFS_UpdateFailureCause*               failureCause);

extern "C" PrjFS_Result PrjFS_ReplacePlaceholderFileWithSymLink(
    _In_    const char*                             relativePath,
    _In_    const char*                             symLinkTarget,
    _In_    PrjFS_UpdateType                        updateFlags,
    _Out_   PrjFS_UpdateFailureCause*               failureCause);

extern "C" PrjFS_Result PrjFS_DeleteFile(
    _In_    const char*                             relativePath,
    _In_    PrjFS_UpdateType                        updateFlags,
    _Out_   PrjFS_UpdateFailureCause*               failureCause);

extern "C" PrjFS_Result PrjFS_WriteFileContents(
    _In_    const PrjFS_FileHandle*                 fileHandle,
    _In_    const void*                             bytes,
    _In_    unsigned int                            byteCount);

typedef enum
{
    PrjFS_FileState_Invalid                         = 0x00000000,
    
    PrjFS_FileState_Placeholder                     = 0x00000001,
    PrjFS_FileState_HydratedPlaceholder             = 0x00000002,
    PrjFS_FileState_Full                            = 0x00000008,
    
} PrjFS_FileState;

PrjFS_Result PrjFS_GetOnDiskFileState(
    _In_    const char*                             fullPath,
    _Out_   unsigned int*                           fileState);

typedef PrjFS_Result (PrjFS_EnumerateDirectoryCallback)(
    _In_    unsigned long                           commandId,
    _In_    const char*                             relativePath,
    _In_    int                                     triggeringProcessId,
    _In_    const char*                             triggeringProcessName);

typedef PrjFS_Result (PrjFS_GetFileStreamCallback)(
    _In_    unsigned long                           commandId,
    _In_    const char*                             relativePath,
    _In_    unsigned char                           providerId[PrjFS_PlaceholderIdLength],
    _In_    unsigned char                           contentId[PrjFS_PlaceholderIdLength],
    _In_    int                                     triggeringProcessId,
    _In_    const char*                             triggeringProcessName,
                                                   
    _In_    const PrjFS_FileHandle*                 fileHandle);

typedef PrjFS_Result (PrjFS_NotifyOperationCallback)(
    _In_    unsigned long                           commandId,
    _In_    const char*                             relativePath,
    _In_    const char*                             relativeFromPath,
    _In_    unsigned char                           providerId[PrjFS_PlaceholderIdLength],
    _In_    unsigned char                           contentId[PrjFS_PlaceholderIdLength],
    _In_    int                                     triggeringProcessId,
    _In_    const char*                             triggeringProcessName,
                                                     
    _In_    bool                                    isDirectory,
    _In_    PrjFS_NotificationType                  notificationType,
    _In_    const char*                             destinationRelativePath);

typedef void (PrjFS_LogErrorCallback)(
    _In_    const char*                             errorMessage);

typedef void (PrjFS_LogWarningCallback)(
    _In_    const char*                             warningMessage);

typedef void (PrjFS_LogInfoCallback)(
    _In_    const char*                             infoMessage);

typedef struct _PrjFS_Callbacks
{
    _In_    PrjFS_EnumerateDirectoryCallback*       EnumerateDirectory;
    _In_    PrjFS_GetFileStreamCallback*            GetFileStream;
    _In_    PrjFS_NotifyOperationCallback*          NotifyOperation;
    _In_    PrjFS_LogErrorCallback*                 LogError;
    _In_    PrjFS_LogWarningCallback*               LogWarning;
    _In_    PrjFS_LogInfoCallback*                  LogInfo;

} PrjFS_Callbacks;

PrjFS_Result PrjFS_CompleteCommand(
    _In_    unsigned long                           commandId,
    _In_    PrjFS_Result                            result);

#endif /* PrjFSLib_h */
