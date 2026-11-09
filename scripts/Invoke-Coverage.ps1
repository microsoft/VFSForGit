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
    [string]$TestOutcome
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
        & $collector shutdown $build.SessionId
        throw
    }
} else {
    if (-not $TestOutcome) { throw 'Stop requires the test outcome.' }
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    & $collector shutdown $metadata.SessionId
    if ($LASTEXITCODE -ne 0) { throw 'Coverage collector shutdown failed.' }
    [xml]$report = Get-Content -LiteralPath $coveragePath -Raw
    if ([int]$report.coverage.'lines-covered' -le 0) { throw 'Coverage report contains no executed lines.' }
    if ($metadata.Suite -ne 'Unit') {
        & (Join-Path $PSScriptRoot 'Assert-AotCoverage.ps1') -CoveragePath $coveragePath |
            Set-Content -LiteralPath (Join-Path $Directory 'service.json') -Encoding utf8
    }
    if ($metadata.Suite -in @('Unit', 'Functional')) {
        Copy-Item -LiteralPath 'TestResult.xml' -Destination (Join-Path $Directory 'tests.xml')
    }
    $metadata.TestOutcome = $TestOutcome
    $metadata.Complete = $true
    $metadata.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    $metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8
}
