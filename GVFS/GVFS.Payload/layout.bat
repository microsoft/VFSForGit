@ECHO OFF
SETLOCAL

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
    ECHO error: missing ProjFS path
    ECHO.
    GOTO USAGE
)

IF "%~4" == "" (
    ECHO error: missing VCRuntime path
    ECHO.
    GOTO USAGE
)

IF "%~5" == "" (
    ECHO error: missing output path
    ECHO.
    GOTO USAGE
)

SET CONFIGURATION=%1
SET GVFSVERSION=%2
SET PROJFS=%3
SET VCRUNTIME=%4
SET OUTPUT=%5

SET ROOT=%~dp0..\..
SET BUILD_OUT="%ROOT%\..\out"
SET MANAGED_OUT_FRAGMENT=bin\%CONFIGURATION%\net461\win-x64
SET NATIVE_OUT_FRAGMENT=bin\x64\%CONFIGURATION%

ECHO Copying files...
xcopy /Y %PROJFS%\filter\PrjFlt.sys %OUTPUT%\Filter\
xcopy /Y %PROJFS%\filter\prjflt.inf %OUTPUT%\Filter\
xcopy /Y %PROJFS%\lib\ProjectedFSLib.dll %OUTPUT%\ProjFS\
xcopy /Y %VCRUNTIME%\lib\x64\msvcp140.dll %OUTPUT%
xcopy /Y %VCRUNTIME%\lib\x64\msvcp140_1.dll %OUTPUT%
xcopy /Y %VCRUNTIME%\lib\x64\msvcp140_2.dll %OUTPUT%
xcopy /Y %VCRUNTIME%\lib\x64\vcruntime140.dll %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS\%MANAGED_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.Hooks\%MANAGED_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.Mount\%MANAGED_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.Service\%MANAGED_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.Service.UI\%MANAGED_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GitHooksLoader\%NATIVE_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.PostIndexChangedHook\%NATIVE_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.ReadObjectHook\%NATIVE_OUT_FRAGMENT%\* %OUTPUT%
xcopy /Y /S %BUILD_OUT%\GVFS.VirtualFileSystemHook\%NATIVE_OUT_FRAGMENT%\* %OUTPUT%

ECHO Cleaning up...
REM Remove unused LibGit2 files
RMDIR /S /Q %OUTPUT%\lib
REM Remove files for x86 (not supported)
RMDIR /S /Q %OUTPUT%\x86

GOTO EOF

:USAGE
ECHO usage: %~n0%~x0 ^<configuration^> ^<version^> ^<projfs^> ^<vcruntime^> ^<output^>
ECHO.
ECHO   configuration   Build configuration (Debug, Release).
ECHO   version         GVFS version string.
ECHO   projfs          Path to GVFS.ProjFS NuGet package contents.
ECHO   vcruntime       Path to GVFS.VCRuntime NuGet package contents.
ECHO   output          Output directory.
ECHO.
EXIT 1

:ERROR
ECHO Failed with error %ERRORLEVEL%
EXIT /B %ERRORLEVEL%

:EOF
