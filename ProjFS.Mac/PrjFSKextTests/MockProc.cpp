#include "MockProc.hpp"
#include <map>
#include <string>

struct thread
{
};

using std::make_pair;
using std::map;
using std::string;

static map<uintptr_t /*credential ID*/, int /*UID*/> s_credentialMap;
static map<vfs_context_t /*context*/, int /*pid*/> s_contextMap;
static map<int /*process Id*/, proc> s_processMap;
static int s_selfPid;
static uint16_t s_currentThreadIndex = 0;
static thread s_threadPool[MockProcess_ThreadPoolSize] = {};

void MockProcess_Reset()
{
    s_processMap.clear();
    s_credentialMap.clear();
    s_contextMap.clear();
    MockProcess_SetCurrentThreadIndex(0);
}

void MockProcess_SetSelfPid(int selfPid)
{
    s_selfPid = selfPid;
}

int proc_pid(proc_t proc)
{
    return proc->pid;
}

void proc_name(int pid, char* buf, int size)
{
    map<int /*process Id*/, proc>::const_iterator found = s_processMap.find(pid);
    if (found == s_processMap.end())
    {
        assert("pid not found in s_processMap");
    }
    else
    {
        strlcpy(buf, found->second.name.c_str(), size);
    }
}

proc_t vfs_context_proc(vfs_context_t ctx)
{
    map<vfs_context_t /*context*/, int /*pid*/>::const_iterator foundPidId = s_contextMap.find(ctx);
    if (foundPidId == s_contextMap.end())
    {
        assert("ctx not found in s_contextMap");
        return nullptr;
    }

    return proc_find(foundPidId->second);
}

int vfs_context_pid(vfs_context_t ctx)
{
    return proc_pid(vfs_context_proc(ctx));
}

proc_t proc_self(void)
{
    map<int /*process Id*/, proc>::iterator selfIter = s_processMap.find(s_selfPid);
    if (selfIter == s_processMap.end())
    {
        assert("pid not found in s_processMap");
        return nullptr;
    }
    else
    {
        return &(selfIter->second);
    }
}

kauth_cred_t kauth_cred_proc_ref(proc_t procp)
{
    map<int /*process Id*/, proc>::const_iterator found = s_processMap.find(procp->pid);
    if (found == s_processMap.end())
    {
        assert("pid not found in s_processMap");
        return nullptr;
    }
    else
    {
        return reinterpret_cast<kauth_cred_t>(found->second.credentialId);
    }
}

uid_t kauth_cred_getuid(kauth_cred_t _cred)
{
    map<uintptr_t /*credential ID*/, int /*UID*/>::const_iterator found = s_credentialMap.find(reinterpret_cast<uintptr_t>(_cred));
    if (found == s_credentialMap.end())
    {
        assert("uid not found in s_credentialMap");
        return -1;
    }
    else
    {
        return found->second;
    }
}

void kauth_cred_unref(kauth_cred_t *_cred)
{
}

int proc_ppid(proc_t p)
{
    map<int /*process Id*/, proc>::const_iterator found = s_processMap.find(p->pid);
    if (found == s_processMap.end())
    {
        assert("pid not found in s_processMap");
        return -1;
    }
    else
    {
        return found->second.ppid;
    }
}

proc_t proc_find(int pid)
{
    map<int /*process Id*/, proc>::iterator procIter = s_processMap.find(pid);
    if (procIter == s_processMap.end())
    {
        assert("pid not found in s_processMap");
        return nullptr;
    }
    else
    {
        return &(procIter->second);
    }
}

int proc_rele(proc_t p)
{
   return 1;
}

int proc_selfpid(void)
{
    return s_selfPid;
}

void MockProcess_AddCredential(uintptr_t credentialId, uid_t UID)
{
    s_credentialMap.insert(make_pair(credentialId, UID));
}

void MockProcess_AddContext(vfs_context_t contextId, int pid)
{
    s_contextMap.insert(make_pair(contextId, pid));
}

void MockProcess_AddProcess(int pid, uintptr_t credentialId, int ppid, string name)
{
    proc process = proc {pid, credentialId, ppid, name};
    s_processMap.insert(make_pair(pid, process));
}

kernel_thread_t current_thread()
{
    return &s_threadPool[s_currentThreadIndex];
}

void MockProcess_SetCurrentThreadIndex(uint16_t threadIndex)
{
    assert(threadIndex < MockProcess_ThreadPoolSize);
    s_currentThreadIndex = threadIndex;
}
