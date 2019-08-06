#pragma once

inline bool GVFSEnvironment_IsUnattended()
{
    char unattendedEnvVariable[2056];
    size_t requiredSize;
    if (getenv_s(&requiredSize, unattendedEnvVariable, "GVFS_UNATTENDED") == 0)
    {
        return 0 == strcmp(unattendedEnvVariable, "1");
    }

    return false;
}