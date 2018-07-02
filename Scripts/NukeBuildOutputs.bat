@ECHO OFF

taskkill /f /im GVFS.Mount.exe 2>&1
verify >nul

powershell -NonInteractive -NoProfile -Command "& { (Get-MpPreference).ExclusionPath | ? {$_.StartsWith('C:\Repos\')} | %%{Remove-MpPreference -ExclusionPath $_} }"

IF EXIST C:\Repos\GVFSFunctionalTests\enlistment (
    rmdir /s /q C:\Repos\GVFSFunctionalTests\enlistment
) ELSE (
    ECHO no test enlistment found
)

IF EXIST C:\Repos\GVFSPerfTest (
    rmdir /s /q C:\Repos\GVFSPerfTest
) ELSE (
    ECHO no perf test enlistment found
)

IF EXIST %~dp0\..\..\BuildOutput (
    ECHO deleting build outputs
    rmdir /s /q %~dp0\..\..\BuildOutput
) ELSE (
    ECHO no build outputs found
)

IF EXIST %~dp0\..\..\packages (
    ECHO deleting packages
    rmdir /s /q %~dp0\..\..\packages
) ELSE (
    ECHO no packages found
)

call %~dp0\StopAllServices.bat
