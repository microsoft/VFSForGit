@ECHO OFF
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

IF "%1"=="" (SET "CONFIGURATION=Debug") ELSE (SET "CONFIGURATION=%1")

SET RESULT=0

%VFS_OUTDIR%\GVFS.UnitTests\bin\%CONFIGURATION%\net48\win-x64\GVFS.UnitTests.exe || SET RESULT=1

EXIT /b %RESULT%
