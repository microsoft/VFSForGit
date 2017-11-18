@ECHO OFF
IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

sc create %Configuration%.RGFS.Service binPath=%~dp0\..\..\BuildOutput\RGFS.Service\bin\x64\%Configuration%\RGFS.Service.exe