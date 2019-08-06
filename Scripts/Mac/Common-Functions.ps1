<#
.SYNOPSIS
Generates the text for GVFS.Installer.Mac nuspec file from the provided inputs
#>
function Write-Nuspec
{
    param
    (
        [Parameter(Mandatory)]
        [string]
        $PackageVersion,

        [Parameter(Mandatory)]
        [string]
        $GvfsInstallerPkg,

        [Parameter(Mandatory)]
        [string]
        $GitInstallerPkg,

        [Parameter(Mandatory)]
        [string]
        $GcmInstallerPkg
    )

     $template =
     "<?xml version=""1.0""?>
     <package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
       <metadata>
         <id>GVFS.Installers.Mac</id>
         <version>$PackageVersion</version>
         <authors>Microsoft</authors>
         <requireLicenseAcceptance>false</requireLicenseAcceptance>
         <description>GVFS and G4M Mac installers</description>
       </metadata>
       <files>
         <file src=""$GvfsInstallerPkg"" target=""GVFS"" />
         <file src=""$GitInstallerPkg"" target=""G4M"" />
         <file src=""$GcmInstallerPkg"" target=""GCM"" />
       </files>
     </package>"

     return $template
}

<#
.DESCRIPTION
Downloads the specified version of GCM Core to the specified DownloadLocation
#>
function Download-GCM
{
    param
    (
        [Parameter(Mandatory)]
        [string]
        $GcmReleaseVersion,

        [Parameter(Mandatory)]
        [string]
        $GcmPackageName,

        [Parameter(Mandatory)]
        [string]
        $DownloadLocation
    )

    $url = "https://github.com/microsoft/Git-Credential-Manager-Core/releases/download/$GcmReleaseVersion/$GcmPackageName"
    $outfile = $DownloadLocation + "/" + $GcmPackageName

    Invoke-WebRequest -Uri $url -Outfile $outfile

    return $outfile
}

<#
.SYNOPSIS
Parses the version of the NuGet package that contains the Git installer from a props file.
#>
function Get-GitPackageVersionFromProps
{
    param
    (
        [Parameter(Mandatory)]
        [string]
        $GvfsPropsPath
    )

    $gitPackageVersionLine = Get-ChildItem -Path $GvfsPropsPath | Select-String -Pattern 'GitPackageVersion'
    $matchFount = $gitPackageVersionLine -match '<GitPackageVersion>(.*?)</GitPackageVersion>'
    return $matches[1]
}

<#
.SYNOPSIS
Given a path to where NuGet packages are located, find the GitForMac NuGetPackage version and extract the Git version.
#>
function Get-GitVersionFromNuGetPackage
{
    param
    (
        [Parameter(Mandatory)]
        [string]
        $NuGetPackagesDir,

        [Parameter(Mandatory)]
        [string]
        $GitPackageVersion
    )

    # Find the git version number (looking through packages directory)
    $gitInstallerPath = $NuGetPackagesDir + "/gitformac.gvfs.installer/" + $GitPackageVersion + "/tools"
    $toolsContents = Get-ChildItem -Path ($NuGetPackagesDir + "/gitformac.gvfs.installer/" + $GitPackageVersion + "/tools") -Include *.pkg

    $gitInstallerPkgName = $toolsContents[0].Name
    return $gitInstallerPkgName
}

<#
.DESCRIPTION
Generate and write a GVFS.Installers.Mac NuSpec from given
parameters. This function orchestrates finding the various packages
required for generating the installer assuming the behavior for how
the current CI/CD Release pipleline lays out artifacts. It will also
download GCM core.
#>
function Generate-NuSpec
{
    param
    (
        [Parameter(Mandatory)]
        [string]
        $PackageDir,

        [Parameter(Mandatory)]
        [string]
        $GvfsVersion,

        [Parameter(Mandatory)]
        [string]
        $GcmReleaseVersion,

        [Parameter(Mandatory)]
        [string]
        $GcmPackageName
    )

    # Git installer pkg: Look throuhg the packages directory to find the git installer pkg
    $gitInstallers = Get-ChildItem -Path ($PackageDir + "/*") -Include 'git-*.pkg'
    $gitInstallerPkg = $gitInstallers[0]

    # Gcm installer pkg: Download
    $GcmInstallerPkg = Download-GCM -GcmReleaseVersion $GcmReleaseVersion -GcmPackageName $GcmPackageName -DownloadLocation $PackageDir

    # GVFS installer pkg
    $GvfsInstallerPkg = $PackageDir + "/" + "VFSForGit.$GvfsVersion.pkg"

    $template = Write-Nuspec -PackageVersion $GvfsVersion -GvfsInstallerPkg $GvfsInstallerPkg -GitInstallerPkg $gitInstallerPkg -GcmInstallerPkg $GcmInstallerPkg
    return $template
}
