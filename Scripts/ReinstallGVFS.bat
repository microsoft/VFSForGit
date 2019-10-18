@ECHO OFF
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

REM Passing the remove prjflt flag to the uninstall script
call %VFS_SCRIPTSDIR%\UninstallGVFS.bat "%2"

if not exist "c:\Program Files\Git" goto :noGit
for /F "delims=" %%g in ('dir "c:\Program Files\Git\unins*.exe" /B /S /O:-D') do %%g /VERYSILENT /SUPPRESSMSGBOXES /NORESTART & goto :deleteGit

:deleteGit
rmdir /q/s "c:\Program Files\Git"

:noGit
REM This is a hacky way to sleep for 2 seconds in a non-interactive window.  The timeout command does not work if it can't redirect stdin.
ping 1.1.1.1 -n 1 -w 2000 >NUL

call %VFS_SCRIPTSDIR%\StopService.bat gvflt
call %VFS_SCRIPTSDIR%\StopService.bat prjflt

if not exist c:\Windows\System32\drivers\gvflt.sys goto :removePrjFlt
del c:\Windows\System32\drivers\gvflt.sys

:removePrjFlt
if not "%2"=="--remove-prjflt" goto :runInstallers
if not exist c:\Windows\System32\drivers\PrjFlt.sys goto :runInstallers
sc delete prjflt
verify >nul
del c:\Windows\System32\drivers\PrjFlt.sys

:runInstallers
call %VFS_OUTPUTDIR%\GVFS.Build\InstallProduct.bat
