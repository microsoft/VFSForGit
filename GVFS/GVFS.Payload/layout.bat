@ECHO OFF
SETLOCAL
SETLOCAL EnableDelayedExpansion

IF "%~1" == "" (
    ECHO error: missing configuration
    ECHO.
    GOTO USAGE
)

IF "%~2" == "" (
    ECHO error: missing version
    ECHO.
    GOTO USAGE
)

IF "%~3" == "" (
    ECHO error: missing VCRuntime path
    ECHO.
    GOTO USAGE
)

IF "%~4" == "" (
    ECHO error: missing output path
    ECHO.
    GOTO USAGE
)

SET CONFIGURATION=%1
SET GVFSVERSION=%2
SET VCRUNTIME=%3
SET OUTPUT=%4

IF "%~5" == "" (
    SET ARCH=x64
) ELSE (
    SET ARCH=%5
)
IF /I "%ARCH%"=="ARM64" SET ARCH=arm64
IF /I "%ARCH%"=="X64"   SET ARCH=x64
IF "%ARCH%"=="arm64" (
    SET NATIVE_PLATFORM=ARM64
) ELSE (
    SET NATIVE_PLATFORM=x64
)

SET ROOT=%~dp0..\..
SET BUILD_OUT="%ROOT%\..\out"
SET MANAGED_OUT_FRAGMENT=bin\%CONFIGURATION%\net10.0-windows10.0.17763.0\win-%ARCH%\publish
SET NATIVE_OUT_FRAGMENT=bin\%NATIVE_PLATFORM%\%CONFIGURATION%

ECHO Copying files for ARCH=%ARCH%...
REM ProjFS is now a Windows Optional Feature (available since Windows 10 1809).
REM The filter driver and native library are no longer bundled from a NuGet package.
REM
REM VC++ runtime DLL source:
REM   * x64   -> GVFS.VCRuntime NuGet package (lib\x64\) for parity with the historical layout.
REM   * arm64 -> VS install's redist tree, since the GVFS.VCRuntime package
REM             only ships x64. VCToolsRedistDir is set by Build.bat's
REM             vcvarsall.bat call; the matching Microsoft.VC***.CRT folder
REM             is resolved with a FOR /D wildcard so this keeps working
REM             across MSVC toolset version bumps.
IF "%ARCH%"=="arm64" (
    IF NOT DEFINED VCToolsRedistDir (
        ECHO error: VCToolsRedistDir not set. ARM64 layout requires running
        ECHO        under a VS C++ developer environment ^(Build.bat calls
        ECHO        vcvarsall.bat^).
        EXIT /B 1
    )
    SET VCREDIST_ARCH_DIR=
    FOR /D %%D IN ("%VCToolsRedistDir%arm64\Microsoft.VC*.CRT") DO SET "VCREDIST_ARCH_DIR=%%D"
    IF NOT DEFINED VCREDIST_ARCH_DIR (
        ECHO error: could not locate Microsoft.VC*.CRT under "%VCToolsRedistDir%arm64\"
        EXIT /B 1
    )
    xcopy /Y "!VCREDIST_ARCH_DIR!\msvcp140.dll"     %OUTPUT%
    xcopy /Y "!VCREDIST_ARCH_DIR!\msvcp140_1.dll"   %OUTPUT%
    xcopy /Y "!VCREDIST_ARCH_DIR!\msvcp140_2.dll"   %OUTPUT%
    xcopy /Y "!VCREDIST_ARCH_DIR!\vcruntime140.dll" %OUTPUT%
) ELSE (
    xcopy /Y %VCRUNTIME%\lib\%ARCH%\msvcp140.dll %OUTPUT%
    xcopy /Y %VCRUNTIME%\lib\%ARCH%\msvcp140_1.dll %OUTPUT%
    xcopy /Y %VCRUNTIME%\lib\%ARCH%\msvcp140_2.dll %OUTPUT%
    xcopy /Y %VCRUNTIME%\lib\%ARCH%\vcruntime140.dll %OUTPUT%
)
xcopy /Y /S %BUILD_OUT%\GVFS\%MANAGED_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.Hooks\%MANAGED_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.Mount\%MANAGED_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.Service\%MANAGED_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GitHooksLoader\%NATIVE_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.PostIndexChangedHook\%NATIVE_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.ReadObjectHook\%NATIVE_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.VirtualFileSystemHook\%NATIVE_OUT_FRAGMENT%\* %OUTPUT%

ECHO Cleaning up...
REM Remove unused LibGit2 files
RMDIR /S /Q %OUTPUT%\lib
REM Remove files for x86 (not supported)
RMDIR /S /Q %OUTPUT%\x86
REM Remove stray managed artifacts (AOT binaries don't need these)
DEL /Q %OUTPUT%\*.runtimeconfig.json 2>nul
DEL /Q %OUTPUT%\*.deps.json 2>nul
REM Remove VS C++ code-analysis marker files generated next to native exes
DEL /S /Q %OUTPUT%\*.lastcodeanalysissucceeded 2>nul
REM Remove orphaned managed PDBs (these libraries are compiled into AOT exes)
DEL /Q %OUTPUT%\GVFS.Common.pdb 2>nul
DEL /Q %OUTPUT%\GVFS.Platform.Windows.pdb 2>nul
DEL /Q %OUTPUT%\GVFS.Virtualization.pdb 2>nul

GOTO EOF

:USAGE
ECHO usage: %~n0%~x0 ^<configuration^> ^<version^> ^<vcruntime^> ^<output^> [^<arch^>]
ECHO.
ECHO   configuration   Build configuration (Debug, Release).
ECHO   version         GVFS version string.
ECHO   vcruntime       Path to GVFS.VCRuntime NuGet package contents.
ECHO   output          Output directory.
ECHO   arch            Target CPU architecture: x64 or arm64 (default: x64).
ECHO.
EXIT 1

:ERROR
ECHO Failed with error %ERRORLEVEL%
EXIT /B %ERRORLEVEL%

:EOF
