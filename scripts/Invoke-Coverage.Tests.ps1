BeforeAll {
    $lifecycle = Join-Path $PSScriptRoot 'Invoke-Coverage.ps1'
    $sourceCommit = git -C (Split-Path -Parent $PSScriptRoot) rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify test source commit.' }

    function New-BuildManifest {
        param([hashtable]$Overrides = @{})
        $manifest = @{
            SchemaVersion = 1
            SourceCommit = $sourceCommit
            SourceRoot = 'C:\build\src'
            Repository = 'microsoft/VFSForGit'
            SessionId = 'test-session'
            Architecture = 'x64'
            Configuration = 'Release'
        }
        foreach ($key in $Overrides.Keys) { $manifest[$key] = $Overrides[$key] }
        $path = Join-Path $TestDrive 'build.json'
        $manifest | ConvertTo-Json | Set-Content $path
        return $path
    }

    function Invoke-TestCollector {
        $collectorState.Calls.Add([string]$args[0])
        $global:LASTEXITCODE = if ($args[0] -in @($collectorState.Failure)) { 1 } else { 0 }
        if ($args[0] -eq 'shutdown') {
            $collectorState.ShutdownArguments = @($args)
            $metadataName = if ([string]$args[1] -like '*-component') { 'component-metadata.json' } else { 'metadata.json' }
            $collectorState.MetadataAtShutdown = Get-Content -LiteralPath (Join-Path $collectorState.Directory $metadataName) -Raw | ConvertFrom-Json
            if ($global:LASTEXITCODE -ne 0) { 'Fake shutdown failure' } else { 'Fake shutdown complete' }
        }
        if ($args[0] -eq '--version') { return 'Fake coverage collector 1.0' }
        if ($args[0] -eq 'collect') {
            $collectorState.CollectSettings = $args[[array]::IndexOf($args, '--settings') + 1]
            $collectorState.CollectSession = $args[[array]::IndexOf($args, '--session-id') + 1]
            $outputPath = $args[[array]::IndexOf($args, '--output') + 1]
            @"
<coverage lines-covered="$($collectorState.CoveredLines)" lines-valid="2"><packages>
<package name="GVFS.Service"><classes>
<class name="GVFS.Service.GVFSService"><methods><method name="OnStart"><lines><line number="1" hits="$($collectorState.StartupHits)"/></lines></method></methods><lines><line number="1" hits="$($collectorState.StartupHits)"/></lines></class>
</classes></package></packages></coverage>
"@ | Set-Content -LiteralPath $outputPath
            'Fake collector diagnostics' | Set-Content -LiteralPath $args[[array]::IndexOf($args, '--log-file') + 1]
        }
    }

    function Get-ManifestBootstrap {
        param([string]$Workflow)
        $path = Join-Path (Split-Path -Parent $PSScriptRoot) ".github\workflows\$Workflow"
        $text = Get-Content -LiteralPath $path -Raw
        $step = [regex]::Match($text, '(?m)^    - name: Read installer coverage manifest\r?\n(?:[^\r\n]*\r?\n)*?      run: \|\r?\n(?<body>(?:        [^\r\n]*(?:\r?\n|$))+)')
        if (-not $step.Success) { throw "Cannot find manifest bootstrap in $Workflow." }
        return [scriptblock]::Create(($step.Groups['body'].Value -replace '(?m)^ {8}', ''))
    }
}

Describe 'Coverage lifecycle input validation' {
    BeforeEach {
        Mock Get-Command { [pscustomobject]@{ Source = 'unreachable-collector.exe' } } -ParameterFilter {
            $Name -eq 'Microsoft.CodeCoverage.Console.exe'
        }
    }

    It 'Rejects malformed build identity before a collector starts' {
        $path = New-BuildManifest @{ SourceCommit = 'not-a-commit' }
        { & $lifecycle -Action Start -Directory $TestDrive -BuildManifest $path -Suite Unit } |
            Should -Throw '*Invalid coverage build manifest*'
    }

    It 'Rejects a build from another source checkout' {
        $path = New-BuildManifest @{ SourceCommit = '0000000000000000000000000000000000000000' }
        { & $lifecycle -Action Start -Directory $TestDrive -BuildManifest $path -Suite Unit } |
            Should -Throw '*does not match the compiled manifest*'
    }

    It 'Rejects functional shard identifiers outside the existing matrix' {
        $path = New-BuildManifest
        { & $lifecycle -Action Start -Directory $TestDrive -BuildManifest $path -Suite Functional -Shard 12 } |
            Should -Throw '*Invalid functional shard*'
    }

    It 'Rejects upgrade collection without a scenario identity' {
        $path = New-BuildManifest
        { & $lifecycle -Action Start -Directory $TestDrive -BuildManifest $path -Suite Upgrade } |
            Should -Throw '*requires a scenario*'
    }

    It 'Requires a test outcome before completion can be recorded' {
        { & $lifecycle -Action Stop -Directory $TestDrive } |
            Should -Throw '*Stop requires the test outcome*'
    }
}

Describe 'Coverage lifecycle producer output' {
            BeforeEach {
                $collectorState = @{
                    Directory = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
                    Calls = [System.Collections.Generic.List[string]]::new()
                    Failure = ''
                    CoveredLines = 1
                    StartupHits = 1
                }
                Mock Get-Command { [pscustomobject]@{ Source = 'Invoke-TestCollector' } } -ParameterFilter {
                    $Name -eq 'Microsoft.CodeCoverage.Console.exe'
                }
                Push-Location $TestDrive
                '<test-run result="Passed" total="1" passed="1"><test-case result="Passed"/></test-run>' |
                    Set-Content -LiteralPath TestResult.xml
            }

            AfterEach {
                Pop-Location
            }

            It 'Completes <Suite> evidence from the real build-manifest writer' -ForEach @(
                @{ Suite = 'Unit'; Shard = -1; Scenario = ''; Outcome = 'failure'; HasTests = $true },
                @{ Suite = 'Functional'; Shard = 0; Scenario = ''; Outcome = 'success'; HasTests = $true },
                @{ Suite = 'Component'; Shard = 0; Scenario = ''; Outcome = 'success'; HasTests = $true },
                @{ Suite = 'Upgrade'; Shard = -1; Scenario = 'staging-upgrade'; Outcome = 'success'; HasTests = $false }
            ) {
                $manifestPath = Join-Path $TestDrive 'produced-build.json'
                & (Join-Path $PSScriptRoot 'New-CoverageManifest.ps1') -Path $manifestPath -Repository microsoft/VFSForGit `
                    -SessionId test-session -Architecture x64
                & $lifecycle -Action Start -Directory $collectorState.Directory -BuildManifest $manifestPath -Suite $Suite -Shard $Shard -Scenario $Scenario
                $prefix = if ($Suite -eq 'Component') { 'component-' } else { '' }
                $metadataPath = Join-Path $collectorState.Directory "${prefix}metadata.json"
                $started = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
                $started.Complete | Should -BeFalse
                $started.CompletedUtc | Should -BeNullOrEmpty
                $started.SourceCommit | Should -Be $sourceCommit
                $started.SourceRoot | Should -Be (Split-Path -Parent $PSScriptRoot)
                $started.Suite | Should -Be $Suite
                $started.Shard | Should -Be $Shard
                $started.Scenario | Should -BeExactly $Scenario
                $started.CollectorVersion | Should -Be 'Fake coverage collector 1.0'
                $started.SessionId | Should -Be $(if ($Suite -eq 'Component') { 'test-session-component' } else { 'test-session' })
                $collectorState.CollectSession | Should -Be $started.SessionId
                Split-Path -Leaf $collectorState.CollectSettings | Should -Be $(if ($Suite -in @('Unit', 'Component')) { 'TestProcessCoverage.config' } else { 'AotCoverage.config' })
                & $lifecycle -Action Stop -Directory $collectorState.Directory -Suite $Suite -TestOutcome $Outcome
                $stopped = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
                $stopped.Complete | Should -BeTrue
                $stopped.TestOutcome | Should -Be $Outcome
                ([datetime]$stopped.CompletedUtc) | Should -BeGreaterOrEqual ([datetime]$stopped.StartedUtc)
                Test-Path -LiteralPath (Join-Path $collectorState.Directory "${prefix}tests.xml") | Should -Be $HasTests
                if ($HasTests) {
                    Get-Content -LiteralPath (Join-Path $collectorState.Directory "${prefix}tests.xml") -Raw |
                        Should -BeExactly (Get-Content -LiteralPath TestResult.xml -Raw)
                }
                if ($Suite -in @('Functional', 'Upgrade')) {
                    $service = Get-Content -LiteralPath (Join-Path $collectorState.Directory 'service.json') -Raw | ConvertFrom-Json
                    $service.ScmServiceStartupCovered | Should -BeTrue
                } else {
                    Test-Path -LiteralPath (Join-Path $collectorState.Directory 'service.json') | Should -BeFalse
                }
                @($collectorState.Calls) | Should -Be @('--version', 'collect', 'snapshot', 'shutdown')
                $collectorState.ShutdownArguments[2] | Should -Be '--timeout'
                $collectorState.ShutdownArguments[3] | Should -Be 30000
                $collectorState.MetadataAtShutdown.TestOutcome | Should -Be $Outcome
                $collectorState.MetadataAtShutdown.Complete | Should -BeFalse
            }

            It 'Keeps product and component sessions, metadata, and results separate in one worker directory' {
                $manifest = New-BuildManifest
                & $lifecycle -Action Start -Directory $collectorState.Directory -BuildManifest $manifest -Suite Functional -Shard 0
                & $lifecycle -Action Start -Directory $collectorState.Directory -BuildManifest $manifest -Suite Component -Shard 0
                & $lifecycle -Action Stop -Directory $collectorState.Directory -Suite Component -TestOutcome success
                $product = Get-Content -LiteralPath (Join-Path $collectorState.Directory 'metadata.json') -Raw | ConvertFrom-Json
                $component = Get-Content -LiteralPath (Join-Path $collectorState.Directory 'component-metadata.json') -Raw | ConvertFrom-Json
                $product.SessionId | Should -Be 'test-session'
                $component.SessionId | Should -Be 'test-session-component'
                $product.Complete | Should -BeFalse
                $component.Complete | Should -BeTrue
                Test-Path -LiteralPath (Join-Path $collectorState.Directory 'component-tests.xml') | Should -BeTrue
                Test-Path -LiteralPath (Join-Path $collectorState.Directory 'tests.xml') | Should -BeFalse
                & $lifecycle -Action Stop -Directory $collectorState.Directory -TestOutcome success
                (Get-Content -LiteralPath (Join-Path $collectorState.Directory 'metadata.json') -Raw | ConvertFrom-Json).Complete | Should -BeTrue
                Test-Path -LiteralPath (Join-Path $collectorState.Directory 'component-collector.log') | Should -BeTrue
                Test-Path -LiteralPath (Join-Path $collectorState.Directory 'collector.log') | Should -BeTrue
            }

            It 'Shuts down after a failed readiness snapshot and retains incomplete diagnostics' {
                $collectorState.Failure = 'snapshot'
                $manifest = New-BuildManifest
                { & $lifecycle -Action Start -Directory $collectorState.Directory -BuildManifest $manifest -Suite Unit } |
                    Should -Throw '*readiness check failed*'
                @($collectorState.Calls) | Should -Be @('--version', 'collect', 'snapshot', 'shutdown')
                (Get-Content -LiteralPath (Join-Path $collectorState.Directory 'metadata.json') -Raw | ConvertFrom-Json).Complete | Should -BeFalse
                Test-Path -LiteralPath (Join-Path $collectorState.Directory 'collector.log') | Should -BeTrue
                $collectorState.ShutdownArguments[2] | Should -Be '--timeout'
                $collectorState.ShutdownArguments[3] | Should -Be 30000
            }

            It 'Preserves a <Outcome> test outcome when the collector is already gone (<HasResults>)' -ForEach @(
                @{ Outcome = 'failure'; HasResults = $true },
                @{ Outcome = 'cancelled'; HasResults = $false }
            ) {
                $manifest = New-BuildManifest
                & $lifecycle -Action Start -Directory $collectorState.Directory -BuildManifest $manifest -Suite Functional -Shard 0
                if (-not $HasResults) { Remove-Item -LiteralPath TestResult.xml }
                $collectorState.Failure = 'shutdown'
                { & $lifecycle -Action Stop -Directory $collectorState.Directory -TestOutcome $Outcome -ShutdownTimeoutSeconds 1 3>$null } |
                    Should -Throw "*test outcome remains '$Outcome'; coverage is incomplete*"
                $metadata = Get-Content -LiteralPath (Join-Path $collectorState.Directory 'metadata.json') -Raw | ConvertFrom-Json
                $metadata.TestOutcome | Should -Be $Outcome
                $metadata.Complete | Should -BeFalse
                $metadata.CompletedUtc | Should -BeNullOrEmpty
                $metadata.StopError | Should -Match 'Coverage collector shutdown failed.*timeout 1s'
                $collectorState.MetadataAtShutdown.TestOutcome | Should -Be $Outcome
                $collectorState.ShutdownArguments[3] | Should -Be 1000
                Test-Path -LiteralPath (Join-Path $collectorState.Directory 'tests.xml') | Should -Be $HasResults
                Test-Path -LiteralPath (Join-Path $collectorState.Directory 'shutdown.log') | Should -BeTrue
                Get-Content -LiteralPath (Join-Path $collectorState.Directory 'shutdown.log') -Raw | Should -Match 'Fake shutdown failure'
                Test-Path -LiteralPath (Join-Path $collectorState.Directory 'service.json') | Should -BeFalse
            }

            It 'Fails incomplete collection when successful tests lose the collector' {
                $manifest = New-BuildManifest
                & $lifecycle -Action Start -Directory $collectorState.Directory -BuildManifest $manifest -Suite Unit
                $collectorState.Failure = 'shutdown'
                { & $lifecycle -Action Stop -Directory $collectorState.Directory -TestOutcome success } |
                    Should -Throw '*Coverage collector shutdown failed*'
                $metadata = Get-Content -LiteralPath (Join-Path $collectorState.Directory 'metadata.json') -Raw | ConvertFrom-Json
                $metadata.TestOutcome | Should -Be 'success'
                $metadata.Complete | Should -BeFalse
                $metadata.StopError | Should -Match 'shutdown failed'
                Test-Path -LiteralPath (Join-Path $collectorState.Directory 'tests.xml') | Should -BeTrue
            }

            It 'Still stops the collector when successful tests omit NUnit results' {
                $manifest = New-BuildManifest
                & $lifecycle -Action Start -Directory $collectorState.Directory -BuildManifest $manifest -Suite Unit
                Remove-Item -LiteralPath TestResult.xml
                { & $lifecycle -Action Stop -Directory $collectorState.Directory -TestOutcome success } |
                    Should -Throw '*successful test run produced no TestResult.xml*'
                @($collectorState.Calls) | Should -Contain 'shutdown'
                (Get-Content -LiteralPath (Join-Path $collectorState.Directory 'metadata.json') -Raw | ConvertFrom-Json).Complete | Should -BeFalse
            }

            It 'Preserves the readiness error when bounded cleanup also fails' {
                $collectorState.Failure = @('snapshot', 'shutdown')
                $manifest = New-BuildManifest
                { & $lifecycle -Action Start -Directory $collectorState.Directory -BuildManifest $manifest -Suite Unit 3>$null } |
                    Should -Throw '*readiness check failed*'
                @($collectorState.Calls) | Should -Be @('--version', 'collect', 'snapshot', 'shutdown')
                $collectorState.ShutdownArguments[3] | Should -Be 30000
            }

            It 'Does not mark a zero-hit report complete' {
                $collectorState.CoveredLines = 0
                $manifest = New-BuildManifest
                & $lifecycle -Action Start -Directory $collectorState.Directory -BuildManifest $manifest -Suite Unit
                { & $lifecycle -Action Stop -Directory $collectorState.Directory -TestOutcome success } |
                    Should -Throw '*no executed lines*'
                (Get-Content -LiteralPath (Join-Path $collectorState.Directory 'metadata.json') -Raw | ConvertFrom-Json).Complete | Should -BeFalse
                Test-Path -LiteralPath (Join-Path $collectorState.Directory 'tests.xml') | Should -BeTrue
            }

            It 'Does not mark non-SCM functional evidence complete' {
                $collectorState.StartupHits = 0
                $manifest = New-BuildManifest
                & $lifecycle -Action Start -Directory $collectorState.Directory -BuildManifest $manifest -Suite Functional -Shard 0
                { & $lifecycle -Action Stop -Directory $collectorState.Directory -TestOutcome success } |
                    Should -Throw '*no executed GVFSService.OnStart*'
                (Get-Content -LiteralPath (Join-Path $collectorState.Directory 'metadata.json') -Raw | ConvertFrom-Json).Complete | Should -BeFalse
            }
        }

        Describe 'Installer coverage manifest bootstrap' {
            BeforeEach {
                $savedOutput = $env:GITHUB_OUTPUT
                $savedRepository = $env:EXPECTED_REPOSITORY
                $savedArchitecture = $env:EXPECTED_ARCH
                $env:GITHUB_OUTPUT = Join-Path $TestDrive 'outputs.txt'
                $env:EXPECTED_REPOSITORY = 'microsoft/VFSForGit'
                $env:EXPECTED_ARCH = 'x64'
                Set-Content -LiteralPath $env:GITHUB_OUTPUT -Value ''
                Push-Location $TestDrive
            }

            AfterEach {
                Pop-Location
                $env:GITHUB_OUTPUT = $savedOutput
                $env:EXPECTED_REPOSITORY = $savedRepository
                $env:EXPECTED_ARCH = $savedArchitecture
            }

            It 'Binds <Workflow> checkout to the requested artifact repository' -ForEach @(
                @{ Workflow = 'functional-tests.yaml'; InstallerDirectory = 'gvfs' },
                @{ Workflow = 'upgrade-tests.yaml'; InstallerDirectory = 'gvfs-new' }
            ) {
                $bootstrap = Get-ManifestBootstrap $Workflow
                [void][System.IO.Directory]::CreateDirectory((Join-Path $TestDrive $InstallerDirectory))
                $path = New-BuildManifest @{ Repository = 'other/private-repo' }
                Copy-Item -LiteralPath $path -Destination "$InstallerDirectory\coverage-build.json" -Force
                { & $bootstrap } | Should -Throw '*Invalid installer coverage manifest*'
                (Get-Content -LiteralPath $env:GITHUB_OUTPUT -Raw).Trim() | Should -BeNullOrEmpty
                $path = New-BuildManifest @{ Repository = 'MICROSOFT/vfsforgit' }
                Copy-Item -LiteralPath $path -Destination "$InstallerDirectory\coverage-build.json" -Force
                & $bootstrap
                Get-Content -LiteralPath $env:GITHUB_OUTPUT | Should -Contain 'enabled=true'
                Get-Content -LiteralPath $env:GITHUB_OUTPUT | Should -Contain 'repository=MICROSOFT/vfsforgit'
                Get-Content -LiteralPath $env:GITHUB_OUTPUT | Should -Contain "source_commit=$sourceCommit"
            }

            It 'Leaves collection disabled for a manifest-free <Workflow> drop' -ForEach @(
                @{ Workflow = 'functional-tests.yaml'; InstallerDirectory = 'gvfs' },
                @{ Workflow = 'upgrade-tests.yaml'; InstallerDirectory = 'gvfs-new' }
            ) {
                $manifestPath = Join-Path $TestDrive "$InstallerDirectory\coverage-build.json"
                if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath }
                & (Get-ManifestBootstrap $Workflow)
                @(Get-Content -LiteralPath $env:GITHUB_OUTPUT | Where-Object { $_ }) | Should -Be @('enabled=false')
            }
        }
