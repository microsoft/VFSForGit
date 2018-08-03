@ECHO OFF
IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

set RESULT=0

%~dp0\..\..\BuildOutput\GVFS.UnitTests.Windows\bin\x64\%Configuration%\GVFS.UnitTests.Windows.exe  || set RESULT=1
dotnet %~dp0\..\..\BuildOutput\GVFS.UnitTests\bin\x64\%Configuration%\netcoreapp2.1\GVFS.UnitTests.dll  || set RESULT=1

exit /b %RESULT%