#pragma once

#ifdef KEXT_UNIT_TESTING
#include <atomic>
#define _Atomic(X) std::atomic< X >
using std::atomic_uint_least32_t;
using std::atomic_uint_least64_t;
using std::atomic_int;
using std::memory_order_seq_cst;
using std::atomic_exchange;
using std::atomic_fetch_add;
#else
#include <stdatomic.h>
#endif
