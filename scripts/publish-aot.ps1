<#
.SYNOPSIS
    Publish VFSForGit .NET 10 NativeAOT binaries and create installer layout.

.DESCRIPTION
    Builds all GVFS projects as self-contained NativeAOT executables,
    then assembles them into a flat layout directory suitable for the Inno Setup
    installer (Setup.iss).

    Supports building for win-x64, win-arm64, or both architectures.

    The NativeAOT layout is dramatically simpler than the .NET Framework layout:
    just 9 self-contained .exe files instead of ~150 DLLs + runtimes.

.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release)

.PARAMETER Runtime
    Target runtime: win-x64, win-arm64, or both (default: both)

.PARAMETER OutputDir
    Layout output directory. When building both architectures, -x64 and -arm64
    suffixes are appended. (default: $RepoRoot\..\out\gvfs-aot-layout)

.PARAMETER SkipBuild
    Skip the dotnet publish step (use existing build output)

.PARAMETER BuildInstaller
    Also build the Inno Setup installer after creating the layout
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64", "both")]
    [string]$Runtime = "both",
    [string]$OutputDir = "",
    [switch]$SkipBuild,
    [switch]$BuildInstaller
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$GVFSRoot = Join-Path $RepoRoot "GVFS"
$OutBase = if ($OutputDir) { $OutputDir } else { Join-Path (Split-Path $RepoRoot) "out\gvfs-aot-layout" }

# Determine which runtimes to build
$Runtimes = if ($Runtime -eq "both") { @("win-x64", "win-arm64") } else { @($Runtime) }

Write-Host "=== VFSForGit NativeAOT Publish ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Runtimes:      $($Runtimes -join ', ')"
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Project definitions
# ─────────────────────────────────────────────────────────────────────────────
$ManagedProjects = @(
    @{ Name = "GVFS";              Dir = "GVFS";              Exe = "GVFS.exe" },
    @{ Name = "GVFS.Mount";        Dir = "GVFS.Mount";        Exe = "GVFS.Mount.exe" },
    @{ Name = "GVFS.Hooks";        Dir = "GVFS.Hooks";        Exe = "GVFS.Hooks.exe" },
    @{ Name = "GVFS.Service";      Dir = "GVFS.Service";      Exe = "GVFS.Service.exe" },
    @{ Name = "GVFS.Service.UI";   Dir = "GVFS.Service.UI";   Exe = "GVFS.Service.UI.exe" }
)

# Native C++ projects (built with MSBuild, not dotnet publish)
$NativeProjects = @(
    @{ Name = "GitHooksLoader";            Dir = "GitHooksLoader";            Exe = "GitHooksLoader.exe" },
    @{ Name = "GVFS.ReadObjectHook";       Dir = "GVFS.ReadObjectHook";       Exe = "GVFS.ReadObjectHook.exe" },
    @{ Name = "GVFS.PostIndexChangedHook"; Dir = "GVFS.PostIndexChangedHook"; Exe = "GVFS.PostIndexChangedHook.exe" },
    @{ Name = "GVFS.VirtualFileSystemHook"; Dir = "GVFS.VirtualFileSystemHook"; Exe = "GVFS.VirtualFileSystemHook.exe" }
)

# Map dotnet RID to MSBuild platform for native C++ projects
$RidToMSBuildPlatform = @{
    "win-x64"  = "x64"
    "win-arm64" = "ARM64"
}

# Find MSBuild once
$msbuildExe = $null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -property installationPath 2>$null
    if ($vsPath) {
        $msbuildExe = Join-Path $vsPath "MSBuild\Current\Bin\amd64\MSBuild.exe"
        if (-not (Test-Path $msbuildExe)) { $msbuildExe = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe" }
    }
}
if (-not $msbuildExe -or -not (Test-Path $msbuildExe)) {
    $msbuildExe = (Get-Command msbuild.exe -EA 0).Source
}

# ─────────────────────────────────────────────────────────────────────────────
# Build each architecture
# ─────────────────────────────────────────────────────────────────────────────
foreach ($rid in $Runtimes) {
    # Output directory: append arch suffix when building both
    $OutRoot = if ($Runtimes.Count -gt 1) { "${OutBase}-$($rid.Replace('win-',''))" } else { $OutBase }
    $msbuildPlatform = $RidToMSBuildPlatform[$rid]

    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host "  Building for $rid → $OutRoot" -ForegroundColor Cyan
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host ""

    if (-not $SkipBuild) {
        Write-Host "--- Publishing managed projects ($rid) ---" -ForegroundColor Yellow

        foreach ($proj in $ManagedProjects) {
            $csproj = Join-Path $GVFSRoot "$($proj.Dir)\$($proj.Name).csproj"
            if (-not (Test-Path $csproj)) {
                Write-Warning "Project not found: $csproj — skipping"
                continue
            }

            Write-Host "  Publishing $($proj.Name)..." -NoNewline
            $sw = [Diagnostics.Stopwatch]::StartNew()

            dotnet publish $csproj `
                -c $Configuration `
                -r $rid `
                --self-contained true `
                -o "$OutRoot" `
                2>&1 | Out-Null

            if ($LASTEXITCODE -ne 0) {
                Write-Host " FAILED" -ForegroundColor Red
                throw "dotnet publish failed for $($proj.Name) ($rid)"
            }

            $sw.Stop()
            $size = if (Test-Path "$OutRoot\$($proj.Exe)") {
                [math]::Round((Get-Item "$OutRoot\$($proj.Exe)").Length / 1MB, 1)
            } else { "?" }
            Write-Host " OK (${size}MB, $([math]::Round($sw.Elapsed.TotalSeconds, 1))s)" -ForegroundColor Green
        }

        # Build native C++ projects
        Write-Host ""
        Write-Host "  Building native hooks ($msbuildPlatform)..." -NoNewline

        if ($msbuildExe -and (Test-Path $msbuildExe)) {
            foreach ($proj in $NativeProjects) {
                $vcxproj = Get-ChildItem -Path $GVFSRoot -Recurse -Filter "$($proj.Name).vcxproj" | Select-Object -First 1
                if ($vcxproj) {
                    & $msbuildExe $vcxproj.FullName /p:Configuration=$Configuration /p:Platform=$msbuildPlatform /v:minimal /nologo 2>&1 | Out-Null
                    $nativeExe = Join-Path (Split-Path $RepoRoot) "out\$($proj.Name)\bin\$msbuildPlatform\$Configuration\$($proj.Exe)"
                    if (Test-Path $nativeExe) {
                        Copy-Item $nativeExe $OutRoot -Force
                    }
                }
            }
            Write-Host " OK" -ForegroundColor Green
        } else {
            Write-Host " SKIPPED (MSBuild not found)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "--- Skipped build (using existing output) ---" -ForegroundColor DarkGray
    }

    # ─────────────────────────────────────────────────────────────────────
    # Verify layout
    # ─────────────────────────────────────────────────────────────────────
    Write-Host ""
    Write-Host "--- Verifying layout ($rid) ---" -ForegroundColor Yellow

    New-Item -ItemType Directory -Path "$OutRoot\ProgramData\GVFS.Service" -Force | Out-Null

    $icon = Join-Path $GVFSRoot "GVFS\GitVirtualFileSystem.ico"
    if (Test-Path $icon) { Copy-Item $icon $OutRoot -Force }

    "" | Out-File "$OutRoot\OnDiskVersion16CapableInstallation.dat" -Encoding ascii

    $allExes = ($ManagedProjects + $NativeProjects) | ForEach-Object { $_.Exe }
    $missing = @()
    foreach ($exe in $allExes) {
        $path = Join-Path $OutRoot $exe
        if (Test-Path $path) {
            $size = [math]::Round((Get-Item $path).Length / 1MB, 1)
            Write-Host "  [OK] $exe (${size}MB)" -ForegroundColor Green
        } else {
            Write-Host "  [MISSING] $exe" -ForegroundColor Red
            $missing += $exe
        }
    }

    if ($missing.Count -gt 0) {
        Write-Warning "Missing $($missing.Count) executable(s) for $rid."
    } else {
        $totalSize = [math]::Round((Get-ChildItem $OutRoot -File | Measure-Object Length -Sum).Sum / 1MB, 1)
        Write-Host "  Layout: $($allExes.Count) executables, ${totalSize}MB total" -ForegroundColor Cyan
    }

    # ─────────────────────────────────────────────────────────────────────
    # Optional: Build installer
    # ─────────────────────────────────────────────────────────────────────
    if ($BuildInstaller) {
        Write-Host ""
        Write-Host "--- Building Installer ($rid) ---" -ForegroundColor Yellow

        $iscc = $null
        $nugetIscc = Get-ChildItem "$env:USERPROFILE\.nuget\packages\tools.innosetup" -Recurse -Filter "ISCC.exe" -EA 0 | Select-Object -First 1
        if ($nugetIscc) { $iscc = $nugetIscc.FullName }
        if (-not $iscc) {
            $progIscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
            if (Test-Path $progIscc) { $iscc = $progIscc }
        }

        if (-not $iscc) {
            Write-Warning "Inno Setup compiler (ISCC.exe) not found."
        } else {
            $setupIss = Join-Path $GVFSRoot "GVFS.Installers\Setup.iss"
            $archSuffix = $rid.Replace("win-", "")
            $installerOut = Join-Path (Split-Path $OutRoot) "installer-$archSuffix"
            New-Item -ItemType Directory -Path $installerOut -Force | Out-Null

            $versionStr = if (Test-Path "$OutRoot\GVFS.exe") {
                (Get-Item "$OutRoot\GVFS.exe").VersionInfo.ProductVersion
            } else { "0.0.0.0" }

            Write-Host "  Version: $versionStr"
            & $iscc /DLayoutDir="$OutRoot" /DGVFSVersion=$versionStr $setupIss /O"$installerOut" 2>&1 | Out-Null

            if ($LASTEXITCODE -eq 0) {
                $installer = Get-ChildItem $installerOut -Filter "SetupGVFS*.exe" | Select-Object -First 1
                if ($installer) {
                    $instSize = [math]::Round($installer.Length / 1MB, 1)
                    Write-Host "  Installer: $($installer.FullName) (${instSize}MB)" -ForegroundColor Green
                }
            } else {
                Write-Warning "Installer build failed for $rid"
            }
        }
    }

    Write-Host ""
}

Write-Host "=== Done ===" -ForegroundColor Cyan
foreach ($rid in $Runtimes) {
    $dir = if ($Runtimes.Count -gt 1) { "${OutBase}-$($rid.Replace('win-',''))" } else { $OutBase }
    Write-Host "  $rid → $dir"
}
