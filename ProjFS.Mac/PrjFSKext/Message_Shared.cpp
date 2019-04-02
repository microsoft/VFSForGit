#include "public/Message.h"

#ifdef KERNEL
#include <kern/assert.h>
#else
#include <cassert>
#endif

uint32_t Message_EncodedSize(const MessageHeader* messageHeader)
{
    uint32_t size = sizeof(*messageHeader);
    for (unsigned i = 0; i < MessagePath_Count; ++i)
    {
        bool overflow = __builtin_uadd_overflow(messageHeader->pathSizesBytes[i], size, &size);
        assert(!overflow); // small constant plus small number of uint16_t should never overflow a uint32_t
    }
    return size;
}
