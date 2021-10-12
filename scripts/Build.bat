@ECHO OFF
CALL "%~dp0\InitializeEnvironment.bat" || EXIT /b 10
SETLOCAL
SETLOCAL EnableDelayedExpansion

IF "%~1"=="" (
    SET CONFIGURATION=Debug
) ELSE (
    SET CONFIGURATION=%1
)

IF "%~2"=="" (
    SET GVFSVERSION=0.2.173.2
) ELSE (
    SET GVFSVERSION=%2
)

IF "%~3"=="" (
    SET VERBOSITY=minimal
) ELSE (
    SET VERBOSITY=%3
)

REM If we have MSBuild on the PATH then go straight to the build phase
FOR /F "tokens=* USEBACKQ" %%F IN (`where msbuild.exe`) DO (
    SET MSBUILD_EXEC=%%F
    ECHO INFO: Found msbuild.exe at '%%F'
    GOTO :BUILD
)

:LOCATE_MSBUILD
REM Locate MSBuild via the vswhere tool
FOR /F "tokens=* USEBACKQ" %%F IN (`where nuget.exe`) DO (
    SET NUGET_EXEC=%%F
    ECHO INFO: Found nuget.exe at '%%F'
)

REM NuGet is required to be on the PATH to install vswhere
IF NOT EXIST "%NUGET_EXEC%" (
    ECHO ERROR: Could not find nuget.exe on the PATH
    EXIT /B 10
)

REM Acquire vswhere to find VS installations reliably
SET VSWHERE_VER=2.6.7
"%NUGET_EXEC%" install vswhere -Version %VSWHERE_VER% || exit /b 1
SET VSWHERE_EXEC="%VFS_PACKAGESDIR%\vswhere.%VSWHERE_VER%\tools\vswhere.exe"

REM Assumes default installation location for Windows 10 SDKs
IF NOT EXIST "C:\Program Files (x86)\Windows Kits\10\Include\10.0.16299.0" (
    ECHO ERROR: Could not find Windows 10 SDK Version 16299
    EXIT /B 1
)

REM Use vswhere to find the latest VS installation with the MSBuild component
REM See https://github.com/Microsoft/vswhere/wiki/Find-MSBuild
FOR /F "tokens=* USEBACKQ" %%F IN (`%VSWHERE_EXEC% -all -prerelease -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\amd64\MSBuild.exe`) DO (
    SET MSBUILD_EXEC=%%F
    ECHO INFO: Found msbuild.exe at '%%F'
)

:BUILD
IF NOT DEFINED MSBUILD_EXEC (
  ECHO ERROR: Could not locate a Visual Studio installation with required components.
  ECHO Refer to Readme.md for a list of the required Visual Studio components.
  EXIT /B 10
)

ECHO ^**********************
ECHO ^* Restoring Packages *
ECHO ^**********************
"%MSBUILD_EXEC%" "%VFS_SRCDIR%\GVFS.sln" ^
        /t:Restore ^
        /v:%VERBOSITY% ^
        /p:Configuration=%CONFIGURATION% || GOTO ERROR

ECHO ^*********************
ECHO ^* Building Solution *
ECHO ^*********************
"%MSBUILD_EXEC%" "%VFS_SRCDIR%\GVFS.sln" ^
        /t:Build ^
        /v:%VERBOSITY% ^
        /p:Configuration=%CONFIGURATION% || GOTO ERROR

GOTO :EOF

:USAGE
ECHO usage: %~n0%~x0 [^<configuration^>] [^<version^>] [^<verbosity^>]
ECHO.
ECHO   configuration    Solution configuration (default: Debug).
ECHO   version          GVFS version (default: 0.2.173.2).
ECHO   verbosity        MSBuild verbosity (default: minimal).
ECHO.
EXIT 1

:ERROR
ECHO ERROR: Build failed with exit code %ERRORLEVEL%
EXIT /B %ERRORLEVEL%
