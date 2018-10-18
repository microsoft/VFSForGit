@ECHO OFF

REM Maintain compat for current build definitions while we move to YAML by
REM automatically initing when this is running on a build agent.
IF "%TF_BUILD%"=="true" (
  CALL %~dp0/../init.cmd
)
REM Delete this block when we have moved to YAML-based pipelines.

IF NOT "%VFS_DEVSHELL%"=="true" (
  ECHO ERROR: This shell is not a VFS for Git developer shell.
  ECHO Run init.cmd or init.ps1 at the root of the repository.
  EXIT /b 10
)
