@ECHO OFF
CALL %~dp0\..\InitializeEnvironment.bat || EXIT /b 10

IF "%1"=="" GOTO USAGE
IF "%2"=="" GOTO USAGE

SETLOCAL enableextensions
SET Configuration=%1
SET VFS_STAGEDIR=%2

REM Prepare the staging directories for functional tests.
IF EXIST %VFS_STAGEDIR% (
  rmdir /s /q %VFS_STAGEDIR%
)
mkdir %VFS_STAGEDIR%\src\Scripts
mkdir %VFS_STAGEDIR%\BuildOutput\GVFS.Build
mkdir %VFS_STAGEDIR%\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\netstandard2.0
mkdir %VFS_STAGEDIR%\BuildOutput\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\

REM Make a minimal 'test' enlistment to pass along our pipeline.
copy %VFS_SRCDIR%\init.cmd %VFS_STAGEDIR%\src\
copy %VFS_SCRIPTSDIR%\*.* %VFS_STAGEDIR%\src\Scripts\
copy %VFS_OUTPUTDIR%\GVFS.Build\*.* %VFS_STAGEDIR%\BuildOutput\GVFS.Build
copy %VFS_OUTPUTDIR%\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\*.* %VFS_STAGEDIR%\BuildOutput\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\
GOTO END

:USAGE
echo "ERROR: Usage: CreateBuildDrop.bat [configuration] [build drop root directory]"
exit 1

:END