param(
    [Parameter(Mandatory = $true)]
    [string] $CompilerPath,
    [string] $ArtifactDirectory = (Join-Path $PSScriptRoot '..\..\out\InstallerTelemetryTests')
)

$ErrorActionPreference = 'Stop'
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$harness = Join-Path $PSScriptRoot '..\GVFS\GVFS.Installers\Tests\InstallationTelemetry.iss'
$compiler = Start-Process -FilePath $CompilerPath -ArgumentList @(
    "/DTelemetryTestSid=$($sid.Value)",
    "/O`"$ArtifactDirectory`"",
    "`"$harness`""
) -NoNewWindow -PassThru -Wait
if ($compiler.ExitCode -ne 0) { throw "Inno compiler failed: $($compiler.ExitCode)" }

$executable = Join-Path $ArtifactDirectory 'InstallationTelemetryTests.exe'
$testRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ("GVFS.InstallerTelemetry.Tests." + [Guid]::NewGuid().ToString('N'))

function New-ProtectedDirectory([string] $Path) {
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $security.SetOwner($sid)
    $rule = [Security.AccessControl.FileSystemAccessRule]::new(
        $sid, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')
    $security.AddAccessRule($rule)
    [IO.FileSystemAclExtensions]::CreateDirectory($security, $Path) | Out-Null
}

function Run-Harness([string] $Scenario, [string] $RecordDirectory) {
    $log = Join-Path $ArtifactDirectory "$Scenario.log"
    $process = Start-Process -FilePath $executable -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        "/SCENARIO=$Scenario", "/RECORDDIR=`"$RecordDirectory`"", "/LOG=`"$log`""
    ) -PassThru
    if (-not $process.WaitForExit(30000)) {
        Stop-Process -Id $process.Id
        throw "The $Scenario harness exceeded 30 seconds."
    }
    return $process.ExitCode
}

try {
    New-ProtectedDirectory $testRoot
    foreach ($scenario in @('success', 'staged', 'remount', 'fail', 'cancel', 'escape', 'nested')) {
        $recordsPath = Join-Path $testRoot ("$scenario Records " + [char]0x96EA)
        if ($scenario -eq 'nested') { $recordsPath = Join-Path $recordsPath 'parent\records' }
        $exitCode = Run-Harness $scenario $recordsPath
        $expectedExit = 0
        if ($scenario -eq 'remount') { $expectedExit = 17 }
        if ($scenario -in @('fail', 'cancel')) {
            if ($exitCode -eq 0) { throw "$scenario unexpectedly succeeded." }
        } elseif ($exitCode -ne $expectedExit) {
            throw "$scenario returned $exitCode instead of $expectedExit."
        }

        $records = @(Get-ChildItem -LiteralPath $recordsPath -Filter '*.json' | ForEach-Object {
            [IO.File]::ReadAllText($_.FullName) | ConvertFrom-Json
        })
        if ($records.Count -ne 2) { throw "$scenario produced $($records.Count) records instead of two." }
        $start = @($records | Where-Object EventType -eq 'InstallationStarted')
        $finish = @($records | Where-Object EventType -eq 'InstallationFinished')
        if ($start.Count -ne 1 -or $finish.Count -ne 1) { throw "$scenario has incorrect lifecycle records." }
        if ($start[0].AttemptId -ne $finish[0].AttemptId -or $start[0].EventId -eq $finish[0].EventId) {
            throw "$scenario has incorrect record identifiers."
        }
        foreach ($record in $records) {
            if ([Guid]::Parse($record.EventId) -eq [Guid]::Empty -or $record.InstallerVersion -ne '0.2.1.0') {
                throw "$scenario has invalid identity or package fields."
            }
        }
        $expectedOutcome = switch ($scenario) {
            'staged' { 'Staged' }
            'fail' { 'Failed' }
            'cancel' { 'Cancelled' }
            default { 'Completed' }
        }
        if ($finish[0].Outcome -ne $expectedOutcome) { throw "$scenario has incorrect outcome: $($finish[0].Outcome)" }
        if ($scenario -eq 'remount' -and ($finish[0].RemountOutcome -ne 'Failed' -or $finish[0].ScriptResult -ne 17)) {
            throw 'The remount result did not survive successful setup completion.'
        }
        if ($scenario -eq 'escape') {
            $escaped = [IO.File]::ReadAllText((Join-Path $recordsPath 'escape.txt')) | ConvertFrom-Json
            if ($escaped -cne ('quotes" slash\' + "`n" + [char]0x96EA)) {
                throw "JSON escaping changed the text: $([IO.File]::ReadAllText((Join-Path $recordsPath 'escape.txt')))"
            }
        }
        Write-Output "PASS: $scenario"
    }

    $capacity = Join-Path $testRoot 'capacity'
    New-ProtectedDirectory $capacity
    $olderPaths = @(1..256 | ForEach-Object {
        $path = Join-Path $capacity ([Guid]::NewGuid().ToString() + '.json')
        [IO.File]::WriteAllText($path, '{}')
        [IO.File]::SetLastWriteTimeUtc($path, [DateTime]::UtcNow.AddHours(-1).AddSeconds($_))
        $path
    })
    $expired = Join-Path $capacity ([Guid]::NewGuid().ToString() + '.tmp')
    [IO.File]::WriteAllText($expired, '{partial')
    [IO.File]::SetLastWriteTimeUtc($expired, [DateTime]::UtcNow.AddDays(-8))
    if ((Run-Harness 'capacity' $capacity) -ne 0) { throw 'Capacity handling changed the installation result.' }
    if ((Get-ChildItem -LiteralPath $capacity -Filter '*.json').Count -ne 256 -or [IO.File]::Exists($expired)) {
        throw 'The producer did not enforce capacity and retention.'
    }
    if ([IO.File]::Exists($olderPaths[0]) -or [IO.File]::Exists($olderPaths[1]) -or
        @($olderPaths[2..255] | Where-Object { -not [IO.File]::Exists($_) }).Count -ne 0) {
        throw 'Capacity pruning did not delete exactly the two oldest records.'
    }
    $newRecords = @(Get-ChildItem -LiteralPath $capacity -Filter '*.json' |
        Where-Object FullName -NotIn $olderPaths |
        ForEach-Object { [IO.File]::ReadAllText($_.FullName) | ConvertFrom-Json })
    if ($newRecords.Count -ne 2 -or
        @($newRecords | Where-Object EventType -eq 'InstallationStarted').Count -ne 1 -or
        @($newRecords | Where-Object { $_.EventType -eq 'InstallationFinished' -and $_.Outcome -eq 'Completed' }).Count -ne 1) {
        throw 'Capacity handling did not publish the new installation records.'
    }
    Write-Output 'PASS: capacity and retention'

    $blocked = Join-Path $testRoot 'blocked'
    [IO.File]::WriteAllText($blocked, 'A file blocks directory creation.')
    if ((Run-Harness 'blocked' $blocked) -ne 0) { throw 'A record failure changed the installation result.' }
    Write-Output 'PASS: record failure preserves setup success'

    $unsafe = Join-Path $testRoot 'unsafe'
    New-ProtectedDirectory $unsafe
    $info = [IO.DirectoryInfo]::new($unsafe)
    $acl = [IO.FileSystemAclExtensions]::GetAccessControl($info)
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        [Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'), 'Write', 'Allow'))
    [IO.FileSystemAclExtensions]::SetAccessControl($info, $acl)
    if ((Run-Harness 'unsafe' $unsafe) -ne 0 -or (Get-ChildItem -LiteralPath $unsafe).Count -ne 0) {
        throw 'The producer accepted an unsafe directory or changed the installation result.'
    }
    Write-Output 'PASS: unsafe directory rejection'
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
