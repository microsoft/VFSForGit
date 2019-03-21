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
    proc_t proc_self(void);
    kauth_cred_t kauth_cred_proc_ref(proc_t procp);
    uid_t kauth_cred_getuid(kauth_cred_t _cred);
    void kauth_cred_unref(kauth_cred_t *_cred);
    int proc_ppid(proc_t);
    proc_t proc_find(int pid);
    int proc_rele(proc_t p);
    int proc_selfpid(void);
}

void SetProcName(const std::string& procName);
