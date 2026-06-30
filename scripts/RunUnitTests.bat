@ECHO OFF
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

IF "%1"=="" (SET "CONFIGURATION=Debug") ELSE (SET "CONFIGURATION=%1")
IF "%2"=="" (SET "ARCH=x64") ELSE (SET "ARCH=%2")

SET RESULT=0

%VFS_OUTDIR%\GVFS.UnitTests\bin\%CONFIGURATION%\net10.0-windows10.0.17763.0\win-%ARCH%\publish\GVFS.UnitTests.exe || SET RESULT=1

EXIT /b %RESULT%
