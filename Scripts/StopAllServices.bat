@ECHO OFF
CALL %~dp0\EnsureVfsDevShell.bat || EXIT /b 10

call %VFS_SCRIPTSDIR%\StopService.bat GVFS.Service
call %VFS_SCRIPTSDIR%\StopService.bat Test.GVFS.Service
