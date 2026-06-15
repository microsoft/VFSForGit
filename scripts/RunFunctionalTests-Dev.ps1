<#
.SYNOPSIS
    Runs GVFS functional tests in dev mode (no admin, no install required).

.DESCRIPTION
    Runs GVFS.FunctionalTests.exe using build output from out\ instead of
    requiring a system-wide GVFS installation. The test harness launches a
    test service as a console process (not a Windows service), so no admin
    privileges are required.

    After the test process exits, any GVFS.Service.exe child processes it
    spawned are killed by PID. This is safe for concurrent runs — each
    invocation only cleans up its own child processes.

.PARAMETER Configuration
    Build configuration: Debug (default) or Release.

.PARAMETER Arch
    Target CPU architecture: x64 (default) or arm64. Selects which
    publish output (win-x64 or win-arm64) to put on PATH and to run.

.PARAMETER ExtraArgs
    Additional arguments passed through to GVFS.FunctionalTests.exe
    (e.g. --test=GVFS.FunctionalTests.Tests.GVFSVerbTests.UnknownVerb)

.EXAMPLE
    .\RunFunctionalTests-Dev.ps1
    .\RunFunctionalTests-Dev.ps1 -Configuration Release
    .\RunFunctionalTests-Dev.ps1 -Configuration Release -Arch arm64
    .\RunFunctionalTests-Dev.ps1 -ExtraArgs "--test=GVFS.FunctionalTests.Tests.GVFSVerbTests.UnknownVerb"
    .\RunFunctionalTests-Dev.ps1 Debug --test=GVFS.FunctionalTests.Tests.EnlistmentPerFixture.WorktreeTests
#>
param(
    [string]$Configuration = "Debug",
    [ValidateSet("x64","arm64")]
    [string]$Arch = "x64",
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$ExtraArgs
)

$ErrorActionPreference = "Stop"

# Resolve paths (mirrors InitializeEnvironment.bat)
$scriptsDir = $PSScriptRoot
$srcDir = Split-Path $scriptsDir -Parent
$enlistmentDir = Split-Path $srcDir -Parent
$outDir = Join-Path $enlistmentDir "out"

# Dev mode environment
$env:GVFS_FUNCTIONAL_TEST_DEV_MODE = "1"
$env:GVFS_DEV_OUT_DIR = $outDir
$env:GVFS_DEV_CONFIGURATION = $Configuration

# Derive a unique service name from the enlistment path so concurrent runs
# from different working directories don't collide on the named pipe.
$hash = [System.BitConverter]::ToString(
    [System.Security.Cryptography.SHA256]::Create().ComputeHash(
        [System.Text.Encoding]::UTF8.GetBytes($enlistmentDir.ToLowerInvariant())
    )
).Replace("-","").Substring(0,8)
$env:GVFS_TEST_SERVICE_NAME = "Test.GVFS.Service.$hash.$PID"

# Isolate test data per enlistment and run
$env:GVFS_TEST_DATA = Join-Path $env:TEMP "GVFS-FunctionalTest-$hash.$PID"
$env:GVFS_COMMON_APPDATA_ROOT = Join-Path $env:GVFS_TEST_DATA "AppData"
$env:GVFS_SECURE_DATA_ROOT = Join-Path $env:GVFS_TEST_DATA "ProgramData"

# Put build output gvfs.exe on PATH. The Payload csproj sets
# <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
# and layout.bat assembles binaries under bin\<Configuration>\win-<arch>\,
# so there is no net<tfm>\publish\ segment for this project.
$payloadDir = Join-Path $outDir "GVFS.Payload\bin\$Configuration\win-$Arch"
if (-not (Test-Path (Join-Path $payloadDir "GVFS.exe"))) {
    Write-Error "Payload GVFS.exe not found at $payloadDir. Has the solution been built for $Arch / $Configuration?"
    exit 1
}
$env:PATH = "$payloadDir;C:\Program Files\Git\cmd;$env:PATH"

Write-Host "============================================"
Write-Host "GVFS Functional Tests - Dev Mode (no admin)"
Write-Host "============================================"
Write-Host "Configuration:       $Configuration"
Write-Host "Architecture:        $Arch"
Write-Host "Build output:        $outDir"
Write-Host "Test service:        $env:GVFS_TEST_SERVICE_NAME"
Write-Host "Test data:           $env:GVFS_TEST_DATA"
Write-Host ""

# Validate prerequisites
$gvfsPath = Get-Command gvfs -ErrorAction SilentlyContinue
if (-not $gvfsPath) {
    Write-Error "Unable to locate gvfs on the PATH. Has the solution been built?"
    exit 1
}
Write-Host "gvfs location:       $($gvfsPath.Source)"

$gitPath = Get-Command git -ErrorAction SilentlyContinue
if (-not $gitPath) {
    Write-Error "Unable to locate git on the PATH."
    exit 1
}
Write-Host "git location:        $($gitPath.Source)"
Write-Host ""

# Build test exe path. The FunctionalTests csproj is a regular AOT-published
# executable, so it ends up under the standard publish layout.
$testExe = Join-Path $outDir "GVFS.FunctionalTests\bin\$Configuration\net10.0-windows10.0.17763.0\win-$Arch\publish\GVFS.FunctionalTests.exe"
if (-not (Test-Path $testExe)) {
    Write-Error "Test executable not found: $testExe`nRun Build.bat first."
    exit 1
}

# Build arguments
$testArgs = @("/result:$(Join-Path $enlistmentDir 'TestResult.xml')")
if ($ExtraArgs) { $testArgs += $ExtraArgs }

Write-Host "Running: $testExe"
Write-Host "  Args:  $($testArgs -join ' ')"
Write-Host ""

# Start the test process and track its PID
$testProc = Start-Process -FilePath $testExe -ArgumentList $testArgs `
    -NoNewWindow -PassThru

try {
    $testProc.WaitForExit()
}
finally {
    # Kill any GVFS.Service.exe that was spawned by our test process.
    # ParentProcessId is set at creation time and doesn't change when the
    # parent exits, so this works even after GVFS.FunctionalTests.exe is gone.
    $orphans = Get-CimInstance Win32_Process -Filter `
        "Name = 'GVFS.Service.exe' AND ParentProcessId = $($testProc.Id)" `
        -ErrorAction SilentlyContinue
    foreach ($orphan in $orphans) {
        Write-Host "Cleaning up test service process (PID $($orphan.ProcessId))..."
        Stop-Process -Id $orphan.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

exit $testProc.ExitCode
