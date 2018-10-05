taskkill /F /T /FI "IMAGENAME eq git.exe"
taskkill /F /T /FI "IMAGENAME eq GVFS.exe"
taskkill /F /T /FI "IMAGENAME eq GVFS.Mount.exe"

if not exist "c:\Program Files\GVFS" goto :end

call %~dp0\StopAllServices.bat

REM The GVFS uninstaller will not remove prjflt, and so we must remove it ourselves first.  If we don't, the non-inbox ProjFS
REM will cause problems next time the inbox ProjFS is enabled
call %~dp0\StopService.bat prjflt
rundll32.exe SETUPAPI.DLL,InstallHinfSection DefaultUninstall 128 C:\Program Files\GVFS\Filter\prjflt.inf

REM Find the latest uninstaller file by date and run it. Goto the next step after a single execution.
for /F "delims=" %%f in ('dir "c:\Program Files\GVFS\unins*.exe" /B /S /O:-D') do %%f /VERYSILENT /SUPPRESSMSGBOXES /NORESTART & goto :deleteGVFS

:deleteGVFS
rmdir /q/s "c:\Program Files\GVFS"

if exist "C:\ProgramData\GVFS\GVFS.Upgrade" rmdir /q/s "C:\ProgramData\GVFS\GVFS.Upgrade"

:end
