param(
    [Parameter(Mandatory)]
    [string]$InputDirectory,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [Parameter(Mandatory)]
    [string]$SourceDirectory,
    [Parameter(Mandatory)]
    [string]$SourceCommit,
    [Parameter(Mandatory)]
    [string]$Repository,
    [string]$BaseCommit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-ReportXml {
    param([string]$Path)

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Ignore
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return ,$document
    }
    finally {
        $reader.Dispose()
    }
}

function Get-Count {
    param([string]$Value, [string]$Context, [switch]$Positive)

    [long]$count = 0
    if ($Value -notmatch '^\d+$' -or
        -not [long]::TryParse($Value, [ref]$count) -or ($Positive -and $count -eq 0)) {
        throw "Invalid count '$Value' in $Context."
    }
    return $count
}

function Get-WindowsPath {
    param([string]$Path)

    $pathText = $Path.Replace('/', '\')
    if ($pathText -notmatch '^(?:[A-Za-z]:\\|\\\\[^\\]+\\[^\\]+\\)') {
        throw "Expected an absolute Windows source path: '$Path'."
    }
    return [System.IO.Path]::GetFullPath($pathText).TrimEnd('\')
}

function Get-TestTotals {
    param([string]$Path, [string]$WorkerId)

    $document = Read-ReportXml $Path
    $run = $document.SelectSingleNode('/test-run')
    if ($null -eq $run) { throw "Expected an NUnit test-run in $WorkerId." }
    $total = Get-Count $run.GetAttribute('total') "$WorkerId tests" -Positive
    $passed = Get-Count $run.GetAttribute('passed') "$WorkerId passed tests"
    $failed = Get-Count $run.GetAttribute('failed') "$WorkerId failed tests"
    $skipped = Get-Count $run.GetAttribute('skipped') "$WorkerId skipped tests"
    $inconclusive = 0L
    if ($run.HasAttribute('inconclusive')) {
        $inconclusive = Get-Count $run.GetAttribute('inconclusive') "$WorkerId inconclusive tests"
    }
    $errors = 0L
    if ($run.HasAttribute('errors')) {
        $errors = Get-Count $run.GetAttribute('errors') "$WorkerId test errors"
    }
    if ($failed -ne 0 -or $errors -ne 0 -or $run.GetAttribute('result') -cne 'Passed' -or
        $document.SelectNodes('//failure | //*[@result="Failed" or @result="Error"]').Count -ne 0) {
        throw "Failed NUnit tests or fixture errors in $WorkerId."
    }
    $cases = @($document.SelectNodes('//test-case'))
    $casePassed = @($cases | Where-Object { $_.GetAttribute('result') -ceq 'Passed' }).Count
    $caseSkipped = @($cases | Where-Object { $_.GetAttribute('result') -ceq 'Skipped' }).Count
    $caseInconclusive = @($cases | Where-Object { $_.GetAttribute('result') -ceq 'Inconclusive' }).Count
    if ($passed + $failed + $skipped + $inconclusive -ne $total -or $cases.Count -ne $total -or
        $casePassed -ne $passed -or $caseSkipped -ne $skipped -or $caseInconclusive -ne $inconclusive) {
        throw "Inconsistent NUnit test counts in $WorkerId."
    }
    if ($passed + $inconclusive -eq 0) {
        throw "No executed NUnit tests in $WorkerId. Skipped cases are not executed tests."
    }
    return [ordered]@{
        Total = $total
        Executed = $passed + $inconclusive
        Passed = $passed
        Skipped = $skipped
        Inconclusive = $inconclusive
        Failed = $failed
        Errors = $errors
    }
}

function Get-LineStatistics {
    param([bool[]]$Lines)

    $measured = @($Lines).Count
    $hit = @($Lines | Where-Object { $_ }).Count
    return [ordered]@{
        Measured = $measured
        Hit = $hit
        Uncovered = $measured - $hit
        Percentage = if ($measured -eq 0) { $null } else { [Math]::Round(100.0 * $hit / $measured, 2) }
    }
}

function Update-CoverageRates {
    param([System.Xml.XmlElement]$Element)

    # Cobertura method lines repeat class lines. Only class lines define the denominator.
    $lines = if ($Element.LocalName -eq 'class') {
        @($Element.SelectNodes('./lines/line'))
    } else {
        @($Element.SelectNodes('.//class/lines/line'))
    }
    $covered = @($lines | Where-Object { [long]$_.GetAttribute('hits') -gt 0 }).Count
    $rate = if ($lines.Count -eq 0) { '0' } else {
        ($covered / [double]$lines.Count).ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
    }
    $Element.SetAttribute('line-rate', $rate)
    if ($Element.LocalName -eq 'coverage') {
        $Element.SetAttribute('lines-valid', [string]$lines.Count)
        $Element.SetAttribute('lines-covered', [string]$covered)
    }
}

function ConvertFrom-GitPath {
    param([string]$Path)

    if (-not $Path.StartsWith('"')) { return $Path }
    if (-not $Path.EndsWith('"')) { throw "Invalid quoted git path: $Path." }
    $bytes = [System.Collections.Generic.List[byte]]::new()
    $text = $Path.Substring(1, $Path.Length - 2)
    for ($index = 0; $index -lt $text.Length; $index++) {
        if ($text[$index] -eq '\') {
            $index++
            if ($index -ge $text.Length) { throw "Invalid git path escape: $Path." }
            if ($text[$index] -match '[0-7]') {
                $octal = [string]$text[$index]
                for ($digit = 0; $digit -lt 2 -and $index + 1 -lt $text.Length -and $text[$index + 1] -match '[0-7]'; $digit++) {
                    $index++
                    $octal += $text[$index]
                }
                $bytes.Add([Convert]::ToByte($octal, 8))
            } else {
                $escapes = @{ a = 7; b = 8; t = 9; n = 10; v = 11; f = 12; r = 13; '\' = 92; '"' = 34 }
                $key = [string]$text[$index]
                if (-not $escapes.ContainsKey($key)) { throw "Invalid git path escape: $Path." }
                $bytes.Add([byte]$escapes[$key])
            }
        } else {
            $length = if ([char]::IsHighSurrogate($text[$index])) { 2 } else { 1 }
            $bytes.AddRange([System.Text.Encoding]::UTF8.GetBytes($text.Substring($index, $length)))
            $index += $length - 1
        }
    }
    return [System.Text.Encoding]::UTF8.GetString($bytes.ToArray())
}

function Get-ChangedFiles {
    $patch = @(git -C $SourceDirectory diff --unified=0 --no-ext-diff --no-textconv $BaseCommit $SourceCommit --)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read the diff between BaseCommit and the tested SourceCommit.' }
    $files = @{}
    $file = $null
    $inHunk = $false
    $oldRemaining = 0
    $newRemaining = 0
    $newLine = 0
    foreach ($text in $patch) {
        if ($text -match '^diff --git (?<old>"(?:\\.|[^"])*"|a/.*?) (?<new>"(?:\\.|[^"])*"|b/.*)$') {
            $path = (ConvertFrom-GitPath $Matches.new).Substring(2)
            $file = [ordered]@{ Path = $path; Lines = [System.Collections.Generic.HashSet[int]]::new() }
            $files[$path] = $file
            $inHunk = $false
        } elseif ($text -match '^@@ -\d+(?:,(?<oldCount>\d+))? \+(?<start>\d+)(?:,(?<newCount>\d+))? @@') {
            if ($null -eq $file) { throw 'A diff hunk has no source file.' }
            $oldRemaining = if ($Matches.ContainsKey('oldCount')) { [int]$Matches.oldCount } else { 1 }
            $newRemaining = if ($Matches.ContainsKey('newCount')) { [int]$Matches.newCount } else { 1 }
            $newLine = [int]$Matches.start
            $inHunk = $true
        } elseif ($inHunk -and ($oldRemaining -gt 0 -or $newRemaining -gt 0)) {
            if ($text.StartsWith('+')) {
                [void]$file.Lines.Add($newLine)
                $newLine++
                $newRemaining--
            } elseif ($text.StartsWith('-')) {
                $oldRemaining--
            } elseif ($text.StartsWith(' ')) {
                $newLine++
                $newRemaining--
                $oldRemaining--
            } elseif (-not $text.StartsWith('\ No newline')) {
                throw "Invalid diff hunk for $($file.Path)."
            }
        } elseif ($text.StartsWith('+++ ')) {
            $path = ConvertFrom-GitPath $text.Substring(4).TrimEnd("`t")
            if ($path -ne '/dev/null') {
                $oldPath = $file.Path
                $file.Path = $path.Substring(2)
                if ($oldPath -ne $file.Path) {
                    $files.Remove($oldPath)
                    $files[$file.Path] = $file
                }
            }
        }
    }
    return @($files.Values | Sort-Object Path)
}

function Get-ChangedMeasurement {
    param([string]$Path, [int[]]$Numbers, [hashtable]$Index, [string]$Classification)

    $lines = @(
        foreach ($number in $Numbers) {
            $key = "$Path`0$number"
            $entry = $Index[$key]
            [ordered]@{
                Line = $number
                State = if ($Classification -ne 'managed-source') { $Classification }
                    elseif ($null -eq $entry) { 'no-entry/unmeasured-or-non-executable' }
                    elseif ($entry.Hit) { 'hit' } else { 'uncovered' }
                Provenance = if ($null -eq $entry -or $Classification -ne 'managed-source') { @() } else {
                    @($entry.Provenance.Values | Sort-Object WorkerId, Module, SourceDocument)
                }
            }
        }
    )
    $measured = @($lines | Where-Object { $_.State -in @('hit', 'uncovered') })
    $hit = @($measured | Where-Object { $_.State -eq 'hit' }).Count
    return [ordered]@{
        Measured = $measured.Count
        Hit = $hit
        Uncovered = $measured.Count - $hit
        NoEntry = @($lines | Where-Object { $_.State -eq 'no-entry/unmeasured-or-non-executable' }).Count
        Percentage = if ($measured.Count -eq 0) { $null } else { [Math]::Round(100.0 * $hit / $measured.Count, 2) }
        Lines = $lines
    }
}

function Format-Percentage {
    param($Value)
    if ($null -eq $Value) { return 'N/A' }
    return $Value.ToString('0.##', [System.Globalization.CultureInfo]::InvariantCulture) + '%'
}

$SourceDirectory = Get-WindowsPath (Resolve-Path -LiteralPath $SourceDirectory).Path
$InputDirectory = Get-WindowsPath (Resolve-Path -LiteralPath $InputDirectory).Path
$OutputDirectory = Get-WindowsPath $OutputDirectory
if ($OutputDirectory -eq $InputDirectory -or $OutputDirectory -eq $SourceDirectory -or
    $InputDirectory.StartsWith("$OutputDirectory\", [StringComparison]::OrdinalIgnoreCase) -or
    $SourceDirectory.StartsWith("$OutputDirectory\", [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must not contain InputDirectory or SourceDirectory.'
}
[void][System.IO.Directory]::CreateDirectory($OutputDirectory)
$summaryPath = Join-Path $OutputDirectory 'summary.json'

# Invalidate old success reports before any input check can fail.
foreach ($name in @('Summary.md', 'changed-lines.json', 'ChangedLines.md')) {
    $path = Join-Path $OutputDirectory $name
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
[ordered]@{ SchemaVersion = 1; Complete = $false; Repository = $Repository; SourceCommit = $SourceCommit } |
    ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding utf8

try {
    if ($SourceCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'SourceCommit must be a 40-hex tested commit SHA.' }
    if ($BaseCommit -and $BaseCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'BaseCommit must be a 40-hex commit SHA.' }
    $requiredModules = @('GVFS', 'GVFS.Hooks', 'GVFS.Mount', 'GVFS.Service', 'GVFS.Common', 'GVFS.Platform.Windows', 'GVFS.Virtualization')
    $scenarios = @('staging-upgrade', 'clean-upgrade', 'double-staging', 'staging-then-clean', 'mount-safety-deferral', 'unmount-all-triggers-upgrade')
    $expected = @{}
    foreach ($architecture in @('x64', 'arm64')) {
        $expected["unit-$architecture"] = $true
        foreach ($shard in 0..11) { $expected["functional-$architecture-shard-$shard"] = $true }
    }
    foreach ($scenario in $scenarios) { $expected["upgrade-x64-$scenario"] = $true }

    $workers = [System.Collections.Generic.List[object]]::new()
    $seen = @{}
    $functionalHits = @{}
    $indexes = @{ PRODUCT = @{}; UNIT = @{} }
    $changedFiles = if ($BaseCommit) { @(Get-ChangedFiles) } else { @() }
    $changedLineKeys = @{}
    foreach ($file in $changedFiles) {
        foreach ($number in $file.Lines) { $changedLineKeys["$($file.Path)`0$number"] = $true }
    }
    $exclusions = @{}
    $sourceFiles = @{}
    foreach ($source in Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse -Filter '*.cs') {
        $relative = $source.FullName.Substring($SourceDirectory.Length + 1).Replace('\', '/')
        $sourceFiles[$relative] = $source.FullName
    }
    $metadataFiles = @(Get-ChildItem -LiteralPath $InputDirectory -Filter 'metadata.json' -File -Recurse | Sort-Object FullName)
    foreach ($coverageFile in Get-ChildItem -LiteralPath $InputDirectory -Filter 'coverage.cobertura.xml' -File -Recurse) {
        if (-not (Test-Path -LiteralPath (Join-Path $coverageFile.DirectoryName 'metadata.json') -PathType Leaf)) {
            throw "Missing metadata.json beside coverage artifact $($coverageFile.FullName)."
        }
    }
    foreach ($metadataFile in $metadataFiles) {
        $metadata = Get-Content -LiteralPath $metadataFile.FullName -Raw | ConvertFrom-Json -AsHashtable
        if ($metadata -isnot [System.Collections.IDictionary]) { throw "Invalid metadata object in $($metadataFile.FullName)." }
        foreach ($field in @('SchemaVersion', 'Repository', 'SourceCommit', 'SourceRoot', 'SessionId', 'Architecture', 'Configuration', 'Suite', 'Shard', 'Scenario', 'Complete', 'TestOutcome')) {
            if (-not $metadata.ContainsKey($field)) { throw "Missing metadata field $field in $($metadataFile.FullName)." }
        }
        if ($metadata.SchemaVersion -isnot [long] -and $metadata.SchemaVersion -isnot [int]) {
            throw "Invalid metadata SchemaVersion in $($metadataFile.FullName)."
        }
        if ($metadata.SchemaVersion -ne 1) { throw "Unsupported metadata SchemaVersion in $($metadataFile.FullName)." }
        foreach ($field in @('Repository', 'SourceCommit', 'SourceRoot', 'SessionId', 'Architecture', 'Configuration', 'Suite', 'TestOutcome')) {
            if ($metadata[$field] -isnot [string] -or [string]::IsNullOrWhiteSpace($metadata[$field])) {
                throw "Invalid metadata $field in $($metadataFile.FullName)."
            }
        }
        if ($metadata.Repository -ne $Repository) { throw "Foreign repository in $($metadataFile.FullName): $($metadata.Repository)." }
        if ($metadata.SourceCommit -ne $SourceCommit) { throw "SourceCommit mismatch in $($metadataFile.FullName): $($metadata.SourceCommit)." }
        if ($metadata.Configuration -cne 'Release' -or $metadata.Architecture -cnotin @('x64', 'arm64') -or
            $metadata.Suite -cnotin @('Unit', 'Functional', 'Upgrade')) {
            throw "Invalid worker configuration, architecture, or suite in $($metadataFile.FullName)."
        }
        if ($metadata.Shard -isnot [long] -and $metadata.Shard -isnot [int]) { throw "Invalid worker Shard in $($metadataFile.FullName)." }
        $group = "$($metadata.Suite.ToLowerInvariant())-$($metadata.Architecture)"
        if ($metadata.Suite -ceq 'Functional') {
            if ($metadata.Shard -lt 0 -or $metadata.Shard -gt 11 -or $null -ne $metadata.Scenario) {
                throw "Invalid Functional shard or scenario in $($metadataFile.FullName)."
            }
            $workerId = "$group-shard-$($metadata.Shard)"
        } elseif ($metadata.Suite -ceq 'Upgrade') {
            if ($metadata.Architecture -cne 'x64' -or $metadata.Shard -ne -1 -or $metadata.Scenario -cnotin $scenarios) {
                throw "Invalid Upgrade architecture, shard, or scenario in $($metadataFile.FullName)."
            }
            $workerId = "$group-$($metadata.Scenario)"
        } else {
            if ($metadata.Shard -ne -1 -or $null -ne $metadata.Scenario) { throw "Invalid Unit shard or scenario in $($metadataFile.FullName)." }
            $workerId = $group
        }
        if (-not $expected.ContainsKey($workerId)) { throw "Unexpected worker $workerId." }
        if ($seen.ContainsKey($workerId)) { throw "Duplicate worker $workerId." }
        $seen[$workerId] = $true
        if ($metadata.Complete -isnot [bool] -or -not $metadata.Complete) { throw "Incomplete worker $workerId." }
        if ($metadata.TestOutcome -cne 'success') { throw "Worker $workerId has TestOutcome '$($metadata.TestOutcome)', not success." }
        $sourceRoot = Get-WindowsPath $metadata.SourceRoot
        $coveragePath = Join-Path $metadataFile.DirectoryName 'coverage.cobertura.xml'
        if (-not (Test-Path -LiteralPath $coveragePath -PathType Leaf)) { throw "Missing coverage.cobertura.xml for $workerId." }
        $tests = $null
        if ($metadata.Suite -cne 'Upgrade') {
            $testPath = Join-Path $metadataFile.DirectoryName 'tests.xml'
            if (-not (Test-Path -LiteralPath $testPath -PathType Leaf)) { throw "Missing tests.xml for $workerId." }
            $tests = Get-TestTotals $testPath $workerId
        }
        $document = Read-ReportXml $coveragePath
        $coverage = $document.SelectSingleNode('/coverage')
        if ($null -eq $coverage) { throw "Invalid Cobertura document for $workerId." }
        $modules = @{}
        $workerLines = @{}
        foreach ($package in @($coverage.SelectNodes('./packages/package'))) {
            $module = $package.GetAttribute('name')
            if ([string]::IsNullOrWhiteSpace($module)) { throw "Missing assembly name in $workerId." }
            if ($metadata.Suite -cne 'Unit' -and $module -match '(UnitTests|FunctionalTests|^GVFS\.Tests(?:$|\.))') {
                throw "Test assemblies contaminate product worker ${workerId}: $module."
            }
            if (-not $modules.ContainsKey($module)) { $modules[$module] = @{} }
            foreach ($class in @($package.SelectNodes('./classes/class'))) {
                $rawSource = $class.GetAttribute('filename')
                if ([string]::IsNullOrWhiteSpace($rawSource)) { throw "Missing source filename in $workerId ($module)." }
                $sourcePath = Get-WindowsPath $rawSource
                $inside = $sourcePath.StartsWith("$sourceRoot\", [StringComparison]::OrdinalIgnoreCase)
                $documentPath = if ($inside) { '\' + $sourcePath.Substring($sourceRoot.Length + 1) } else { $sourcePath }
                $generated = $documentPath -match '(?i)\\(?:obj|bin|out|publish)\\'
                if ($documentPath -match '(?i)(?:\.g(?:\.i)?|\.generated|\.AssemblyInfo|\.AssemblyAttributes)\.cs$') {
                    $generated = $generated -or -not $inside -or
                        -not $sourceFiles.ContainsKey($sourcePath.Substring($sourceRoot.Length + 1).Replace('\', '/'))
                }
                $external = $documentPath -match '(?i)\\(?:packages|\.nuget|nuget)\\'
                $reason = $null
                $relative = $null
                if ($generated) {
                    $reason = 'generated-document'
                } elseif ($external) {
                    $reason = 'external-document'
                } elseif (-not $inside) {
                    $matchesRepository = $sourcePath -match '(?i)\\GVFS\\'
                    foreach ($candidate in $sourceFiles.Keys) {
                        if ($sourcePath.EndsWith("\" + $candidate.Replace('/', '\'), [StringComparison]::OrdinalIgnoreCase)) {
                            $matchesRepository = $true
                            break
                        }
                    }
                    if ($matchesRepository) { throw "Unmatched repository source in ${workerId}: '$rawSource' does not match SourceRoot '$sourceRoot'." }
                    $reason = 'external-document'
                } else {
                    $relative = $sourcePath.Substring($sourceRoot.Length + 1).Replace('\', '/')
                    $mapped = Get-WindowsPath (Join-Path $SourceDirectory $relative.Replace('/', '\'))
                    if (-not $mapped.StartsWith("$SourceDirectory\", [StringComparison]::OrdinalIgnoreCase)) {
                        throw "Source path escapes SourceDirectory in ${workerId}: $rawSource."
                    }
                    if (-not (Test-Path -LiteralPath $mapped -PathType Leaf)) { throw "Missing repository source in ${workerId}: $relative." }
                    if ([System.IO.Path]::GetExtension($mapped) -ine '.cs') {
                        $reason = 'non-managed-document'
                    } else {
                        $mapped = $sourceFiles[$relative]
                        if ($null -eq $mapped) { throw "Missing repository source in ${workerId}: $relative." }
                        $relative = $mapped.Substring($SourceDirectory.Length + 1).Replace('\', '/')
                    }
                }
                if ($null -ne $reason) {
                    $key = "$workerId`0$sourcePath"
                    if (-not $exclusions.ContainsKey($key)) {
                        $exclusions[$key] = [ordered]@{ WorkerId = $workerId; Suite = $metadata.Suite; Architecture = $metadata.Architecture; SourceDocument = $rawSource; Reason = $reason; Modules = @{} }
                    }
                    $exclusions[$key].Modules[$module] = $true
                    [void]$class.ParentNode.RemoveChild($class)
                    continue
                }
                $class.SetAttribute('filename', $mapped)
                $scope = if ($metadata.Suite -ceq 'Unit') { 'UNIT' } else { 'PRODUCT' }
                foreach ($line in @($class.SelectNodes('./lines/line'))) {
                    $number = Get-Count $line.GetAttribute('number') "$workerId ($relative) line number" -Positive
                    $hits = Get-Count $line.GetAttribute('hits') "$workerId ($relative) line hits"
                    $key = "$relative`0$number"
                    $workerLines[$key] = $workerLines[$key] -eq $true -or $hits -gt 0
                    $modules[$module][$key] = $modules[$module][$key] -eq $true -or $hits -gt 0
                    if (-not $indexes[$scope].ContainsKey($key)) {
                        $indexes[$scope][$key] = [ordered]@{ Path = $relative; Line = $number; Hit = $false; Provenance = @{} }
                    }
                    $entry = $indexes[$scope][$key]
                    $entry.Hit = $entry.Hit -or $hits -gt 0
                    # Unchanged lines need only hit unions. Detailed provenance stays bounded to the diff.
                    if ($changedLineKeys.ContainsKey($key)) {
                        $provenanceKey = "$workerId`0$module`0$sourcePath"
                        if (-not $entry.Provenance.ContainsKey($provenanceKey)) {
                            $entry.Provenance[$provenanceKey] = [ordered]@{
                                WorkerId = $workerId; Suite = $metadata.Suite; Architecture = $metadata.Architecture
                                Module = $module; SourceDocument = $rawSource; Hit = $false
                            }
                        }
                        $entry.Provenance[$provenanceKey].Hit = $entry.Provenance[$provenanceKey].Hit -or $hits -gt 0
                    }
                    if ($metadata.Suite -ceq 'Functional' -and $hits -gt 0) { $functionalHits[$module] = $true }
                }
                Update-CoverageRates $class
            }
            if ($package.SelectNodes('./classes/class').Count -eq 0) {
                [void]$package.ParentNode.RemoveChild($package)
            } else {
                Update-CoverageRates $package
            }
        }
        $statistics = Get-LineStatistics @($workerLines.Values)
        if ($statistics.Hit -eq 0) { throw "No executed repository source coverage in $workerId." }
        Update-CoverageRates $coverage
        $sources = $coverage.SelectSingleNode('./sources')
        if ($null -ne $sources) { [void]$coverage.RemoveChild($sources) }
        $sources = $document.CreateElement('sources')
        $sourceNode = $document.CreateElement('source')
        $sourceNode.InnerText = $SourceDirectory
        [void]$sources.AppendChild($sourceNode)
        [void]$coverage.PrependChild($sources)
        $moduleSummary = @(
            foreach ($module in @($modules.Keys | Sort-Object)) {
                [ordered]@{ Name = $module; Coverage = Get-LineStatistics @($modules[$module].Values) }
            }
        )
        $worker = [ordered]@{
            WorkerId = $workerId; Group = $group; Suite = $metadata.Suite; Architecture = $metadata.Architecture
            Shard = $metadata.Shard; Scenario = $metadata.Scenario; SessionId = $metadata.SessionId
            SourceRoot = $metadata.SourceRoot; TestOutcome = $metadata.TestOutcome; Complete = $true
            Tests = $tests; Coverage = $statistics; Modules = $moduleSummary
            NormalizedReport = "normalized\$group\$workerId.cobertura.xml"; Document = $document
        }
        foreach ($field in @('CollectorVersion', 'StartedUtc', 'CompletedUtc')) {
            if ($metadata.ContainsKey($field)) { $worker[$field] = $metadata[$field] }
        }
        $workers.Add($worker)
    }
    $missing = @($expected.Keys | Where-Object { -not $seen.ContainsKey($_) } | Sort-Object)
    if ($missing.Count -ne 0) { throw "Missing workers ($($seen.Count)/32): $($missing -join ', ')." }
    foreach ($module in $requiredModules) {
        if (-not $functionalHits.ContainsKey($module)) { throw "No executed Functional source coverage in required product module $module." }
    }

    $changed = [ordered]@{
        SchemaVersion = 1; Repository = $Repository; SourceCommit = $SourceCommit; BaseCommit = $BaseCommit
        Available = [bool]$BaseCommit
        Reason = if ($BaseCommit) { $null } else { 'Changed-line analysis is unavailable because BaseCommit is absent.' }
        Scopes = [ordered]@{ PRODUCT = 'Functional + Upgrade, managed NativeAOT product execution'; UNIT = 'Unit execution, including source-linked production code' }
        Files = @()
    }
    if ($BaseCommit) {
        $changed.Files = @(
            foreach ($file in $changedFiles) {
                $path = $file.Path
                if ($path -match '(?i)(?:^|/)(?:GVFS\.)?(?:UnitTests|FunctionalTests|Tests|NativeTests)(?:/|$)') {
                    $classification = 'intentionally-excluded-test-code'
                } elseif ($path -match '(?i)\.(?:c|cc|cpp|cxx|h|hh|hpp|hxx|inl|inc|m|mm)$') {
                    $classification = 'unsupported-native'
                } elseif ($path -match '(?i)\.cs$') {
                    $classification = 'managed-source'
                } else {
                    $classification = 'not-applicable'
                }
                $numbers = @($file.Lines | Sort-Object)
                [ordered]@{
                    Path = $path; Classification = $classification; AddedModifiedLines = $numbers
                    PRODUCT = Get-ChangedMeasurement $path $numbers $indexes.PRODUCT $classification
                    UNIT = Get-ChangedMeasurement $path $numbers $indexes.UNIT $classification
                }
            }
        )
    }
    $groups = @(
        foreach ($group in @('unit-x64', 'unit-arm64', 'functional-x64', 'functional-arm64', 'upgrade-x64')) {
            $members = @($workers | Where-Object { $_.Group -eq $group })
            $expectedCount = if ($group.StartsWith('unit-')) { 1 } elseif ($group.StartsWith('functional-')) { 12 } else { 6 }
            $totals = $null
            if (-not $group.StartsWith('upgrade-')) {
                $totals = [ordered]@{}
                foreach ($name in @('Total', 'Executed', 'Passed', 'Skipped', 'Inconclusive', 'Failed', 'Errors')) {
                    $totals[$name] = [long](($members | ForEach-Object { $_.Tests[$name] } | Measure-Object -Sum).Sum)
                }
            }
            [ordered]@{
                Group = $group; Suite = $members[0].Suite; Architecture = $members[0].Architecture
                Workers = $members.Count; ExpectedWorkers = $expectedCount; Tests = $totals
                TestEvidence = if ($null -eq $totals) { 'Scenario outcome from metadata. No NUnit test count is inferred.' } else { 'NUnit cases. Skipped cases are not executed tests.' }
            }
        }
    )
    $excludedDocuments = @(
        foreach ($entry in @($exclusions.Values | Sort-Object WorkerId, SourceDocument)) {
            $entry.Modules = @($entry.Modules.Keys | Sort-Object)
            $entry
        }
    )
    $limitations = @(
        'Native C++ coverage is unsupported.'
        'Branch coverage availability is not assumed. These summaries use class source lines.'
        'Unit-linked source coverage is not deployed product execution.'
        'Lines without coverage entries are unmeasured or non-executable, not inferred covered.'
        'No coverage thresholds apply.'
    )
    $summary = [ordered]@{
        SchemaVersion = 1; Complete = $true; Repository = $Repository; SourceCommit = $SourceCommit
        Configuration = 'Release'; SourceDirectory = $SourceDirectory
        Workers = $workers.Count; ExpectedWorkers = 32; Groups = $groups
        Scopes = $changed.Scopes
        Measurements = [ordered]@{
            PRODUCT = Get-LineStatistics @($indexes.PRODUCT.Values | ForEach-Object { $_.Hit })
            UNIT = Get-LineStatistics @($indexes.UNIT.Values | ForEach-Object { $_.Hit })
        }
        RequiredFunctionalModulesWithHits = $requiredModules
        ExcludedDocumentCount = $excludedDocuments.Count; ExcludedDocuments = $excludedDocuments
        Limitations = $limitations
        WorkerReports = @($workers | Sort-Object WorkerId | ForEach-Object {
            $report = [ordered]@{}
            foreach ($key in $_.Keys) { if ($key -ne 'Document') { $report[$key] = $_[$key] } }
            $report
        })
    }
    $markdown = [System.Collections.Generic.List[string]]::new()
    $markdown.Add('# Managed-source coverage')
    $markdown.Add('')
    $markdown.Add("Workers: $($workers.Count)/32. Tested commit: ``$SourceCommit``.")
    foreach ($scope in @('PRODUCT', 'UNIT')) {
        $measurement = $summary.Measurements[$scope]
        $markdown.Add("$scope`: $($changed.Scopes[$scope]). $($measurement.Hit)/$($measurement.Measured) measured lines have hits ($(Format-Percentage $measurement.Percentage)).")
    }
    $markdown.Add('')
    $markdown.Add('| Suite / architecture | Workers | Passed | Skipped | Executed |')
    $markdown.Add('| --- | --- | --- | --- | --- |')
    foreach ($group in $groups) {
        $counts = if ($null -eq $group.Tests) { 'N/A | N/A | N/A' } else {
            "$($group.Tests.Passed) | $($group.Tests.Skipped) | $($group.Tests.Executed)"
        }
        $markdown.Add("| $($group.Suite) / $($group.Architecture) | $($group.Workers)/$($group.ExpectedWorkers) | $counts |")
    }
    $markdown.Add('')
    $markdown.Add("Excluded generated/external/non-managed documents: $($excludedDocuments.Count). See summary.json for documents and original module provenance.")
    $markdown.Add('Upgrade metadata records scenario outcomes, not NUnit case counts.')
    foreach ($limitation in $limitations) { $markdown.Add("- $limitation") }

    $changedMarkdown = [System.Collections.Generic.List[string]]::new()
    $changedMarkdown.Add('# Changed source lines')
    $changedMarkdown.Add('')
    if (-not $BaseCommit) {
        $changedMarkdown.Add($changed.Reason)
    } else {
        $changedMarkdown.Add("Diff: ``$BaseCommit`` to tested commit ``$SourceCommit``.")
        $changedMarkdown.Add('PRODUCT uses Functional + Upgrade. UNIT uses unit execution. These measurements remain separate.')
        $changedMarkdown.Add('Percentages use measured changed lines only. N/A means that no changed lines have coverage entries.')
        $changedMarkdown.Add('No-entry means unmeasured or non-executable. It does not mean covered.')
        $changedMarkdown.Add('')
        $changedMarkdown.Add('| File | Scope | Measured | Hit | Uncovered | No-entry | Percentage | Classification |')
        $changedMarkdown.Add('| --- | --- | --- | --- | --- | --- | --- | --- |')
        foreach ($file in $changed.Files) {
            foreach ($scope in @('PRODUCT', 'UNIT')) {
                $measurement = $file[$scope]
                $displayPath = $file.Path.Replace('|', '\|').Replace("`n", ' ').Replace("`r", ' ')
                $changedMarkdown.Add("| $displayPath | $scope | $($measurement.Measured) | $($measurement.Hit) | $($measurement.Uncovered) | $($measurement.NoEntry) | $(Format-Percentage $measurement.Percentage) | $($file.Classification) |")
            }
        }
        $changedMarkdown.Add('')
        $changedMarkdown.Add('changed-lines.json lists new-side line numbers, per-line states, and source/module/worker provenance.')
    }
    $normalized = Join-Path $OutputDirectory 'normalized'
    if (Test-Path -LiteralPath $normalized) { Remove-Item -LiteralPath $normalized -Recurse -Force }
    foreach ($worker in @($workers | Sort-Object WorkerId)) {
        $path = Join-Path $OutputDirectory $worker.NormalizedReport
        [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $path))
        $worker.Document.Save($path)
    }
    $changed | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'changed-lines.json') -Encoding utf8
    $changedMarkdown | Set-Content -LiteralPath (Join-Path $OutputDirectory 'ChangedLines.md') -Encoding utf8
    $markdown | Set-Content -LiteralPath (Join-Path $OutputDirectory 'Summary.md') -Encoding utf8
    $summary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $summaryPath -Encoding utf8
}
catch {
    foreach ($name in @('Summary.md', 'changed-lines.json', 'ChangedLines.md')) {
        $path = Join-Path $OutputDirectory $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    [ordered]@{
        SchemaVersion = 1; Complete = $false; Repository = $Repository; SourceCommit = $SourceCommit
        Error = $_.Exception.Message
    } | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding utf8
    throw
}
