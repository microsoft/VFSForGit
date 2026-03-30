@ECHO OFF
SETLOCAL

REM Lookup full path to VFS for Git installer
FOR /F "tokens=* USEBACKQ" %%F IN ( `where /R %~dp0 SetupGVFS*.exe` ) DO SET GVFS_INSTALLER=%%F

REM Create new empty directory for logs
SET LOGDIR=%~dp0\logs
IF EXIST %LOGDIR% (
    rmdir /S /Q %LOGDIR%
)
mkdir %LOGDIR%

ECHO Installing VFS for Git...
%GVFS_INSTALLER% /LOG="%LOGDIR%\gvfs.log" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="C:\Program Files\VFS for Git"
