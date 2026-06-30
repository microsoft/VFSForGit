#
# Ensure a Visual Studio 2022 (or newer) install with the "Desktop
# development with C++" workload is present on the build agent.
#
# .NET NativeAOT publishing (used by every product-facing managed VFS for
# Git project via PublishAot=true in Directory.Build.props) requires the
# C++ build tools from this workload at publish time. The native VFS
# projects also build against the v143 toolset, which ships with VS 2022.
#
# This script handles three situations:
#   1. A VS 2022+ install with the C++ workload is already present
#      -> exit early.
#   2. A VS 2022+ install (any product) is present but the C++ workload
#      is missing -> modify that install to add it.
#   3. No VS 2022+ install at all -> install VS Build Tools 2022 with
#      the VC tools workload. (An older VS install, e.g. VS 2019, is
#      ignored here -- we leave it alone and install VS 2022 alongside.)
#
# vswhere.exe is bootstrapped from GitHub if not already on disk.
#
# Workload presence is verified via vswhere's -requires/-requiresAny
# rather than by probing for individual files like link.exe; the
# documented prerequisite is the workload itself, not any specific
# implementation detail of NativeAOT.
#
# See https://aka.ms/nativeaot-prerequisites for the full prerequisite list.
#

$ErrorActionPreference = 'Stop'

# Force TLS 1.2+ for downloads (Windows PowerShell 5.1 may default to 1.0/1.1
# on older OS images, which fails against modern HTTPS endpoints).
[Net.ServicePointManager]::SecurityProtocol =
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$vsRoot         = "${env:ProgramFiles(x86)}\Microsoft Visual Studio"
$vsInstallerDir = Join-Path $vsRoot 'Installer'
$vswherePath    = Join-Path $vsInstallerDir 'vswhere.exe'
$setupExePath   = Join-Path $vsInstallerDir 'setup.exe'

$vswhereDownloadUrl    = 'https://github.com/microsoft/vswhere/releases/latest/download/vswhere.exe'
$buildToolsDownloadUrl = 'https://aka.ms/vs/17/release/vs_BuildTools.exe'

# The native VFS projects build against the v143 toolset, which ships with
# Visual Studio 2022 (product line 17.x). VS 2019 (16.x) carries v142 and
# is not sufficient -- so all vswhere queries below are scoped to 17.0+.
$minVsVersion = '[17.0,)'

# Either of these workloads provides the C++ build tools we need.
# Microsoft.VisualStudio.Workload.NativeDesktop = "Desktop development with C++" (Community/Pro/Enterprise).
# Microsoft.VisualStudio.Workload.VCTools       = "C++ build tools" (Build Tools).
$cppWorkloads = @(
    'Microsoft.VisualStudio.Workload.NativeDesktop',
    'Microsoft.VisualStudio.Workload.VCTools'
)

# ARM64 cross-compilation requires an additional component that is not
# included in the default C++ workload install. We ensure it's present
# so that vcpkg and MSBuild can target arm64-windows triplets even when
# running on an x64 host (or on an ARM64 host that only has the default
# ARM64 → ARM64 native tools and not the broader "all targets" set).
$arm64Component = 'Microsoft.VisualStudio.Component.VC.Tools.ARM64'

function Get-VsWhere {
    if (Test-Path $script:vswherePath) {
        return $script:vswherePath
    }
    $dest = Join-Path $env:TEMP 'vswhere.exe'
    Write-Host "vswhere.exe not found at '$script:vswherePath'; downloading from $script:vswhereDownloadUrl..."
    Invoke-WebRequest -Uri $script:vswhereDownloadUrl -OutFile $dest -UseBasicParsing
    Write-Host "Downloaded vswhere to: $dest"
    return $dest
}

function Find-VsInstall {
    param(
        [Parameter(Mandatory = $true)] [string]   $VswhereExe,
        [string[]]                                $RequiredWorkloads
    )
    $vswhereArgs = @('-latest', '-prerelease', '-products', '*', '-version', $script:minVsVersion, '-format', 'json')
    if ($RequiredWorkloads -and $RequiredWorkloads.Count -gt 0) {
        $vswhereArgs += '-requires'
        $vswhereArgs += $RequiredWorkloads
        if ($RequiredWorkloads.Count -gt 1) {
            $vswhereArgs += '-requiresAny'
        }
    }
    $output = & $VswhereExe @vswhereArgs
    if ($LASTEXITCODE -ne 0) {
        throw "vswhere.exe failed with exit code $LASTEXITCODE"
    }
    if (-not $output) { return $null }
    $installs = $output | ConvertFrom-Json
    if (-not $installs -or $installs.Count -eq 0) { return $null }
    return $installs[0]
}

function Invoke-VsSetup {
    param(
        [string]   $ExePath,
        [string[]] $ArgumentList,
        [string]   $Description
    )
    Write-Host "Running $Description : `"$ExePath`" $($ArgumentList -join ' ')"
    $proc = Start-Process -FilePath $ExePath -ArgumentList $ArgumentList -Wait -PassThru -NoNewWindow
    Write-Host "$Description exit code: $($proc.ExitCode)"
    # 0 = success, 3010 = success but reboot required.
    if ($proc.ExitCode -ne 0 -and $proc.ExitCode -ne 3010) {
        throw "$Description failed with exit code $($proc.ExitCode)"
    }
}

# --- Locate or bootstrap vswhere ---
$vswhereExe = Get-VsWhere

# --- Quick exit if a VS install with the C++ workload AND arm64 tools is already present ---
# Check requires a C++ workload (either one) AND the ARM64 component.
# vswhere -requires with -requiresAny means "any one of the listed components".
# To enforce AND, we run vswhere with the ARM64 component as a hard requirement
# and the C++ workloads as a separate check.
$existing = Find-VsInstall -VswhereExe $vswhereExe -RequiredWorkloads $cppWorkloads
if ($existing) {
    # Also check for ARM64 component
    $arm64Present = Find-VsInstall -VswhereExe $vswhereExe -RequiredWorkloads @($arm64Component)
    if ($arm64Present) {
        Write-Host "VS install with C++ workload + ARM64 tools already present: $($existing.installationPath) ($($existing.productId))"
        exit 0
    }
    Write-Host "VS install has C++ workload but missing ARM64 tools; will add..."
}

# --- Find any VS install (regardless of workloads) ---
$install = Find-VsInstall -VswhereExe $vswhereExe

# --- If no VS 2022+ install at all, install VS Build Tools 2022 with the VC workload ---
if (-not $install) {
    Write-Host "No Visual Studio 2022 (or newer) installation found; installing VS Build Tools 2022 with the C++ workload..."
    $bootstrapper = Join-Path $env:TEMP 'vs_BuildTools.exe'
    Write-Host "Downloading VS Build Tools bootstrapper from $buildToolsDownloadUrl..."
    Invoke-WebRequest -Uri $buildToolsDownloadUrl -OutFile $bootstrapper -UseBasicParsing
    Write-Host "Downloaded bootstrapper to: $bootstrapper"

    Invoke-VsSetup -ExePath $bootstrapper -Description 'VS Build Tools install' -ArgumentList @(
        '--add', 'Microsoft.VisualStudio.Workload.VCTools',
        '--add', $arm64Component,
        '--includeRecommended',
        '--quiet',
        '--norestart',
        '--wait',
        '--nocache'
    )
} else {
    # --- Existing VS install without C++ workload: modify it ---
    # Build Tools needs Microsoft.VisualStudio.Workload.VCTools; full Visual
    # Studio (Community/Pro/Enterprise) needs Microsoft.VisualStudio.Workload.NativeDesktop.
    $workload = if ($install.productId -eq 'Microsoft.VisualStudio.Product.BuildTools') {
        'Microsoft.VisualStudio.Workload.VCTools'
    } else {
        'Microsoft.VisualStudio.Workload.NativeDesktop'
    }
    Write-Host "Existing VS install at $($install.installationPath) ($($install.productId)) lacks the C++ workload; adding '$workload'..."

    if (-not (Test-Path $setupExePath)) {
        throw "Visual Studio Installer setup.exe not found at '$setupExePath'"
    }

    Invoke-VsSetup -ExePath $setupExePath -Description 'VS Installer modify' -ArgumentList @(
        'modify',
        '--installPath', $install.installationPath,
        '--add', $workload,
        '--add', $arm64Component,
        '--includeRecommended',
        '--quiet',
        '--norestart',
        '--wait',
        '--nocache'
    )
}

# --- Final verification: vswhere must now report an install with the workload ---
$verified = Find-VsInstall -VswhereExe $vswhereExe -RequiredWorkloads $cppWorkloads
if (-not $verified) {
    throw "Workload install reported success but vswhere still does not report a VS install with the C++ workload"
}
Write-Host "VS install with C++ workload now present: $($verified.installationPath) ($($verified.productId))"
