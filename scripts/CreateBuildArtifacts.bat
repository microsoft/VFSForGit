@ECHO OFF
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10
SETLOCAL

IF "%~1"=="" (
    SET CONFIGURATION=Debug
) ELSE (
    SET CONFIGURATION=%1
)

IF "%~2"=="" (
    SET OUTROOT=%VFS_PUBLISHDIR%
) ELSE (
    SET OUTROOT=%2
)

IF "%~3"=="" (
    SET ARCH=x64
) ELSE (
    SET ARCH=%3
)
IF /I "%ARCH%"=="ARM64" SET ARCH=arm64
IF /I "%ARCH%"=="X64"   SET ARCH=x64

IF EXIST %OUTROOT% (
  rmdir /s /q %OUTROOT%
)

ECHO ^**********************
ECHO ^* Collecting Symbols *
ECHO ^**********************
mkdir %OUTROOT%\Symbols
SET COPY_SYM_CMD="&{Get-ChildItem -Recurse -Path '%VFS_OUTDIR%' -Include *.pdb | Where-Object FullName -Match '\\bin\\.*\\?%CONFIGURATION%\\' | Copy-Item -Destination '%OUTROOT%\Symbols'}"
powershell ^
    -NoProfile ^
    -ExecutionPolicy Bypass ^
    -Command %COPY_SYM_CMD% || GOTO ERROR

ECHO ^******************************
ECHO ^* Collecting GVFS.Installers *
ECHO ^******************************
mkdir %OUTROOT%\GVFS.Installers
xcopy /S /Y ^
    %VFS_OUTDIR%\GVFS.Installers\bin\%CONFIGURATION%\win-%ARCH%\* ^
    %OUTROOT%\GVFS.Installers\ || GOTO ERROR

ECHO ^************************
ECHO ^* Collecting FastFetch *
ECHO ^************************
ECHO Collecting FastFetch...
mkdir %OUTROOT%\FastFetch
xcopy /S /Y ^
    %VFS_OUTDIR%\FastFetch\bin\%CONFIGURATION%\net10.0-windows10.0.17763.0\win-%ARCH%\publish\* ^
    %OUTROOT%\FastFetch\ || GOTO ERROR

ECHO ^***********************************
ECHO ^* Collecting GVFS.FunctionalTests *
ECHO ^***********************************
mkdir %OUTROOT%\GVFS.FunctionalTests
xcopy /S /Y ^
    %VFS_OUTDIR%\GVFS.FunctionalTests\bin\%CONFIGURATION%\net10.0-windows10.0.17763.0\win-%ARCH%\publish\* ^
    %OUTROOT%\GVFS.FunctionalTests\ || GOTO ERROR

GOTO :EOF

:USAGE
ECHO usage: %~n0%~x0 [^<configuration^>] [^<destination^>] [^<arch^>]
ECHO.
ECHO   configuration    Solution configuration (default: Debug).
ECHO   destination      Destination directory to copy artifacts (default: %VFS_PUBLISHDIR%).
ECHO   arch             Target CPU architecture: x64 or arm64 (default: x64).
ECHO.
EXIT 1

:ERROR
ECHO ERROR: Create build artifacts failed with exit code %ERRORLEVEL%
EXIT /B %ERRORLEVEL%
