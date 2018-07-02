IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

call %~dp0\UninstallGVFS.bat

if not exist "c:\Program Files\Git" goto :noGit
for /F "delims=" %%g in ('dir "c:\Program Files\Git\unins*.exe" /B /S /O:-D') do %%g /VERYSILENT /SUPPRESSMSGBOXES /NORESTART & goto :deleteGit

:deleteGit
rmdir /q/s "c:\Program Files\Git"

:noGit
REM This is a hacky way to sleep for 2 seconds in a non-interactive window.  The timeout command does not work if it can't redirect stdin.
ping 1.1.1.1 -n 1 -w 2000 >NUL

call %~dp0\StopService.bat gvflt
call %~dp0\StopService.bat prjflt

if not exist c:\Windows\System32\drivers\gvflt.sys goto :removePrjFlt
del c:\Windows\System32\drivers\gvflt.sys

:removePrjFlt
if not exist c:\Windows\System32\drivers\PrjFlt.sys goto :runInstallers
sc delete prjflt
verify >nul
del c:\Windows\System32\drivers\PrjFlt.sys

:runInstallers
call %~dp0\..\..\BuildOutput\GVFS.Build\InstallG4W.bat
call %~dp0\..\..\BuildOutput\GVFS.Build\InstallGVFS.bat
