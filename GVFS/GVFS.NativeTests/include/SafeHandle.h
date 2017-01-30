#pragma once

// Wrapper for HANDLE that calls CloseHandle when destroyed
class SafeHandle
{
public:
    SafeHandle(HANDLE handle);
    ~SafeHandle();

    HANDLE GetHandle();
    void CloseHandle();

private:
    HANDLE handle;
};

inline SafeHandle::SafeHandle(HANDLE handle)
{
    this->handle = handle;
}

inline SafeHandle::~SafeHandle()
{
    if (this->handle != NULL)
    {
        this->CloseHandle();
    }
}

inline HANDLE SafeHandle::GetHandle()
{
    return this->handle;
}

inline void SafeHandle::CloseHandle()
{
    if (this->handle != NULL && this->handle != INVALID_HANDLE_VALUE)
    {
        ::CloseHandle(this->handle);
        this->handle = NULL;
    }
}