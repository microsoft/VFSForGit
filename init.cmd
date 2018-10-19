@ECHO OFF

REM Don't run inside of devenv shells
IF NOT "%VSCMD_VER%"=="" (
  ECHO ERROR: Do not run from a Developer Command prompt.
  ECHO Use a plain shell and try again.
  EXIT /b 10
)

REM Don't run twice
IF NOT "%VFS_DEVSHELL%"=="" (
    ECHO ERROR: This shell is already a VFS for Git developer shell.
    EXIT /b 20
)

REM Set environment variables for interesting paths that scripts might need access to.
PUSHD %~dp0
SET VFS_SRCDIR=%CD%

CALL :RESOLVEPATH "%VFS_SRCDIR%\Scripts"
SET VFS_SCRIPTSDIR=%_PARSED_PATH_%

CALL :RESOLVEPATH "%VFS_SRCDIR%\.."
SET VFS_ENLISTMENTDIR=%_PARSED_PATH_%

CALL :RESOLVEPATH "%VFS_ENLISTMENTDIR%\BuildOutput"
SET VFS_OUTPUTDIR=%_PARSED_PATH_%

CALL :RESOLVEPATH "%VFS_ENLISTMENTDIR%\packages"
SET VFS_PACKAGESDIR=%_PARSED_PATH_%

CALL :RESOLVEPATH "%VFS_ENLISTMENTDIR%\.tools"
SET VFS_TOOLSDIR=%_PARSED_PATH_%

REM Clean up
SET _PARSED_PATH_=

REM Mark this as a dev shell and load the macros
SET VFS_DEVSHELL=true
TITLE VFS ^for Git Developer Shell ^(%VFS_SRCDIR%^)
doskey /MACROFILE=.\Scripts\Macros.cmd.txt

ECHO ==============================================
ECHO * Welcome to the VFS for Git developer shell *
ECHO *                                            *
ECHO * Build: b                                   *
ECHO * Install Git: ig                            *
ECHO * Install VFS for Git: iv                    *
ECHO * Run functional test: ftest [test name(s)]  *
ECHO ==============================================

GOTO :EOF

:RESOLVEPATH
SET "_PARSED_PATH_=%~f1"
GOTO :EOF
