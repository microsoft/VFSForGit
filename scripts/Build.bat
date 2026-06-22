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

REM Architecture: x64 (default) or arm64.
REM Drives vcpkg triplet selection, vcxproj Platform, and dotnet RID.
IF "%~4"=="" (
    SET ARCH=x64
) ELSE (
    SET ARCH=%4
)
IF /I "%ARCH%"=="ARM64" SET ARCH=arm64
IF /I "%ARCH%"=="X64"   SET ARCH=x64
IF NOT "%ARCH%"=="x64" IF NOT "%ARCH%"=="arm64" (
    ECHO ERROR: Unknown architecture '%ARCH%'. Expected x64 or arm64.
    EXIT /B 2
)
REM vcxproj Platform name (mixed case): x64 stays x64, arm64 becomes ARM64.
IF "%ARCH%"=="arm64" (
    SET NATIVE_PLATFORM=ARM64
) ELSE (
    SET NATIVE_PLATFORM=x64
)
ECHO INFO: Building for ARCH=%ARCH% (vcxproj Platform=%NATIVE_PLATFORM%)

REM Make sure vswhere.exe is on PATH so the NativeAOT toolchain (ilc) can
REM locate the VS install and the matching link.exe. Without this, ilc
REM emits the literal vswhere "not recognized" stderr into the link command
REM line and the publish step fails with a malformed link.rsp invocation.
SET "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"

REM .NET 10 SDK ships MSBuild 18.x; VS 2022 ships MSBuild 17.x.
REM Managed (csproj) projects require MSBuild 18.x via "dotnet build".
REM Native C++ (vcxproj) projects require VS MSBuild with VC++ targets.

ECHO ^**********************
ECHO ^* Restoring Packages *
ECHO ^**********************
dotnet restore "%VFS_SRCDIR%\GVFS.sln" ^
        /v:%VERBOSITY% ^
        /p:Configuration=%CONFIGURATION% ^
        /p:VfsArch=%ARCH% || GOTO ERROR

ECHO ^*************************************
ECHO ^* Installing vcpkg native libraries *
ECHO ^*************************************
IF EXIST "%VFS_OUTDIR%\vcpkg_installed\dynamic\%ARCH%-windows-dynamic\bin\git2.dll" (
    ECHO INFO: vcpkg native libraries already present for %ARCH%, skipping install.
    GOTO :VCPKG_DONE
)
SET VCPKG_EXEC=
IF DEFINED VCPKG_INSTALLATION_ROOT (
    IF EXIST "%VCPKG_INSTALLATION_ROOT%\vcpkg.exe" (
        SET "VCPKG_EXEC=%VCPKG_INSTALLATION_ROOT%\vcpkg.exe"
        GOTO :FOUND_VCPKG
    )
)
FOR /F "tokens=* USEBACKQ" %%F IN (`where vcpkg.exe 2^>nul`) DO (
    SET "VCPKG_EXEC=%%F"
    GOTO :FOUND_VCPKG
)
REM Try VS-bundled vcpkg via vswhere
SET VSWHERE_VCPKG="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
IF EXIST %VSWHERE_VCPKG% (
    FOR /F "tokens=* USEBACKQ" %%F IN (`%VSWHERE_VCPKG% -latest -products * -property installationPath`) DO (
        IF EXIST "%%F\VC\vcpkg\vcpkg.exe" (
            SET "VCPKG_EXEC=%%F\VC\vcpkg\vcpkg.exe"
            GOTO :FOUND_VCPKG
        )
    )
)
ECHO ERROR: vcpkg.exe not found. Install vcpkg or set VCPKG_INSTALLATION_ROOT.
ECHO        See https://learn.microsoft.com/en-us/vcpkg/get-started/get-started
EXIT /B 1

:FOUND_VCPKG
ECHO INFO: Using vcpkg at '%VCPKG_EXEC%'
"%VCPKG_EXEC%" install --triplet %ARCH%-windows-static-aot --x-install-root="%VFS_OUTDIR%\vcpkg_installed\static" --x-manifest-root="%VFS_SRCDIR%" || GOTO ERROR
"%VCPKG_EXEC%" install --triplet %ARCH%-windows-dynamic --x-install-root="%VFS_OUTDIR%\vcpkg_installed\dynamic" --x-manifest-root="%VFS_SRCDIR%" || GOTO ERROR
:VCPKG_DONE

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
IF NOT DEFINED MSBUILD_EXEC (
    ECHO ERROR: Could not find VS MSBuild. Install Visual Studio with the C++ workload to build native projects.
    EXIT /B 1
)

REM Initialize the VC++ developer environment for the target architecture so
REM MSBuild can locate the matching cl.exe / link.exe and the right INCLUDE/LIB
REM search paths. Without this, MSBuild finds the v180 toolset's targets file
REM but cannot locate the actual ARM64 build tool binaries.
SET VCVARS_BAT=
SET VSWHERE_VC="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
IF EXIST %VSWHERE_VC% (
    FOR /F "tokens=* USEBACKQ" %%F IN (`%VSWHERE_VC% -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) DO (
        IF EXIST "%%F\VC\Auxiliary\Build\vcvarsall.bat" SET "VCVARS_BAT=%%F\VC\Auxiliary\Build\vcvarsall.bat"
    )
)
IF NOT DEFINED VCVARS_BAT (
    ECHO ERROR: Could not find vcvarsall.bat. Install Visual Studio with the C++ workload.
    EXIT /B 1
)
ECHO INFO: Initializing VC++ env for %ARCH% via "%VCVARS_BAT%"
CALL "%VCVARS_BAT%" %ARCH% || GOTO ERROR

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
            /p:Platform=%NATIVE_PLATFORM% ^
            /p:VfsArch=%ARCH% ^
            /p:SolutionDir="%VFS_SRCDIR%\\" || GOTO ERROR
)

REM vcvarsall.bat sets Platform=<arch> in the environment. MSBuild picks that
REM up as the default $(Platform) for csproj evaluation, which makes the
REM managed projects add an extra "\<arch>\" segment to their bin\obj output
REM paths. That breaks GVFS.Installers which expects the Payload at a
REM Platform-free path. Clear Platform before the managed build so csproj
REM defaults to "AnyCPU" (= Platform-free output paths).
SET "Platform="

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
    dotnet publish %%P -v:%VERBOSITY% -c %CONFIGURATION% /p:VfsArch=%ARCH% || GOTO ERROR
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
    dotnet publish %%P -v:%VERBOSITY% -c %CONFIGURATION% /p:VfsArch=%ARCH% || GOTO ERROR
)

GOTO :EOF

:USAGE
ECHO usage: %~n0%~x0 [^<configuration^>] [^<version^>] [^<verbosity^>] [^<arch^>]
ECHO.
ECHO   configuration    Solution configuration (default: Debug).
ECHO   version          GVFS version (default: 0.2.173.2).
ECHO   verbosity        MSBuild verbosity (default: minimal).
ECHO   arch             Target CPU architecture: x64 or arm64 (default: x64).
ECHO.
EXIT 1

:ERROR
ECHO ERROR: Build failed with exit code %ERRORLEVEL%
EXIT /B %ERRORLEVEL%
