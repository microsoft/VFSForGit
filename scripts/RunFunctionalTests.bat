@ECHO OFF
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

IF "%1"=="" (SET "CONFIGURATION=Debug") ELSE (SET "CONFIGURATION=%1")

REM Ensure GVFS installation is on the PATH for the Functional Tests to find
SETLOCAL
SET PATH=C:\Program Files\VFS for Git\;C:\Program Files\GVFS;C:\Program Files\Git\cmd;%PATH%

ECHO PATH = %PATH%

ECHO gvfs location:
where gvfs
IF NOT %ERRORLEVEL% == 0 (
    ECHO error: unable to locate GVFS on the PATH (has it been installed?)
)

ECHO GVFS.Service location:
where GVFS.Service
IF NOT %ERRORLEVEL% == 0 (
    ECHO error: unable to locate GVFS.Service on the PATH (has it been installed?)
)

ECHO git location:
where git
IF NOT %ERRORLEVEL% == 0 (
    ECHO error: unable to locate Git on the PATH (has it been installed?)
)

%VFS_OUTDIR%\GVFS.FunctionalTests\bin\%CONFIGURATION%\net471\win-x64\GVFS.FunctionalTests.exe /result:TestResult.xml %2 %3 %4 %5

SET error=%ERRORLEVEL%
CALL %VFS_SCRIPTSDIR%\StopAllServices.bat
EXIT /b %error%
