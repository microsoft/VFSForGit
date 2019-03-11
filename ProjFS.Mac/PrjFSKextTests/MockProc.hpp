#include "../PrjFSKext/kernel-header-wrappers/vnode.h"
#include <string>

enum
{
    KAUTH_RESULT_ALLOW = 1,
    KAUTH_RESULT_DENY,
    KAUTH_RESULT_DEFER
};

// Kernel proc functions that are being mocked
extern "C"
{
    int proc_pid(proc_t);
    void proc_name(int pid, char* buf, int size);
    proc_t vfs_context_proc(vfs_context_t ctx);
}

void SetProcName(const std::string& procName);
