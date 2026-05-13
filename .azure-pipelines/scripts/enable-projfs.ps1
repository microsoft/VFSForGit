<#
.SYNOPSIS
    Enable the Windows Projected File System (ProjFS) optional feature.

.DESCRIPTION
    VFS for Git -- both the runtime and several unit tests -- P/Invokes
    into ProjectedFSLib.dll (e.g. PrjDoesNameContainWildCards). That DLL
    is only present on disk when the 'Client-ProjFS' Windows optional
    feature is enabled. Hosted CI images do not enable it by default,
    so unit tests fail with:

        System.DllNotFoundException : Unable to load DLL
        'ProjectedFSLib.dll' or one of its dependencies.

    This script is a no-op when the feature is already enabled.
#>

#Requires -Version 5
#Requires -RunAsAdministrator

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$featureName = 'Client-ProjFS'

$feature = Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction Stop
if ($feature.State -eq 'Enabled') {
    Write-Host "INFO: Windows optional feature '$featureName' is already enabled."
    exit 0
}

Write-Host "INFO: Enabling Windows optional feature '$featureName'..."
$result = Enable-WindowsOptionalFeature -Online -FeatureName $featureName -NoRestart -ErrorAction Stop

if ($result.RestartNeeded) {
    # The pipeline runs unit tests immediately after this script which P/Invoke
    # into ProjectedFSLib.dll. If the OS reports a reboot is required to make
    # the feature usable, the build agent is in an inconsistent state and the
    # tests will fail unpredictably -- so fail fast here instead.
    throw "Windows optional feature '$featureName' was enabled but a restart is required to take effect; failing the build."
} else {
    Write-Host "INFO: Windows optional feature '$featureName' is now enabled."
}
