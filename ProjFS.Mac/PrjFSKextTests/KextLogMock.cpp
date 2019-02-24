#include "KextLogMock.h"

const void* KextLog_Unslide(const void* pointer)
{
    return pointer;
}

void KextLog_Printf(KextLog_Level level, const char* format, ...)
{
}

