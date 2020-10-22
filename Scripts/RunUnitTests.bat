@ECHO OFF
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

set RESULT=0

%VFS_OUTPUTDIR%\GVFS.UnitTests.Windows\bin\x64\%Configuration%\GVFS.UnitTests.Windows.exe  || set RESULT=1

exit /b %RESULT%