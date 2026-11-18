param(
    [Parameter(Mandatory)]
    [ValidateSet('Start', 'Stop')]
    [string]$Action,
    [Parameter(Mandatory)]
    [string]$Directory,
    [string]$BuildManifest,
    [ValidateSet('Unit', 'Functional', 'Upgrade')]
    [string]$Suite,
    [int]$Shard = -1,
    [string]$Scenario,
    [ValidateSet('success', 'failure', 'skipped', 'cancelled')]
    [string]$TestOutcome,
    [ValidateRange(1, 120)]
    [int]$ShutdownTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$collector = (Get-Command Microsoft.CodeCoverage.Console.exe -ErrorAction Stop).Source
$metadataPath = Join-Path $Directory 'metadata.json'
$coveragePath = Join-Path $Directory 'coverage.cobertura.xml'

if ($Action -eq 'Start') {
    if (-not $BuildManifest -or -not $Suite) { throw 'Start requires a build manifest and suite.' }
    $build = Get-Content -LiteralPath $BuildManifest -Raw | ConvertFrom-Json
    if ($build.SchemaVersion -ne 1 -or $build.SourceCommit -notmatch '^[a-f0-9]{40}$' -or
        $build.Architecture -notin @('x64', 'arm64') -or $build.SessionId -notmatch '^[\w.-]+$' -or -not $build.SourceRoot -or $build.Configuration -ne 'Release') {
        throw 'Invalid coverage build manifest.'
    }
    $sourceCommit = git -C (Split-Path -Parent $PSScriptRoot) rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $sourceCommit -ne $build.SourceCommit) {
        throw 'Collector source checkout does not match the compiled manifest.'
    }
    if ($Suite -eq 'Functional' -and ($Shard -lt 0 -or $Shard -ge 12)) { throw 'Invalid functional shard.' }
    if ($Suite -eq 'Upgrade' -and -not $Scenario) { throw 'Upgrade coverage requires a scenario.' }
    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    $collectorVersion = (& $collector --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the coverage collector version.' }
    $metadata = [ordered]@{
        SchemaVersion = 1
        Repository = $build.Repository
        SourceCommit = $build.SourceCommit
        SourceRoot = $build.SourceRoot
        SessionId = $build.SessionId
        Architecture = $build.Architecture
        Configuration = $build.Configuration
        Suite = $Suite
        Shard = $Shard
        Scenario = $Scenario
        Complete = $false
        TestOutcome = $null
        Collector = $collector
        CollectorVersion = $collectorVersion
        StartedUtc = [DateTime]::UtcNow.ToString('o')
        CompletedUtc = $null
    }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8
    $configuration = Join-Path $PSScriptRoot $(if ($Suite -eq 'Unit') { 'UnitCoverage.config' } else { 'AotCoverage.config' })
    & $collector collect --server-mode --background --session-id $build.SessionId --settings $configuration --output $coveragePath --output-format cobertura --log-file (Join-Path $Directory 'collector.log') --log-level Info
    if ($LASTEXITCODE -ne 0) { throw 'Coverage collector startup failed.' }
    try {
        & $collector snapshot $build.SessionId --output (Join-Path $Directory 'before-tests.coverage')
        if ($LASTEXITCODE -ne 0) { throw 'Coverage collector readiness check failed.' }
    } catch {
        & $collector shutdown $build.SessionId --timeout ($ShutdownTimeoutSeconds * 1000) 2>&1 |
            Tee-Object -FilePath (Join-Path $Directory 'shutdown.log')
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Coverage cleanup failed after the readiness failure. See shutdown.log.' }
        throw
    }
} else {
    if (-not $TestOutcome) { throw 'Stop requires the test outcome.' }
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    $metadata.Complete = $false
    $metadata.CompletedUtc = $null
    $metadata.TestOutcome = $TestOutcome
    $metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8
    if ($metadata.Suite -in @('Unit', 'Functional')) {
        if (Test-Path -LiteralPath 'TestResult.xml') {
            Copy-Item -LiteralPath 'TestResult.xml' -Destination (Join-Path $Directory 'tests.xml')
        } elseif ($TestOutcome -ne 'success') {
            Write-Warning "The test outcome is '$TestOutcome'; TestResult.xml is unavailable."
        }
    }
    & $collector shutdown $metadata.SessionId --timeout ($ShutdownTimeoutSeconds * 1000) 2>&1 |
        Tee-Object -FilePath (Join-Path $Directory 'shutdown.log')
    if ($LASTEXITCODE -ne 0) {
        $message = "Coverage collector shutdown failed (exit $LASTEXITCODE, IPC timeout ${ShutdownTimeoutSeconds}s). See shutdown.log and collector.background.log."
        $metadata | Add-Member -NotePropertyName StopError -NotePropertyValue $message -Force
        $metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8
        throw "$message The test outcome remains '$TestOutcome'; coverage is incomplete."
    }
    if ($metadata.Suite -in @('Unit', 'Functional') -and $TestOutcome -eq 'success' -and -not (Test-Path -LiteralPath 'TestResult.xml')) {
        throw 'The successful test run produced no TestResult.xml.'
    }
    [xml]$report = Get-Content -LiteralPath $coveragePath -Raw
    if ([int]$report.coverage.'lines-covered' -le 0) { throw 'Coverage report contains no executed lines.' }
    if ($metadata.Suite -ne 'Unit') {
        & (Join-Path $PSScriptRoot 'Assert-AotCoverage.ps1') -CoveragePath $coveragePath |
            Set-Content -LiteralPath (Join-Path $Directory 'service.json') -Encoding utf8
    }
    $metadata.Complete = $true
    $metadata.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    $metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8
}
