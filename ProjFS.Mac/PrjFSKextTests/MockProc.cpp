#include "MockProc.hpp"
#include <string>

static std::string s_procName;

int proc_pid(proc_t)
{
    return 1;
}

void proc_name(int pid, char* buf, int size)
{
    strlcpy(buf, s_procName.c_str(), size);
}

void SetProcName(const std::string& procName)
{
    s_procName = procName;
}

proc_t vfs_context_proc(vfs_context_t ctx)
{
    return nullptr;
}

proc_t proc_self(void)
{
    return nullptr;
}

kauth_cred_t kauth_cred_proc_ref(proc_t procp)
{
    return nullptr;
}

uid_t kauth_cred_getuid(kauth_cred_t _cred)
{
    // Values over 500 are non-system processes
    return 501;
}

void kauth_cred_unref(kauth_cred_t *_cred)
{
}

int proc_ppid(proc_t)
{
    return 1;
}

proc_t proc_find(int pid)
{
    return nullptr;
}

int proc_rele(proc_t p)
{
   return 1;
}

int proc_selfpid(void)
{
    return 1;
}
