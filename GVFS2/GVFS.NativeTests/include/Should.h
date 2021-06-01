#pragma once

#include "TestException.h"

#define STRINGIFY(X) #X
#define EXPAND_AND_STRINGIFY(X) STRINGIFY(X)

#define SHOULD_BE_TRUE(expr)                                                                                  \
do {                                                                                                          \
    if(!(expr))                                                                                               \
    {                                                                                                         \
        if(IsDebuggerPresent())                                                                               \
        {                                                                                                     \
            assert(expr);                                                                                     \
        }                                                                                                     \
        throw TestException("Failure on line:" EXPAND_AND_STRINGIFY(__LINE__) ", in function:" __FUNCTION__); \
    }                                                                                                         \
} while (0)

#define SHOULD_EQUAL(P1, P2) SHOULD_BE_TRUE((P1) == (P2))
#define SHOULD_NOT_EQUAL(P1, P2) SHOULD_BE_TRUE((P1) != (P2))

#define FAIL_TEST(msg)                                  \
do {                                                    \
    if (IsDebuggerPresent())                            \
    {                                                   \
        assert(false);                                  \
    }                                                   \
    throw TestException(msg);                           \
} while (0)
