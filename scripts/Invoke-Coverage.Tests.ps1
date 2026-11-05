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
