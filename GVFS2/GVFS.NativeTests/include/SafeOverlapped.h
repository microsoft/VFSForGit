#pragma once

// Wrapper for OVERLAPPED that calls CloseHandle on the OVERLAPPED's hEvent when destroyed
struct SafeOverlapped
{
    SafeOverlapped();
    ~SafeOverlapped();

    OVERLAPPED overlapped;
};

inline SafeOverlapped::SafeOverlapped()
{
    memset(&this->overlapped, 0, sizeof(OVERLAPPED));
}

inline SafeOverlapped::~SafeOverlapped()
{
    if (this->overlapped.hEvent != NULL)
    {
        CloseHandle(this->overlapped.hEvent);
    }
}