@ECHO OFF
REM ==========================================================================
REM RunFunctionalTests-Dev.bat
REM
REM Runs GVFS functional tests using build output from out\ instead of
REM requiring a system-wide GVFS installation. The test service runs as a
REM background console process instead of a Windows service, so no admin
REM privileges are required.
REM
REM Usage: RunFunctionalTests-Dev.bat [configuration] [extra args...]
REM   configuration  - Debug (default) or Release
REM   extra args     - Passed through to GVFS.FunctionalTests.exe
REM                    (e.g. --test=WorktreeTests --ci)
REM ==========================================================================
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

IF "%1"=="" (SET "CONFIGURATION=Debug") ELSE (SET "CONFIGURATION=%1")

REM Enable dev mode so the test harness uses console service + build output paths
SET GVFS_FUNCTIONAL_TEST_DEV_MODE=1

REM Point Settings.cs at the build output directory
SET GVFS_DEV_OUT_DIR=%VFS_OUTDIR%
SET GVFS_DEV_CONFIGURATION=%CONFIGURATION%

REM Redirect service data directories to a user-writable temp location
SET GVFS_TEST_DATA=%TEMP%\GVFS-FunctionalTest
SET GVFS_COMMON_APPDATA_ROOT=%GVFS_TEST_DATA%\AppData
SET GVFS_SECURE_DATA_ROOT=%GVFS_TEST_DATA%\ProgramData

REM Put the build output gvfs.exe on PATH so 'where gvfs' finds it
SETLOCAL
SET PATH=%VFS_OUTDIR%\GVFS.Payload\bin\%CONFIGURATION%\win-x64;C:\Program Files\Git\cmd;%PATH%

ECHO ============================================
ECHO GVFS Functional Tests - Dev Mode (no admin)
ECHO ============================================
ECHO Configuration:       %CONFIGURATION%
ECHO Build output:        %VFS_OUTDIR%
ECHO Test data:           %GVFS_TEST_DATA%
ECHO.

ECHO gvfs location:
where gvfs
IF NOT %ERRORLEVEL% == 0 (
    ECHO error: unable to locate gvfs on the PATH. Has the solution been built?
    EXIT /b 1
)

ECHO git location:
where git
IF NOT %ERRORLEVEL% == 0 (
    ECHO error: unable to locate git on the PATH
    EXIT /b 1
)

%VFS_OUTDIR%\GVFS.FunctionalTests\bin\%CONFIGURATION%\net471\win-x64\GVFS.FunctionalTests.exe /result:TestResult.xml %2 %3 %4 %5

SET error=%ERRORLEVEL%

REM Clean up any orphaned test service process
tasklist /FI "IMAGENAME eq GVFS.Service.exe" /FI "WINDOWTITLE eq N/A" 2>NUL | findstr /I "GVFS.Service" >NUL
IF %ERRORLEVEL% == 0 (
    ECHO Cleaning up orphaned test service processes...
    REM The test harness should have stopped it, but just in case
)

EXIT /b %error%
