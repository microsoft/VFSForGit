param(
    [Parameter(Mandatory)]
    [string]$CoveragePath
)

$ErrorActionPreference = 'Stop'

[xml]$coverage = Get-Content -LiteralPath $CoveragePath -Raw
$packages = @($coverage.coverage.packages.package)
$service = @($packages | Where-Object { $_.name -eq 'GVFS.Service' })
if ($service.Count -ne 1) {
    throw "Expected one coverage module for GVFS.Service; found $($service.Count)."
}

$testModules = @($packages | Where-Object { $_.name -match '(UnitTests|FunctionalTests|^GVFS.Tests$)' })
if ($testModules.Count -ne 0) {
    throw "Test assemblies contaminate the product report: $($testModules.name -join ', ')."
}

$serviceClass = $service.classes.class | Where-Object { $_.name -eq 'GVFS.Service.GVFSService' }
$onStart = @($serviceClass.methods.method | Where-Object { $_.name -eq 'OnStart' })
$startupHits = @($onStart.lines.line | Where-Object { [int]$_.hits -gt 0 })
if ($startupHits.Count -eq 0) {
    throw 'The report contains no executed GVFSService.OnStart lines.'
}

$program = $service.classes.class | Where-Object { $_.name -eq 'GVFS.Service.Program' }
$consoleEntry = @($program.methods.method | Where-Object { $_.name -eq 'RunAsConsoleApp' })
$consoleHits = @($consoleEntry.lines.line | Where-Object { [int]$_.hits -gt 0 })
if ($consoleHits.Count -ne 0) {
    throw 'Console-service execution contaminates the SCM-service probe.'
}

[ordered]@{
    Scope = 'SCM service execution'
    Modules = $packages.name
    ScmServiceStartupCovered = $true
    ConsoleServiceEntryCovered = $false
    NativeCppCovered = $false
    CompleteSuiteCoverage = $false
    CoveredLines = [int]$coverage.coverage.'lines-covered'
    MeasuredLines = [int]$coverage.coverage.'lines-valid'
} | ConvertTo-Json -Depth 3
