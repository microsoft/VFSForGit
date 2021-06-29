@ECHO OFF
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

CALL %VFS_SCRIPTSDIR%\StopService.bat GVFS.Service
CALL %VFS_SCRIPTSDIR%\StopService.bat Test.GVFS.Service
