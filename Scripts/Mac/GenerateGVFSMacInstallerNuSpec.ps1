<#
Generate and write the GVFS.Mac.Installer nuspec file
#>
param (
    [Parameter(Mandatory)]
    [string]$PackageDir,

    [Parameter(Mandatory)]
    [string]$GvfsVersion,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

. "$PSScriptRoot\Common-Functions.ps1"

$GcmReleaseVersion = "v2.0.33-beta"
$GcmPackageName = "gcmcore-osx-2.0.33.21076.pkg"
$nuspec = Generate-NuSpec -PackageDir $PackageDir -GvfsVersion $GvfsVersion -GcmReleaseVersion $GcmReleaseVersion -GcmPackageName $GcmPackageName
Set-Content -Path $OutputPath/GVFS.Installers.Mac.nuspec -Value $nuspec
