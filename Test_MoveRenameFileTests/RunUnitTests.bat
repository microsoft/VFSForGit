@ECHO OFF
IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

%~dp0\..\..\BuildOutput\GVFS.UnitTests\bin\x64\%Configuration%\GVFS.UnitTests.exe