<#
.SYNOPSIS
    Runs GVFS functional tests using build output, with proper process management.

.DESCRIPTION
    Runs functional tests against build output from out\, starts a test service
    in console mode, and ensures all spawned processes are cleaned up.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Debug.

.EXAMPLE
    .\RunFunctionalTests-Dev.ps1
    .\RunFunctionalTests-Dev.ps1 Debug --test=GVFS.FunctionalTests.Tests.GitCommands.CorruptionReproTests.ReproCherryPickRestoreCorruption
#>
param(
    [Parameter(Position=0)]
    [string]$Configuration = "Debug",

    [Parameter(ValueFromRemainingArguments=$true)]
    [string[]]$RemainingArgs
)

$ErrorActionPreference = "Stop"

# Resolve paths
$scriptsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Split-Path -Parent $scriptsDir
$enlistmentDir = Split-Path -Parent $srcDir
$outDir = Join-Path $enlistmentDir "out"
$payloadDir = Join-Path $outDir "GVFS.Payload\bin\$Configuration\win-x64"
$testExeDir = Join-Path $outDir "GVFS.FunctionalTests\bin\$Configuration\net471\win-x64"
$testExe = Join-Path $testExeDir "GVFS.FunctionalTests.exe"

# Validate
if (!(Test-Path $testExe)) {
    Write-Error "Test executable not found: $testExe. Has the solution been built?"
    exit 1
}
if (!(Test-Path $payloadDir)) {
    Write-Error "Payload directory not found: $payloadDir. Has the solution been built?"
    exit 1
}

# Set up test environment
$env:GVFS_FUNCTIONAL_TEST_DEV_MODE = "1"
$env:GVFS_DEV_OUT_DIR = $outDir
$env:GVFS_DEV_CONFIGURATION = $Configuration
$testDataDir = Join-Path $env:TEMP "GVFS-FunctionalTest"
$env:GVFS_TEST_DATA = $testDataDir
$env:GVFS_COMMON_APPDATA_ROOT = Join-Path $testDataDir "AppData"
$env:GVFS_SECURE_DATA_ROOT = Join-Path $testDataDir "ProgramData"

# Put build output gvfs.exe on PATH
$env:PATH = "$payloadDir;C:\Program Files\Git\cmd;$env:PATH"

Write-Host "============================================"
Write-Host "GVFS Functional Tests - Dev Mode"
Write-Host "============================================"
Write-Host "Configuration:  $Configuration"
Write-Host "Build output:   $outDir"
Write-Host "Test data:      $testDataDir"
Write-Host ""

# Verify versions match
$gvfsVer = [System.Diagnostics.FileVersionInfo]::GetVersionInfo("$payloadDir\GVFS.exe").FileVersion
$hooksVer = [System.Diagnostics.FileVersionInfo]::GetVersionInfo("$payloadDir\GVFS.Hooks.exe").FileVersion
$svcVer = [System.Diagnostics.FileVersionInfo]::GetVersionInfo("$payloadDir\GVFS.Service.exe").FileVersion
Write-Host "GVFS.exe:       $gvfsVer"
Write-Host "GVFS.Hooks.exe: $hooksVer"
Write-Host "GVFS.Service:   $svcVer"
if ($gvfsVer -ne $hooksVer -or $gvfsVer -ne $svcVer) {
    Write-Error "Version mismatch! All components must have the same version. Try a clean rebuild."
    exit 1
}
Write-Host ""

# Build test arguments — pass remaining args through like the .bat did
$testArgs = @("/result:TestResult.xml")
if ($RemainingArgs) {
    $testArgs += $RemainingArgs
}

# Record processes before test so we can identify orphans
$preTestPids = (Get-Process | Where-Object { $_.ProcessName -match "GVFS" }).Id

Write-Host "Running: $testExe $($testArgs -join ' ')"
Write-Host ""

# Run test
$testProcess = Start-Process -FilePath $testExe -ArgumentList $testArgs -PassThru -NoNewWindow -Wait
$exitCode = $testProcess.ExitCode

Write-Host ""
Write-Host "Test process exited with code: $exitCode"

# Read results
$resultFile = Join-Path $scriptsDir "TestResult.xml"
if (Test-Path $resultFile) {
    [xml]$xml = Get-Content $resultFile
    Write-Host ""
    Write-Host "============================================"
    Write-Host "Test Results"
    Write-Host "============================================"
    foreach ($t in $xml.SelectNodes("//test-case")) {
        $status = if ($t.result -eq "Passed") { "PASS" } else { "FAIL" }
        Write-Host "  [$status] $($t.fullname) ($($t.duration)s)"
        if ($t.failure) {
            $msg = $t.failure.message.InnerText
            if ($msg.Length -gt 300) { $msg = $msg.Substring(0, 300) + "..." }
            Write-Host "         $msg"
        }
    }
    $suite = $xml.SelectSingleNode("//test-run")
    if ($suite) {
        Write-Host ""
        Write-Host "  Total: $($suite.total), Passed: $($suite.passed), Failed: $($suite.failed)"
    }
}

# Clean up orphaned GVFS processes spawned by the test
$postTestPids = (Get-Process | Where-Object { $_.ProcessName -match "GVFS" }).Id
$orphanPids = $postTestPids | Where-Object { $_ -notin $preTestPids }
if ($orphanPids) {
    Write-Host ""
    Write-Host "Cleaning up $($orphanPids.Count) orphaned GVFS process(es)..."
    foreach ($pid in $orphanPids) {
        Write-Host "  Stopping PID $pid"
        Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue
    }
}

exit $exitCode
