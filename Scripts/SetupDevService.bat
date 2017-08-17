@ECHO OFF
IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

sc create %Configuration%.GVFS.Service binPath=%~dp0\..\..\BuildOutput\GVFS.Service\bin\x64\%Configuration%\GVFS.Service.exe