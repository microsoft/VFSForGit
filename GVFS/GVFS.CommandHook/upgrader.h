#pragma once
#include "common.h"
#include "FileSystem.h"

PATH_STRING Upgrader_GetHighestAvailableVersionFilePath();

inline bool Upgrader_IsLocalUpgradeAvailable()
{
    PATH_STRING highestVersionFilePath(Upgrader_GetHighestAvailableVersionFilePath());
    if (!highestVersionFilePath.empty())
    {
        return FileSystem_FileExists(highestVersionFilePath);
    }

    return false;
}