#include <sys/kernel_types.h>

static inline bool ActionBitIsSet(kauth_action_t action, kauth_action_t mask)
{
    return action & mask;
}

