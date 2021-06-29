@ECHO OFF
SETLOCAL

SET SYS_PRJFLT=C:\Windows\System32\drivers\prjflt.sys
SET SYS_PROJFSLIB=C:\Windows\System32\ProjectedFSLib.dll
SET VFS_PROJFSLIB=C:\Program Files\GVFS\ProjectedFSLib.dll
SET VFS_BUND_PRJFLT=C:\Program Files\GVFS\Filter\PrjFlt.sys
SET VFS_BUND_PROJFSLIB=C:\Program Files\GVFS\ProjFS\ProjectedFSLib.dll
SET VFS_EXEC=C:\Program Files\GVFS\GVFS.exe
SET GIT_EXEC=C:\Program Files\Git\cmd\git.exe

ECHO Checking ProjFS Windows feature...
powershell -Command "Get-WindowsOptionalFeature -Online -FeatureName Client-ProjFS"

ECHO Checking ProjFS and GVFS services...
ECHO GVFS.Service:
sc query GVFS.Service

ECHO Test.GVFS.Service:
sc query Test.GVFS.Service

ECHO prjflt:
sc query prjflt

ECHO Checking ProjFS files...
IF EXIST "%SYS_PRJFLT%" (
    ECHO [ FOUND ] %SYS_PRJFLT%
) ELSE (
    ECHO [MISSING] %SYS_PRJFLT%
)

IF EXIST "%SYS_PROJFSLIB%" (
    ECHO [ FOUND ] %SYS_PROJFSLIB%
) ELSE (
    ECHO [MISSING] %SYS_PROJFSLIB%
)

IF EXIST "%VFS_PROJFSLIB%" (
    ECHO [ FOUND ] %VFS_PROJFSLIB%
) ELSE (
    ECHO [MISSING] %VFS_PROJFSLIB%
)

IF EXIST "%VFS_BUND_PRJFLT%" (
    ECHO [ FOUND ] %VFS_BUND_PRJFLT%
) ELSE (
    ECHO [MISSING] %VFS_BUND_PRJFLT%
)

IF EXIST "%VFS_BUND_PROJFSLIB%" (
    ECHO [ FOUND ] %VFS_BUND_PROJFSLIB%
) ELSE (
    ECHO [MISSING] %VFS_BUND_PROJFSLIB%
)

ECHO.
ECHO Print product versions...
IF EXIST "%VFS_EXEC%" (
    "%VFS_EXEC%" version
) ELSE (
    ECHO GVFS not installed at %VFS_EXEC%
)

IF EXIST "%GIT_EXEC%" (
    "%GIT_EXEC%" version
) ELSE (
    ECHO Git not installed at %GIT_EXEC%
)
