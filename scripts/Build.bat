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

REM .NET 10 SDK ships MSBuild 18.x; VS 2022 ships MSBuild 17.x.
REM Managed (csproj) projects require MSBuild 18.x via "dotnet build".
REM Native C++ (vcxproj) projects require VS MSBuild with VC++ targets.

ECHO ^**********************
ECHO ^* Restoring Packages *
ECHO ^**********************
dotnet restore "%VFS_SRCDIR%\GVFS.sln" ^
        /v:%VERBOSITY% ^
        /p:Configuration=%CONFIGURATION% || GOTO ERROR

ECHO ^**************************
ECHO ^* Building C++ Projects  *
ECHO ^**************************
REM Locate VS MSBuild for native C++ projects
SET MSBUILD_EXEC=
FOR /F "tokens=* USEBACKQ" %%F IN (`where msbuild.exe 2^>nul`) DO (
    SET MSBUILD_EXEC=%%F
    ECHO INFO: Found msbuild.exe at '%%F'
    GOTO :FOUND_MSBUILD
)

:LOCATE_MSBUILD
SET VSWHERE_EXEC="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
IF EXIST %VSWHERE_EXEC% (
    FOR /F "tokens=* USEBACKQ" %%F IN (`%VSWHERE_EXEC% -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find MSBuild\**\Bin\amd64\MSBuild.exe`) DO (
        SET MSBUILD_EXEC=%%F
        ECHO INFO: Found msbuild.exe at '%%F'
    )
)

:FOUND_MSBUILD
IF DEFINED MSBUILD_EXEC (
    FOR %%P IN (
        "%VFS_SRCDIR%\GVFS\GitHooksLoader\GitHooksLoader.vcxproj"
        "%VFS_SRCDIR%\GVFS\GVFS.NativeTests\GVFS.NativeTests.vcxproj"
        "%VFS_SRCDIR%\GVFS\GVFS.PostIndexChangedHook\GVFS.PostIndexChangedHook.vcxproj"
        "%VFS_SRCDIR%\GVFS\GVFS.ReadObjectHook\GVFS.ReadObjectHook.vcxproj"
        "%VFS_SRCDIR%\GVFS\GVFS.VirtualFileSystemHook\GVFS.VirtualFileSystemHook.vcxproj"
    ) DO (
        ECHO Building %%~nP...
        "%MSBUILD_EXEC%" %%P ^
                /t:Build ^
                /v:%VERBOSITY% ^
                /p:Configuration=%CONFIGURATION% ^
                /p:Platform=x64 ^
                /p:SolutionDir="%VFS_SRCDIR%\\" || GOTO ERROR
    )
) ELSE (
    ECHO WARNING: Could not find VS MSBuild. Native C++ projects will not be built.
    ECHO          Install Visual Studio with the C++ workload to build native projects.
)

ECHO ^*****************************
ECHO ^* Building Managed Projects *
ECHO ^*****************************
REM Self-contained deployment requires "dotnet publish" (not "dotnet build")
REM to produce complete output with runtime and correct version resources.
FOR %%P IN (
    "%VFS_SRCDIR%\GVFS\GVFS\GVFS.csproj"
    "%VFS_SRCDIR%\GVFS\GVFS.Mount\GVFS.Mount.csproj"
    "%VFS_SRCDIR%\GVFS\GVFS.Hooks\GVFS.Hooks.csproj"
    "%VFS_SRCDIR%\GVFS\GVFS.Service\GVFS.Service.csproj"
    "%VFS_SRCDIR%\GVFS\FastFetch\FastFetch.csproj"
    "%VFS_SRCDIR%\GVFS\GVFS.UnitTests\GVFS.UnitTests.csproj"
    "%VFS_SRCDIR%\GVFS\GVFS.FunctionalTests\GVFS.FunctionalTests.csproj"
    "%VFS_SRCDIR%\GVFS\GVFS.PerfProfiling\GVFS.PerfProfiling.csproj"
) DO (
    ECHO Publishing %%~nP...
    dotnet publish %%P --no-restore -v:%VERBOSITY% -c %CONFIGURATION% || GOTO ERROR
)

ECHO ^*******************************
ECHO ^* Building Packaging Projects *
ECHO ^*******************************
REM Payload and Installers no longer reference vcxproj (native projects are
REM built separately above). Build ordering is handled by Build.bat.
FOR %%P IN (
    "%VFS_SRCDIR%\GVFS\GVFS.Payload\GVFS.Payload.csproj"
    "%VFS_SRCDIR%\GVFS\GVFS.Installers\GVFS.Installers.csproj"
) DO (
    ECHO Publishing %%~nP...
    dotnet publish %%P --no-restore -v:%VERBOSITY% -c %CONFIGURATION% || GOTO ERROR
)

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
