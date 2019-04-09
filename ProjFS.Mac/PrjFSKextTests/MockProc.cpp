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
    return NULL;
}
