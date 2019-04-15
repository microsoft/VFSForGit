#include "../PrjFSKext/kernel-header-wrappers/vnode.h"
#include <string>

// Kernel functions that are being mocked
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

struct proc {
    int pid;
    uintptr_t credentialId;
    int ppid;
    std::string name;
};

void MockProcess_SetSelfPid(int selfPid);
void MockProcess_AddCredential(uintptr_t credentialId, uid_t UID);
void MockProcess_AddContext(vfs_context_t context, int pid);
void MockProcess_AddProcess(int pid, uintptr_t credentialId, int ppid, std::string procName);
void MockProcess_Reset();
