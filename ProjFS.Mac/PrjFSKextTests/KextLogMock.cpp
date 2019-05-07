#include "KextMockUtilities.hpp"
#include "KextLogMock.h"

const void* KextLog_Unslide(const void* pointer)
{
    return pointer;
}

void KextMessageLogged(KextLog_Level level)
{
}

void KextLog_Printf(KextLog_Level level, const char* format, ...)
{
    MockCalls::RecordFunctionCall(
        KextMessageLogged,
        level);
}

