BeforeAll {
    $mergeScript = Join-Path $PSScriptRoot 'Merge-Coverage.ps1'
    $modules = @('GVFS', 'GVFS.Hooks', 'GVFS.Mount', 'GVFS.Service', 'GVFS.Common', 'GVFS.Platform.Windows', 'GVFS.Virtualization')
    $scenarios = @('staging-upgrade', 'clean-upgrade', 'double-staging', 'staging-then-clean', 'mount-safety-deferral', 'unmount-all-triggers-upgrade')
    $commit = 'a' * 40
    $repository = 'microsoft/VFSForGit'
    $runnerRoot = 'D:\a\VFSForGit\VFSForGit\src'

    function New-Worker {
        param([string]$Suite, [string]$Architecture, [int]$Shard = -1, [string]$Scenario)

        $id = "$($Suite.ToLowerInvariant())-$Architecture"
        if ($Suite -eq 'Functional') { $id += "-shard-$Shard" }
        if ($Suite -eq 'Upgrade') { $id += "-$Scenario" }
        $directory = Join-Path $inputPath $id
        [void][System.IO.Directory]::CreateDirectory($directory)
        [ordered]@{
            SchemaVersion = 1; Repository = $repository; SourceCommit = $commit; SourceRoot = $runnerRoot
            SessionId = "session-$id"; Architecture = $Architecture; Configuration = 'Release'
            Suite = $Suite; Shard = $Shard; Scenario = if ($Suite -eq 'Upgrade') { $Scenario } else { $null }
            Complete = $true; TestOutcome = 'success'
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'metadata.json')
        $workerModules = if ($Suite -eq 'Unit') { @('GVFS.UnitTests', 'GVFS.Common') }
            elseif ($Suite -eq 'Upgrade') { @('GVFS.Service') } else { $modules }
        $packages = foreach ($module in $workerModules) {
            $secondHits = if ($Suite -eq 'Unit') { 1 } else { 0 }
            $firstHits = 1
            if ($Suite -eq 'Unit' -and $module -eq 'GVFS.Common') {
                $firstHits = 0
                $secondHits = 0
            }
            @"
<package name="$module" line-rate="1"><classes>
<class name="$module.Policy" filename="$runnerRoot\GVFS\GVFS.Common\Policy.cs" line-rate="1">
<methods><method name="Method"><lines><line number="2" hits="99"/></lines></method></methods>
<lines><line number="1" hits="$firstHits"/><line number="2" hits="$secondHits"/></lines></class>
</classes></package>
"@
        }
        [xml]$xml = "<coverage lines-covered='999' lines-valid='999' line-rate='1'><sources><source>$runnerRoot</source></sources><packages>$($packages -join '')</packages></coverage>"
        $xml.Save((Join-Path $directory 'coverage.cobertura.xml'))
        if ($Suite -ne 'Upgrade') {
            @'
<test-run result="Passed" total="2" passed="1" failed="0" skipped="1" inconclusive="0">
  <test-suite result="Passed"><test-case name="Pass" result="Passed"/>
    <test-suite result="Skipped" label="Ignored"><test-case name="FixtureSkip" result="Skipped" label="Ignored"/></test-suite>
  </test-suite>
</test-run>
'@ | Set-Content -LiteralPath (Join-Path $directory 'tests.xml')
        }
    }

    function New-Plan {
        foreach ($architecture in @('x64', 'arm64')) {
            New-Worker Unit $architecture
            foreach ($shard in 0..11) { New-Worker Functional $architecture -Shard $shard }
        }
        foreach ($scenario in $scenarios) { New-Worker Upgrade x64 -Scenario $scenario }
    }

    function Invoke-Merge {
        param([string]$Base)
        $arguments = @{
            InputDirectory = $inputPath; OutputDirectory = $outputPath; SourceDirectory = $sourcePath
            SourceCommit = $commit; Repository = $repository
        }
        if ($Base) { $arguments.BaseCommit = $Base }
        & $mergeScript @arguments
    }

    function Set-WorkerMetadata {
        param([string]$Id, [string]$Field, $Value)
        $path = Join-Path $inputPath "$Id\metadata.json"
        $metadata = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $metadata[$Field] = $Value
        $metadata | ConvertTo-Json | Set-Content -LiteralPath $path
    }

    function Edit-WorkerCoverage {
        param([string]$Id, [scriptblock]$Edit)
        $path = Join-Path $inputPath "$Id\coverage.cobertura.xml"
        [xml]$document = Get-Content -LiteralPath $path -Raw
        & $Edit $document
        $document.Save($path)
    }

    function Invoke-TestGit {
        param([string[]]$Arguments)
        $result = git -C $sourcePath @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Fixture git command failed: $result" }
        return $result
    }
}

Describe 'Managed-source coverage aggregation integrity' {
    BeforeEach {
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $inputPath = Join-Path $root 'input'
        $outputPath = Join-Path $root 'output'
        $sourcePath = Join-Path $root 'src'
        [void][System.IO.Directory]::CreateDirectory($inputPath)
        [void][System.IO.Directory]::CreateDirectory((Join-Path $sourcePath 'GVFS\GVFS.Common'))
        "first`nsecond`nthird" | Set-Content -LiteralPath (Join-Path $sourcePath 'GVFS\GVFS.Common\Policy.cs')
        New-Plan
    }

    It 'Accepts exactly 32 workers, keeps assembly attribution, and counts fixture skips without execution' {
        Set-WorkerMetadata unit-x64 CollectorVersion '18.8.0'
        Set-WorkerMetadata unit-x64 StartedUtc '2026-11-05T20:00:00Z'
        Set-WorkerMetadata unit-x64 CompletedUtc '2026-11-05T20:01:00Z'
        Invoke-Merge
        $summary = Get-Content -LiteralPath (Join-Path $outputPath 'summary.json') -Raw | ConvertFrom-Json
        $summary.Complete | Should -BeTrue
        $summary.Workers | Should -Be 32
        $summary.ExpectedWorkers | Should -Be 32
        $summary.Groups.Count | Should -Be 5
        foreach ($group in @($summary.Groups | Where-Object Suite -eq 'Functional')) {
            $group.Workers | Should -Be 12
            $group.ExpectedWorkers | Should -Be 12
            $group.Tests.Passed | Should -Be 12
            $group.Tests.Skipped | Should -Be 12
            $group.Tests.Executed | Should -Be 12
        }
        $summary.Groups[0].Tests.Passed | Should -Be 1
        $summary.Groups[0].Tests.Skipped | Should -Be 1
        $summary.Groups[4].Tests | Should -BeNullOrEmpty
        $summary.Measurements.PRODUCT.Measured | Should -Be 2
        $summary.Measurements.PRODUCT.Hit | Should -Be 1
        $summary.Measurements.UNIT.Measured | Should -Be 2
        $summary.Measurements.UNIT.Hit | Should -Be 2
        $summary.Measurements.COMBINED.Measured | Should -Be 2
        $summary.Measurements.COMBINED.Hit | Should -Be 2
        ($summary.WorkerReports | Where-Object WorkerId -eq 'unit-x64').Modules[0].Coverage.Hit | Should -Be 0
        ($summary.WorkerReports | Where-Object WorkerId -eq 'unit-x64').CollectorVersion | Should -Be '18.8.0'
        $completedUtc = [datetime]($summary.WorkerReports | Where-Object WorkerId -eq 'unit-x64').CompletedUtc
        $completedUtc.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') | Should -Be '2026-11-05T20:01:00Z'
        @(Get-ChildItem -LiteralPath (Join-Path $outputPath 'normalized') -Filter '*.xml' -Recurse).Count | Should -Be 32
        [xml]$unit = Get-Content -LiteralPath (Join-Path $outputPath 'normalized\unit-x64\unit-x64.cobertura.xml') -Raw
        @($unit.coverage.packages.package.name) | Should -Contain 'GVFS.UnitTests'
        $unit.coverage.'lines-valid' | Should -Be '4'
        $unit.SelectSingleNode('//method/lines/line').hits | Should -Be '99'
        (Get-Content -LiteralPath (Join-Path $outputPath 'Summary.md') -Raw) | Should -Match '12/12'
        $changed = Get-Content -LiteralPath (Join-Path $outputPath 'changed-lines.json') -Raw | ConvertFrom-Json
        $changed.Available | Should -BeFalse
        (Get-Content -LiteralPath (Join-Path $outputPath 'ChangedLines.md') -Raw) | Should -Match 'unavailable because BaseCommit is absent'
        (Get-Content -LiteralPath (Join-Path $outputPath 'ChangedLines.html') -Raw) | Should -Match 'unavailable because BaseCommit is absent'
        $before = Get-Content -LiteralPath (Join-Path $outputPath 'summary.json') -Raw
        Invoke-Merge
        (Get-Content -LiteralPath (Join-Path $outputPath 'summary.json') -Raw) | Should -BeExactly $before
    }

    It 'Resolves relative caller directories from the PowerShell location and creates a nonexistent output directory' {
        Push-Location -LiteralPath $root
        try {
            foreach ($relativeOutput in @('coverage-report', '.\reports\nested\..\coverage-report')) {
                Test-Path -LiteralPath $relativeOutput | Should -BeFalse
                & $mergeScript -InputDirectory '.\input' -OutputDirectory $relativeOutput -SourceDirectory 'src' `
                    -SourceCommit $commit -Repository $repository
                $resolvedOutput = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($relativeOutput)
                $summary = Get-Content -LiteralPath (Join-Path $resolvedOutput 'summary.json') -Raw | ConvertFrom-Json
                $summary.Complete | Should -BeTrue
                $summary.Workers | Should -Be 32
                $summary.SourceDirectory | Should -Be $sourcePath
                @(Get-ChildItem -LiteralPath (Join-Path $resolvedOutput 'normalized') -Filter '*.xml' -Recurse).Count | Should -Be 32
                [xml]$normalized = Get-Content -LiteralPath (Join-Path $resolvedOutput 'normalized\unit-x64\unit-x64.cobertura.xml') -Raw
                $normalized.SelectSingleNode('//class').filename | Should -Be (Join-Path $sourcePath 'GVFS\GVFS.Common\Policy.cs')
            }
        }
        finally {
            Pop-Location
        }
    }

    It 'Accepts lifecycle empty scenarios, Int64 shards, and a skipped NUnit root with passed cases' {
        foreach ($file in Get-ChildItem -LiteralPath $inputPath -Filter 'metadata.json' -Recurse -File) {
            $metadata = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -AsHashtable
            $metadata.Shard | Should -BeOfType ([long])
            if ($metadata.Suite -ne 'Upgrade') {
                $metadata.Scenario = ''
                $metadata | ConvertTo-Json | Set-Content -LiteralPath $file.FullName
            }
        }
        $testPath = Join-Path $inputPath 'functional-arm64-shard-0\tests.xml'
        (Get-Content -LiteralPath $testPath -Raw).Replace('<test-run result="Passed"', '<test-run result="Skipped" label="Ignored"') |
            Set-Content -LiteralPath $testPath
        Invoke-Merge
        $summary = Get-Content -LiteralPath (Join-Path $outputPath 'summary.json') -Raw | ConvertFrom-Json
        $summary.Complete | Should -BeTrue
        $summary.Workers | Should -Be 32
        ($summary.WorkerReports | Where-Object WorkerId -eq 'functional-arm64-shard-0').Shard | Should -Be 0
        ($summary.WorkerReports | Where-Object WorkerId -eq 'functional-arm64-shard-0').Tests.Passed | Should -Be 1
        ($summary.WorkerReports | Where-Object WorkerId -eq 'functional-arm64-shard-0').Tests.Skipped | Should -Be 1
        ($summary.WorkerReports | Where-Object WorkerId -eq 'unit-x64').Shard | Should -Be -1
        ($summary.WorkerReports | Where-Object WorkerId -eq 'upgrade-x64-clean-upgrade').Scenario | Should -Be 'clean-upgrade'
    }

    It 'Rejects a missing worker and invalidates an old complete report' {
        Invoke-Merge
        Remove-Item -LiteralPath (Join-Path $inputPath 'functional-arm64-shard-11') -Recurse -Force
        { Invoke-Merge } | Should -Throw '*Missing workers (31/32)*functional-arm64-shard-11*'
        $summary = Get-Content -LiteralPath (Join-Path $outputPath 'summary.json') -Raw | ConvertFrom-Json
        $summary.Complete | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $outputPath 'Summary.md') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $outputPath 'ChangedLines.html') | Should -BeFalse
    }

    It 'Rejects duplicate worker identities instead of treating them as another shard' {
        Copy-Item -LiteralPath (Join-Path $inputPath 'unit-x64') -Destination (Join-Path $inputPath 'duplicate') -Recurse
        { Invoke-Merge } | Should -Throw '*Duplicate worker unit-x64*'
    }

    It 'Rejects mixed source commits and foreign repository evidence' {
        Set-WorkerMetadata unit-x64 SourceCommit ('b' * 40)
        { Invoke-Merge } | Should -Throw '*SourceCommit mismatch*'
        Set-WorkerMetadata unit-x64 SourceCommit $commit
        Set-WorkerMetadata unit-x64 Repository 'someone/other'
        { Invoke-Merge } | Should -Throw '*Foreign repository*'
    }

    It 'Rejects incomplete collectors, failed or skipped outcomes, malformed metadata, and invalid identities' {
        foreach ($mutation in @(
            @{ Field = 'Complete'; Value = $false; Original = $true; Error = '*Incomplete worker*' },
            @{ Field = 'Complete'; Value = 'true'; Original = $true; Error = '*Incomplete worker*' },
            @{ Field = 'TestOutcome'; Value = 'failure'; Original = 'success'; Error = '*TestOutcome*not success*' },
            @{ Field = 'TestOutcome'; Value = 'skipped'; Original = 'success'; Error = '*TestOutcome*not success*' },
            @{ Field = 'SchemaVersion'; Value = 2; Original = 1; Error = '*SchemaVersion*' },
            @{ Field = 'SourceRoot'; Value = 'src'; Original = $runnerRoot; Error = '*Expected an absolute Windows source path*' },
            @{ Field = 'Shard'; Value = 12; Original = 0; Error = '*Invalid Functional shard*' },
            @{ Field = 'Shard'; Value = '0'; Original = 0; Error = '*Invalid worker Shard*' },
            @{ Field = 'Scenario'; Value = 0; Original = $null; Error = '*Invalid worker Scenario*' },
            @{ Field = 'Scenario'; Value = ' '; Original = $null; Error = '*Invalid Functional shard or scenario*' },
            @{ Field = 'Architecture'; Value = 'other'; Original = 'x64'; Error = '*Invalid worker configuration*' }
        )) {
            Set-WorkerMetadata functional-x64-shard-0 $mutation.Field $mutation.Value
            { Invoke-Merge } | Should -Throw $mutation.Error
            Set-WorkerMetadata functional-x64-shard-0 $mutation.Field $mutation.Original
        }
        Set-WorkerMetadata upgrade-x64-clean-upgrade Scenario 'not-a-scenario'
        { Invoke-Merge } | Should -Throw '*Invalid Upgrade*'
    }

    It 'Remaps another runner with case and slash differences, excludes generated/external documents, and preserves raw XML' {
        $rawPath = Join-Path $inputPath 'functional-x64-shard-0\coverage.cobertura.xml'
        Edit-WorkerCoverage functional-x64-shard-0 {
            param($document)
            $classes = $document.SelectSingleNode('//package/classes')
            $template = $classes.SelectSingleNode('./class')
            $template.SetAttribute('filename', 'd:/A/vfsforgit/VFSFORGIT/SRC/gvfs/gvfs.common/policy.cs')
            foreach ($filename in @("$runnerRoot\obj\Generated.g.cs", 'C:\framework\External.cs')) {
                $class = $template.CloneNode($true)
                $class.SetAttribute('filename', $filename)
                [void]$classes.AppendChild($class)
            }
        }
        $raw = Get-Content -LiteralPath $rawPath -Raw
        $raw.Replace('<coverage ', '<!DOCTYPE coverage SYSTEM "https://example.invalid/coverage.dtd"><coverage ') |
            Set-Content -LiteralPath $rawPath
        $rawBefore = Get-Content -LiteralPath $rawPath -Raw
        Invoke-Merge
        (Get-Content -LiteralPath $rawPath -Raw) | Should -BeExactly $rawBefore
        [xml]$normalized = Get-Content -LiteralPath (Join-Path $outputPath 'normalized\functional-x64\functional-x64-shard-0.cobertura.xml') -Raw
        $normalized.SelectNodes('//class').Count | Should -Be 7
        $normalized.SelectSingleNode('//class').filename | Should -Be (Join-Path $sourcePath 'GVFS\GVFS.Common\Policy.cs')
        $normalized.coverage.'lines-valid' | Should -Be '14'
        $normalized.coverage.'lines-covered' | Should -Be '7'
        $summary = Get-Content -LiteralPath (Join-Path $outputPath 'summary.json') -Raw | ConvertFrom-Json
        $summary.ExcludedDocumentCount | Should -Be 2
        $summary.ExcludedDocuments.Reason | Should -Contain 'generated-document'
        $summary.ExcludedDocuments.Reason | Should -Contain 'external-document'
    }

    It 'Rejects missing source, escaped repository paths, absent reports, and empty mapped execution' {
        $path = Join-Path $inputPath 'unit-x64\coverage.cobertura.xml'
        $original = Get-Content -LiteralPath $path -Raw
        Edit-WorkerCoverage unit-x64 {
            param($document)
            $document.SelectSingleNode('//class').SetAttribute('filename', 'GVFS\GVFS.Common\Policy.cs')
        }
        { Invoke-Merge } | Should -Throw '*Expected an absolute Windows source path*'
        $original | Set-Content -LiteralPath $path
        Edit-WorkerCoverage unit-x64 {
            param($document)
            $document.SelectSingleNode('//class').SetAttribute('filename', "$runnerRoot\GVFS\GVFS.Common\Missing.cs")
        }
        { Invoke-Merge } | Should -Throw '*Missing repository source*'
        $original | Set-Content -LiteralPath $path
        Edit-WorkerCoverage unit-x64 {
            param($document)
            $document.SelectSingleNode('//class').SetAttribute('filename', "$runnerRoot\..\elsewhere\GVFS\GVFS.Common\Policy.cs")
        }
        { Invoke-Merge } | Should -Throw '*Unmatched repository source*'
        $original | Set-Content -LiteralPath $path
        Edit-WorkerCoverage unit-x64 {
            param($document)
            foreach ($class in $document.SelectNodes('//class')) { $class.SetAttribute('filename', "$runnerRoot\obj\Generated.g.cs") }
        }
        { Invoke-Merge } | Should -Throw '*No executed repository source coverage*'
        $original | Set-Content -LiteralPath $path
        Edit-WorkerCoverage unit-x64 {
            param($document)
            foreach ($line in $document.SelectNodes('//class/lines/line')) { $line.SetAttribute('hits', '0') }
        }
        { Invoke-Merge } | Should -Throw '*No executed repository source coverage*'
        Remove-Item -LiteralPath $path
        { Invoke-Merge } | Should -Throw '*Missing coverage.cobertura.xml*'
        $original | Set-Content -LiteralPath $path
        $orphan = Join-Path $inputPath 'orphan'
        [void][System.IO.Directory]::CreateDirectory($orphan)
        Copy-Item -LiteralPath $path -Destination (Join-Path $orphan 'coverage.cobertura.xml')
        { Invoke-Merge } | Should -Throw '*Missing metadata.json beside coverage artifact*'
    }

    It 'Rejects product test contamination and requires Functional module hits rather than aggregate or method rates' {
        Edit-WorkerCoverage upgrade-x64-clean-upgrade {
            param($document)
            $document.SelectSingleNode('//package').SetAttribute('name', 'GVFS.UnitTests')
        }
        { Invoke-Merge } | Should -Throw '*Test assemblies contaminate product*'
        Edit-WorkerCoverage upgrade-x64-clean-upgrade {
            param($document)
            $document.SelectSingleNode('//package').SetAttribute('name', 'GVFS.Service')
        }
        foreach ($architecture in @('x64', 'arm64')) {
            foreach ($shard in 0..11) {
                Edit-WorkerCoverage "functional-$architecture-shard-$shard" {
                    param($document)
                    foreach ($line in $document.SelectNodes("//package[@name='GVFS.Hooks']/classes/class/lines/line")) {
                        $line.SetAttribute('hits', '0')
                    }
                }
            }
        }
        { Invoke-Merge } | Should -Throw '*No executed Functional source coverage*GVFS.Hooks*'
    }

    It 'Rejects fixture errors and all-skipped or empty NUnit runs even with success metadata' {
        $path = Join-Path $inputPath 'functional-x64-shard-0\tests.xml'
        $original = Get-Content -LiteralPath $path -Raw
        $original.Replace('<test-suite result="Passed">', '<test-suite result="Failed"><failure><message>Setup failed</message></failure>') |
            Set-Content -LiteralPath $path
        { Invoke-Merge } | Should -Throw '*Failed NUnit tests or fixture errors*'
        '<test-run result="Passed" total="1" passed="0" failed="0" skipped="1"><test-case result="Skipped"/></test-run>' |
            Set-Content -LiteralPath $path
        { Invoke-Merge } | Should -Throw '*No executed NUnit tests*'
        '<test-run result="Passed" total="0" passed="0" failed="0" skipped="0"/>' | Set-Content -LiteralPath $path
        { Invoke-Merge } | Should -Throw '*Invalid count*'
        $original.Replace('passed="1"', 'passed="2"') | Set-Content -LiteralPath $path
        { Invoke-Merge } | Should -Throw '*Inconsistent NUnit test counts*'
        Remove-Item -LiteralPath $path
        { Invoke-Merge } | Should -Throw '*Missing tests.xml*'
    }

    It 'Combines unique changed source lines with distinct evidence and a union denominator' {
        $policyPath = Join-Path $sourcePath 'GVFS\GVFS.Common\Policy.cs'
        1..6 | ForEach-Object { "old$_" } | Set-Content -LiteralPath $policyPath
        Invoke-TestGit @('init', '--quiet') | Out-Null
        Invoke-TestGit @('config', 'user.email', 'fixture@example.invalid') | Out-Null
        Invoke-TestGit @('config', 'user.name', 'Coverage fixture') | Out-Null
        Invoke-TestGit @('config', 'core.autocrlf', 'false') | Out-Null
        Invoke-TestGit @('config', 'commit.gpgsign', 'false') | Out-Null
        Invoke-TestGit @('add', '.') | Out-Null
        Invoke-TestGit @('-c', 'core.hooksPath=NUL', 'commit', '--quiet', '-m', 'Base fixture') | Out-Null
        $base = [string](Invoke-TestGit @('rev-parse', 'HEAD'))
        1..6 | ForEach-Object { "new$_" } | Set-Content -LiteralPath $policyPath
        Invoke-TestGit @('add', '.') | Out-Null
        Invoke-TestGit @('-c', 'core.hooksPath=NUL', 'commit', '--quiet', '-m', 'Tested fixture') | Out-Null
        $commit = [string](Invoke-TestGit @('rev-parse', 'HEAD'))
        foreach ($metadataFile in Get-ChildItem -LiteralPath $inputPath -Filter 'metadata.json' -Recurse) {
            $id = $metadataFile.Directory.Name
            Set-WorkerMetadata $id SourceCommit $commit
            $isUnit = $id.StartsWith('unit-')
            Edit-WorkerCoverage $id {
                param($document)
                foreach ($class in $document.SelectNodes('//class')) {
                    $hits = if ($isUnit) { @{ 1 = 1; 2 = 0; 3 = 1; 4 = 0; 6 = 1 } }
                        else { @{ 1 = 1; 2 = 1; 3 = 0; 4 = 0 } }
                    $unitDuplicate = $isUnit -and $class.name -eq 'GVFS.Common.Policy'
                    $class.SelectSingleNode('./lines').InnerXml = ($hits.Keys | Sort-Object | ForEach-Object {
                        $hit = if ($unitDuplicate) { 0 } else { $hits[$_] }
                        "<line number='$_' hits='$hit'/>"
                    }) -join ''
                }
            }
        }
        Invoke-Merge -Base $base
        $changed = Get-Content -LiteralPath (Join-Path $outputPath 'changed-lines.json') -Raw | ConvertFrom-Json
        $combined = $changed.Files[0].COMBINED
        $combined.Measured | Should -Be 5
        $combined.Hit | Should -Be 4
        $combined.Percentage | Should -Be 80
        $combined.Both | Should -Be 1
        $combined.ProductOnly | Should -Be 1
        $combined.UnitOnly | Should -Be 2
        $combined.Uncovered | Should -Be 1
        $combined.NoEntry | Should -Be 1
        @($combined.Lines.State) | Should -Be @('product-and-unit', 'product-only', 'unit-only', 'uncovered', 'no-entry/unmeasured-or-non-executable', 'unit-only')
        $combined.Lines[0].Provenance.Suite | Should -Contain 'Unit'
        $combined.Lines[0].Provenance.Suite | Should -Contain 'Functional'
        $combined.Lines[2].Provenance.Module | Should -Contain 'GVFS.UnitTests'
        $changed.Files[0].PRODUCT.Hit | Should -Be 2
        $changed.Files[0].UNIT.Hit | Should -Be 3
        $changed.Measurements.COMBINED.Hit | Should -Be 4
        $changed.Measurements.COMBINED.NoEntry | Should -Be 1
        $summary = Get-Content -LiteralPath (Join-Path $outputPath 'summary.json') -Raw | ConvertFrom-Json
        $summary.Measurements.COMBINED.Measured | Should -Be 5
        $summary.Measurements.COMBINED.Hit | Should -Be 4
        $markdown = Get-Content -LiteralPath (Join-Path $outputPath 'ChangedLines.md') -Raw
        $markdown | Should -Match '\*\*Combined: 80% \(4/5 measured changed lines\)'
        $markdown | Should -Match 'Separate product and unit'
        $html = Get-Content -LiteralPath (Join-Path $outputPath 'ChangedLines.html') -Raw
        $html | Should -Match "https://github.com/microsoft/VFSForGit/blob/$commit/GVFS/GVFS.Common/Policy.cs#L6"
        $html | Should -Match 'No-entry \(coverage unknown\)'
    }

    It 'Uses the tested git commits, separates unit-only hits, and keeps native, test, non-code, and no-entry changes visible with N/A' {
        [void][System.IO.Directory]::CreateDirectory((Join-Path $sourcePath 'GVFS\GVFS.UnitTests'))
        [void][System.IO.Directory]::CreateDirectory((Join-Path $sourcePath 'GVFS\GVFS.NativeTests'))
        foreach ($relative in @('GVFS\GVFS.Common\No Entry.cs', 'GVFS\GVFS.Common\No é Entry.cs', 'GVFS\GVFS.Common\Native.cpp', 'GVFS\GVFS.UnitTests\Test.cs', 'GVFS\GVFS.NativeTests\Test.cpp', 'Readme.md')) {
            'old' | Set-Content -LiteralPath (Join-Path $sourcePath $relative)
        }
        [System.IO.File]::WriteAllBytes((Join-Path $sourcePath 'binary.dat'), [byte[]]@(0, 1, 2, 3))
        Invoke-TestGit @('init', '--quiet') | Out-Null
        Invoke-TestGit @('config', 'user.email', 'fixture@example.invalid') | Out-Null
        Invoke-TestGit @('config', 'user.name', 'Coverage fixture') | Out-Null
        Invoke-TestGit @('config', 'core.autocrlf', 'false') | Out-Null
        Invoke-TestGit @('config', 'commit.gpgsign', 'false') | Out-Null
        Invoke-TestGit @('add', '.') | Out-Null
        Invoke-TestGit @('-c', 'core.hooksPath=NUL', 'commit', '--quiet', '-m', 'Base fixture') | Out-Null
        $base = [string](Invoke-TestGit @('rev-parse', 'HEAD'))
        "first`nmodified`nthird" | Set-Content -LiteralPath (Join-Path $sourcePath 'GVFS\GVFS.Common\Policy.cs')
        foreach ($relative in @('GVFS\GVFS.Common\No Entry.cs', 'GVFS\GVFS.Common\No é Entry.cs', 'GVFS\GVFS.Common\Native.cpp', 'GVFS\GVFS.UnitTests\Test.cs', 'GVFS\GVFS.NativeTests\Test.cpp', 'Readme.md')) {
            'new' | Set-Content -LiteralPath (Join-Path $sourcePath $relative)
        }
        [System.IO.File]::WriteAllBytes((Join-Path $sourcePath 'binary.dat'), [byte[]]@(0, 1, 2, 4))
        Invoke-TestGit @('add', '.') | Out-Null
        Invoke-TestGit @('-c', 'core.hooksPath=NUL', 'commit', '--quiet', '-m', 'Tested fixture') | Out-Null
        $commit = [string](Invoke-TestGit @('rev-parse', 'HEAD'))
        foreach ($path in Get-ChildItem -LiteralPath $inputPath -Filter 'metadata.json' -Recurse) {
            Set-WorkerMetadata $path.Directory.Name SourceCommit $commit
        }
        # A later working-copy edit must not change the commit-based line selection.
        "first`nmodified`nlater-uncommitted" | Set-Content -LiteralPath (Join-Path $sourcePath 'GVFS\GVFS.Common\Policy.cs')
        Invoke-Merge -Base $base
        $changed = Get-Content -LiteralPath (Join-Path $outputPath 'changed-lines.json') -Raw | ConvertFrom-Json
        $changed.Available | Should -BeTrue
        $changed.SourceCommit | Should -Be $commit
        $policy = $changed.Files | Where-Object Path -eq 'GVFS/GVFS.Common/Policy.cs'
        @($policy.AddedModifiedLines) | Should -Be @(2)
        $policy.PRODUCT.Measured | Should -Be 1
        $policy.PRODUCT.Hit | Should -Be 0
        $policy.PRODUCT.Uncovered | Should -Be 1
        $policy.PRODUCT.Lines[0].State | Should -Be 'uncovered'
        $policy.UNIT.Hit | Should -Be 1
        $policy.COMBINED.Hit | Should -Be 1
        $policy.COMBINED.Lines[0].State | Should -Be 'unit-only'
        $policy.COMBINED.UnitOnly | Should -Be 1
        $policy.UNIT.Lines[0].Provenance.Module | Should -Contain 'GVFS.UnitTests'
        $policy.PRODUCT.Lines[0].Provenance.Suite | Should -Not -Contain 'Unit'
        $absent = $changed.Files | Where-Object Path -eq 'GVFS/GVFS.Common/No Entry.cs'
        $absent.PRODUCT.Percentage | Should -BeNullOrEmpty
        $absent.PRODUCT.NoEntry | Should -Be 1
        $absent.PRODUCT.Lines[0].State | Should -Be 'no-entry/unmeasured-or-non-executable'
        $absent.COMBINED.Percentage | Should -BeNullOrEmpty
        $absent.COMBINED.NoEntry | Should -Be 1
        $absent.COMBINED.Lines[0].State | Should -Be 'no-entry/unmeasured-or-non-executable'
        ($changed.Files | Where-Object Path -eq 'GVFS/GVFS.Common/No é Entry.cs').PRODUCT.NoEntry | Should -Be 1
        ($changed.Files | Where-Object Path -like '*Native.cpp').Classification | Should -Be 'unsupported-native'
        ($changed.Files | Where-Object Path -like '*Native.cpp').COMBINED.Lines[0].State | Should -Be 'unsupported-native'
        ($changed.Files | Where-Object Path -like '*UnitTests/Test.cs').Classification | Should -Be 'intentionally-excluded-test-code'
        ($changed.Files | Where-Object Path -like '*NativeTests/Test.cpp').Classification | Should -Be 'intentionally-excluded-test-code'
        ($changed.Files | Where-Object Path -eq 'Readme.md').Classification | Should -Be 'not-applicable'
        ($changed.Files | Where-Object Path -eq 'binary.dat').Classification | Should -Be 'not-applicable'
        $changed.Files.Count | Should -Be 8
        (Get-Content -LiteralPath (Join-Path $outputPath 'ChangedLines.md') -Raw) | Should -Match 'N/A'
        (Get-Content -LiteralPath (Join-Path $outputPath 'ChangedLines.md') -Raw) | Should -Match 'No%20%C3%A9%20Entry.cs'
        $changed.Measurements.COMBINED.NoEntry | Should -Be 2
        { Invoke-Merge -Base 'not-a-sha' } | Should -Throw '*BaseCommit must be a 40-hex*'
        (Get-Content -LiteralPath (Join-Path $outputPath 'summary.json') -Raw | ConvertFrom-Json).Complete | Should -BeFalse
    }
}
