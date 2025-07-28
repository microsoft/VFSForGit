@ECHO OFF
SETLOCAL

REM Determine the correct architecture for the installer
IF "%PROCESSOR_ARCHITECTURE%"=="AMD64" (
    SET GIT_ARCH=64-bit
) ELSE IF "%PROCESSOR_ARCHITECTURE%"=="ARM64" (
    SET GIT_ARCH=arm64
) ELSE (
    ECHO Unknown architecture: %PROCESSOR_ARCHITECTURE%
    exit 1
)

REM Lookup full paths to Git and VFS for Git installers
FOR /F "tokens=* USEBACKQ" %%F IN ( `where /R %~dp0 Git*-%GIT_ARCH%.exe` ) DO SET GIT_INSTALLER=%%F
FOR /F "tokens=* USEBACKQ" %%F IN ( `where /R %~dp0 SetupGVFS*.exe` ) DO SET GVFS_INSTALLER=%%F

REM Create new empty directory for logs
SET LOGDIR=%~dp0\logs
IF EXIST %LOGDIR% (
    rmdir /S /Q %LOGDIR%
)
mkdir %LOGDIR%

ECHO Installing Git (%GIT_ARCH%)...
%GIT_INSTALLER% /LOG="%LOGDIR%\git.log" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ALLOWDOWNGRADE=1

ECHO Installing VFS for Git...
%GVFS_INSTALLER% /LOG="%LOGDIR%\gvfs.log" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART  /DIR="C:\Program Files\VFS for Git"
