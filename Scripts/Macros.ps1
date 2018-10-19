function global:b { & (Join-Path $Env:VFS_SCRIPTSDIR "BuildGVFSForWindows.bat") Debug $args }
function global:ftest { & (Join-Path $Env:VFS_SCRIPTSDIR "RunFunctionalTests.bat" ) Debug --full-suite "--test=$args" }
function global:ig { & (Join-Path $Env:VFS_OUTPUTDIR "GVFS.Build\InstallG4W.bat ") }
function global:iv { & (Join-Path $Env:VFS_OUTPUTDIR "GVFS.Build\InstallGVFS.bat ") }
