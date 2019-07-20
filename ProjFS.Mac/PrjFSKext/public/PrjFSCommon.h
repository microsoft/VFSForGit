#ifndef PrjFSCommon_h
#define PrjFSCommon_h

#include "PrjFSVersion.h"
#include <stdint.h>
#include <stdbool.h>

#define PrjFSMaxPath            1024
#define PrjFSKextBundleId       "org.vfsforgit.PrjFSKext"

#define PrjFSServiceClass       "org_vfsforgit_PrjFS"

#define PrjFSKextVersion PrjFSVersionString
// Name of property on the main PrjFS IOService indicating the kext version, to be checked by user space
#define PrjFSKextVersionKey "org.vfsforgit.PrjFSKext.Version"

#define PrjFSProviderPathKey "org.vfsforgit.PrjFSKext.ProviderUserClient.Path"

typedef enum
{
    FileFlags_Invalid = 0,
    
    FileFlags_IsInVirtualizationRoot    = 0x00000008, // UF_OPAQUE
    FileFlags_IsEmpty                   = 0x00000010, // UF_NOUNLINK
    
} FileFlags;

// User client types to be passed to IOServiceOpen.
enum PrjFSServiceUserClientType
{
    UserClientType_Invalid = 0,
    
    UserClientType_Provider,
    UserClientType_Log,
};

// When building the kext in user space for unit testing, we want some functions
// to have external linkage in order to make them testable
#ifdef KEXT_UNIT_TESTING
#define KEXT_STATIC
#define KEXT_STATIC_INLINE
#else
#define KEXT_STATIC static
#define KEXT_STATIC_INLINE static inline
#endif

namespace PrjFSDarwinMajorVersion
{
    enum
    {
        MacOS10_13_HighSierra = 17,
        MacOS10_14_Mojave = 18,
        MacOS10_15_Catalina = 19,
    };
}

#endif /* PrjFSCommon_h */
