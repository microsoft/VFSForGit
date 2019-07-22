#include "stdafx.h"
#include "common.h"
#include "Upgrader.h"
#include "Shlobj.h"

PATH_STRING Upgrader_GetHighestAvailableVersionFilePath()
{
    PWSTR programDataPath;
    HRESULT getPathResult = SHGetKnownFolderPath(
        FOLDERID_ProgramData,  // rfid
        KF_FLAG_CREATE,        // dwFlags
        NULL,                  // hToken
        &programDataPath);

    if (SUCCEEDED(getPathResult))
    {
        wchar_t upgradeDirectory[MAX_PATH];
        _snwprintf_s(upgradeDirectory, _TRUNCATE, L"%s\\GVFS\\GVFS.Upgrade\\HighestAvailableVersion", programDataPath);
        CoTaskMemFree(programDataPath);
        return PATH_STRING(upgradeDirectory);
    }

    return PATH_STRING();
}