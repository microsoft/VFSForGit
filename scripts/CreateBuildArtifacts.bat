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
    %VFS_OUTDIR%\GVFS.Installers\bin\%CONFIGURATION%\win-x64 ^
    %OUTROOT%\GVFS.Installers\ || GOTO ERROR

ECHO ^************************
ECHO ^* Collecting FastFetch *
ECHO ^************************
ECHO Collecting FastFetch...
mkdir %OUTROOT%\FastFetch
xcopy /S /Y ^
    %VFS_OUTDIR%\FastFetch\bin\%CONFIGURATION%\net471\win-x64 ^
    %OUTROOT%\FastFetch\ || GOTO ERROR

ECHO ^***********************************
ECHO ^* Collecting GVFS.FunctionalTests *
ECHO ^***********************************
mkdir %OUTROOT%\GVFS.FunctionalTests
xcopy /S /Y ^
    %VFS_OUTDIR%\GVFS.FunctionalTests\bin\%CONFIGURATION%\net471\win-x64 ^
    %OUTROOT%\GVFS.FunctionalTests\ || GOTO ERROR

GOTO :EOF

:USAGE
ECHO usage: %~n0%~x0 [^<configuration^>] [^<destination^>]
ECHO.
ECHO   configuration    Solution configuration (default: Debug).
ECHO   destination      Destination directory to copy artifacts (default: %VFS_PUBLISHDIR%).
ECHO.
EXIT 1

:ERROR
ECHO ERROR: Create build artifacts failed with exit code %ERRORLEVEL%
EXIT /B %ERRORLEVEL%
