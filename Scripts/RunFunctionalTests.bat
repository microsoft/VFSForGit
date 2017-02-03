@ECHO OFF
IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

%~dp0\..\..\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\GVFS.FunctionalTests.exe %2