@ECHO OFF
CALL %~dp0\EnsureVfsDevShell.bat || EXIT /b 10

IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

sc create %Configuration%.GVFS.Service binPath=%VFS_OUTPUTDIR%\GVFS.Service\bin\x64\%Configuration%\GVFS.Service.exe