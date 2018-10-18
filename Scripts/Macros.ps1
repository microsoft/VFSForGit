function global:b { & (Join-Path $Env:VFS_SCRIPTSDIR "BuildGVFSForWindows.bat") @args }
function global:br { & (Join-Path $Env:VFS_SCRIPTSDIR "BuildGVFSForWindows.bat") Release @args }
function global:ut { & (Join-Path $Env:VFS_SCRIPTSDIR "RunUnitTests.bat") @args }
function global:utr { & (Join-Path $Env:VFS_SCRIPTSDIR "RunUnitTests.bat") Release @args }
function global:ft { & (Join-Path $Env:VFS_SCRIPTSDIR "RunFunctionalTests.bat") @args }
function global:ftr { & (Join-Path $Env:VFS_SCRIPTSDIR "RunFunctionalTests.bat") Release @args }
function global:scorch { & (Join-Path $Env:VFS_SCRIPTSDIR "NukeBuildOutputs.bat") @args }
