[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("x64", "arm64")]
    [string]$Arch = "x64"
)

$ErrorActionPreference = "Stop"
$enlistmentDir = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$resultPath = Join-Path $enlistmentDir "LocalComponentTestResult.xml"
if (Test-Path -LiteralPath $resultPath) {
    Remove-Item -LiteralPath $resultPath
}

& (Join-Path $PSScriptRoot "RunFunctionalTests-Dev.ps1") -Configuration $Configuration -Arch $Arch `
    -ExtraArgs "--test=GVFS.FunctionalTests.Tests.LocalComponents", "--result=$resultPath"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

[xml]$result = Get-Content -LiteralPath $resultPath -Raw
$cases = @($result.SelectNodes("//test-case"))
if ($cases.Count -eq 0) {
    throw "No local component tests were selected."
}
foreach ($case in $cases) {
    if (-not $case.fullname.StartsWith("GVFS.FunctionalTests.Tests.LocalComponents.", [StringComparison]::Ordinal)) {
        throw "Unexpected test in the local component run: $($case.fullname)"
    }
    if ($case.result -ne "Passed") {
        throw "Local component test did not pass: $($case.fullname) ($($case.result))"
    }
}
