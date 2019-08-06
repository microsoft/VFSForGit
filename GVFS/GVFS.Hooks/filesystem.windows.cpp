#include "stdafx.h"
#include "common.h"
#include "FileSystem.h"

bool FileSystem_FileExists(const PATH_STRING& path)
{
    DWORD attributes = GetFileAttributesW(path.c_str());
    return (attributes != INVALID_FILE_ATTRIBUTES) && ((attributes & FILE_ATTRIBUTE_DIRECTORY) == 0);
}