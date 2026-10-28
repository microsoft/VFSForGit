param(
    [Parameter(Mandatory)]
    [string]$CoveragePath
)

$ErrorActionPreference = 'Stop'

[xml]$coverage = Get-Content -LiteralPath $CoveragePath -Raw
$packages = @($coverage.coverage.packages.package)
$requiredModules = @(
    'GVFS',
    'GVFS.Hooks',
    'GVFS.Mount',
    'GVFS.Service',
    'GVFS.Common',
    'GVFS.Platform.Windows',
    'GVFS.Virtualization'
)

foreach ($name in $requiredModules) {
    $module = @($packages | Where-Object { $_.name -eq $name })
    if ($module.Count -ne 1) {
        throw "Expected one coverage module for $name; found $($module.Count)."
    }

    $hitLines = @($module[0].classes.class.lines.line | Where-Object { [int]$_.hits -gt 0 })
    if ($hitLines.Count -eq 0) {
        throw "No executed lines in required product module $name."
    }
}

$testModules = @($packages | Where-Object { $_.name -match '(UnitTests|FunctionalTests|^GVFS.Tests$)' })
if ($testModules.Count -ne 0) {
    throw "Test assemblies contaminate the product report: $($testModules.name -join ', ')."
}

$service = $packages | Where-Object { $_.name -eq 'GVFS.Service' }
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
    Scope = 'Managed NativeAOT product; x64 shard 0 of 12'
    Modules = $packages.name
    ScmServiceStartupCovered = $true
    ConsoleServiceEntryCovered = $false
    NativeCppCovered = $false
    CompleteSuiteCoverage = $false
    CoveredLines = [int]$coverage.coverage.'lines-covered'
    MeasuredLines = [int]$coverage.coverage.'lines-valid'
} | ConvertTo-Json -Depth 3
