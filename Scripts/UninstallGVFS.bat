@ECHO OFF
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

taskkill /F /T /FI "IMAGENAME eq git.exe"
taskkill /F /T /FI "IMAGENAME eq GVFS.exe"
taskkill /F /T /FI "IMAGENAME eq GVFS.Mount.exe"

if not exist "c:\Program Files\GVFS" goto :end

call %VFS_SCRIPTSDIR%\StopAllServices.bat

if not "%2"=="--remove-prjflt" goto :uninstallGVFS
REM The GVFS uninstaller will not remove prjflt, and so we must remove it ourselves first.  If we don't, the non-inbox ProjFS
REM will cause problems next time the inbox ProjFS is enabled
call %VFS_SCRIPTSDIR%\StopService.bat prjflt
rundll32.exe SETUPAPI.DLL,InstallHinfSection DefaultUninstall 128 C:\Program Files\GVFS\Filter\prjflt.inf

:uninstallGVFS
REM Find the latest uninstaller file by date and run it. Goto the next step after a single execution.
for /F "delims=" %%f in ('dir "c:\Program Files\GVFS\unins*.exe" /B /S /O:-D') do %%f /VERYSILENT /SUPPRESSMSGBOXES /NORESTART & goto :deleteGVFS

:deleteGVFS
rmdir /q/s "c:\Program Files\GVFS"

REM Delete ProgramData\GVFS directory (logs, downloaded upgrades, repo-registry, gvfs.config). It can affect the behavior of a future GVFS install.
if exist "C:\ProgramData\GVFS" rmdir /q/s "C:\ProgramData\GVFS"

:end
