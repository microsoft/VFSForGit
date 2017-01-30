#include "stdafx.h"
#include "PlaceholderUtils.h"
#include "SafeHandle.h"
#include "TestException.h"
#include "TestHelpers.h"
#include "TestVerifiers.h"
#include "Should.h"

using namespace TestHelpers;

bool PlaceHolderHasVersionInfo(const char* virtualPath, int version, const WCHAR* sha, const WCHAR* commit)
{
    try
    {
        std::string path(virtualPath);
        std::shared_ptr<GV_REPARSE_INFO> reparseInfo = GetReparseInfo(path);

        SHOULD_EQUAL(reparseInfo->versionInfo.EpochID[0], static_cast<UCHAR>(version));

        SHOULD_EQUAL(std::wstring(sha), std::wstring(static_cast<WCHAR*>(static_cast<void*>(reparseInfo->versionInfo.ContentID))));

        WCHAR* epoch = static_cast<WCHAR*>(static_cast<void*>(reparseInfo->versionInfo.EpochID + 4));
        SHOULD_EQUAL(std::wstring(commit), std::wstring(epoch));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;

}
