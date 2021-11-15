@ECHO OFF
SETLOCAL

REM Lookup full paths to Git and VFS for Git installers
FOR /F "tokens=* USEBACKQ" %%F IN ( `where /R %~dp0 Git*.exe` ) DO SET GIT_INSTALLER=%%F
FOR /F "tokens=* USEBACKQ" %%F IN ( `where /R %~dp0 SetupGVFS*.exe` ) DO SET GVFS_INSTALLER=%%F

REM Create new empty directory for logs
SET LOGDIR=%~dp0\logs
IF EXIST %LOGDIR% (
    rmdir /S /Q %LOGDIR%
)
mkdir %LOGDIR%

ECHO Installing Git for Windows...
%GIT_INSTALLER% /LOG="%LOGDIR%\git.log" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ALLOWDOWNGRADE=1

ECHO Installing VFS for Git...
%GVFS_INSTALLER% /LOG="%LOGDIR%\gvfs.log" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART  /DIR="C:\Program Files\VFS for Git"
