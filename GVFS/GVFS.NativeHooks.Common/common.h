#pragma once

#include <cstring>
#include <string>

#if defined(__APPLE__) || defined(__gnu_linux__)
typedef std::string PATH_STRING;
typedef int PIPE_HANDLE;
#define PRINTF_FMT(X, Y) __attribute__((__format__ (printf, X, Y)))
#elif defined(_WIN32)
typedef std::wstring PATH_STRING;
typedef HANDLE PIPE_HANDLE;
#define PRINTF_FMT(X, Y)
#else
#error Unsupported platform
#endif

#ifdef __gnu_linux__
#define DOT_GVFS_ROOT ".vfsforgit"
#else
#define DOT_GVFS_ROOT ".gvfs"
#endif

#if __cplusplus <  201103L
  #error The hooks require at least C++11 support
#endif

enum ReturnCode
{
	Success = 0,
	InvalidArgCount = 1,
	GetCurrentDirectoryFailure = 2,
	NotInGVFSEnlistment = 3,
	PipeConnectError = 4,
	PipeConnectTimeout = 5,
	InvalidSHA = 6,
	PipeWriteFailed = 7,
	PipeReadFailed = 8,
	FailureToDownload = 9,
	PathNameError = 10,

	LastError = PathNameError,	
};

void die(int err, const char *fmt, ...) PRINTF_FMT(2,3);
inline void die(int err, const char *fmt, ...)
{
	va_list params;
	va_start(params, fmt);
	vfprintf(stderr, fmt, params);
	va_end(params);
	exit(err);
}

PATH_STRING GetFinalPathName(const PATH_STRING& path);
PATH_STRING GetGVFSPipeName(const char *appName);
PIPE_HANDLE CreatePipeToGVFS(const PATH_STRING& pipeName);
void DisableCRLFTranslationOnStdPipes();

bool WriteToPipe(
    PIPE_HANDLE pipe, 
    const char* message, 
    unsigned long messageLength, 
    /* out */ unsigned long* bytesWritten, 
    /* out */ int* error);

bool ReadFromPipe(
    PIPE_HANDLE pipe, 
    char* buffer, 
    unsigned long bufferLength, 
    /* out */ unsigned long* bytesRead, 
    /* out */ int* error);
