BeforeAll {
    $workflowPath = Join-Path (Split-Path -Parent $PSScriptRoot) '.github\workflows\build.yaml'
    $workflow = Get-Content -LiteralPath $workflowPath -Raw

    function Get-WorkflowScript {
        param([string]$StepName, [string]$Key)
        $step = [regex]::Match($workflow, "(?m)^    - name: $([regex]::Escape($StepName))\r?`n(?:(?!    - name:)[^\r`n]*\r?`n)*?        $Key`: \|\r?`n(?<body>(?:          [^\r`n]*(?:\r?`n|$)|\r?`n)+)")
        if (-not $step.Success) { throw "Cannot find workflow script for $StepName." }
        return $step.Groups['body'].Value -replace '(?m)^ {10}', ''
    }

    $reuseScript = Join-Path $TestDrive 'reuse.js'
    Get-WorkflowScript 'Look for prior successful runs' 'script' | Set-Content -LiteralPath $reuseScript
    $reuseHarness = Join-Path $TestDrive 'reuse.mjs'
    @'
import { readFileSync } from 'node:fs';
const fixture = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const script = readFileSync(process.argv[3], 'utf8');
const url = id => `https://github.com/microsoft/VFSForGit/actions/runs/${id}`;
const run = (id, event) => ({
  id, event, workflow_id: 1, head_sha: 'head', head_commit: { tree_id: 'tree' },
  html_url: url(id), status: 'completed', conclusion: 'success'
});
const runs = Object.fromEntries(fixture.runs.map(({id, event}) => [id, run(id, event)]));
const current = run(1, fixture.coverage ? 'pull_request' : 'push');
const notices = [];
const inspected = [];
process.env.AotMsCodeCoverageInstrumentation = String(fixture.coverage);
console.log = () => {};
const github = { rest: {
  actions: {
    getWorkflowRun: async ({run_id}) => ({data: run_id === 1 ? current : runs[run_id]}),
    listWorkflowRuns: async () => ({data: {workflow_runs: fixture.candidates.map(id => runs[id])}}),
    listJobsForWorkflowRun: async ({run_id}) => {
      inspected.push(run_id);
      if (fixture.unreadable.includes(run_id)) throw new Error('Cannot inspect run');
      return {data: {jobs: [{name: 'Validation', id: run_id}]}};
    }
  },
  checks: {listAnnotations: async ({check_run_id}) => ({
    data: fixture.links[check_run_id] ? [{
      message: `Skipping: There already is a successful run: ${url(fixture.links[check_run_id])}`
    }] : []
  })}
}};
const core = {notice: value => notices.push(value), error: error => {throw error}, warning: () => {}};
const AsyncFunction = Object.getPrototypeOf(async function(){}).constructor;
const result = await new AsyncFunction('github', 'context', 'core', script)(
  github, {runId: 1, repo: {owner: 'microsoft', repo: 'VFSForGit'}}, core);
process.stdout.write(JSON.stringify({result, notices, inspected}));
'@ | Set-Content -LiteralPath $reuseHarness

    function Invoke-ReuseCheck {
        param([bool]$Coverage, [object[]]$Runs, [int[]]$Candidates, [hashtable]$Links = @{}, [int[]]$Unreadable = @())
        $fixturePath = Join-Path $TestDrive 'reuse.json'
        @{
            coverage = $Coverage; runs = $Runs; candidates = $Candidates
            links = $Links; unreadable = $Unreadable
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fixturePath
        $result = node $reuseHarness $fixturePath $reuseScript
        if ($LASTEXITCODE -ne 0) { throw 'Workflow reuse script failed.' }
        return $result | ConvertFrom-Json
    }
}

Describe 'Coverage-mode workflow execution' {
    It 'Reuses only matching <Event> runs' -ForEach @(
        @{ Coverage = $true; Event = 'pull_request'; OtherEvent = 'push' },
        @{ Coverage = $false; Event = 'push'; OtherEvent = 'pull_request' }
    ) {
        $result = Invoke-ReuseCheck -Coverage $Coverage -Runs @(@{ id = 2; event = $OtherEvent }, @{ id = 3; event = $Event }) -Candidates @(2, 3)
        $result.result | Should -Be 'https://github.com/microsoft/VFSForGit/actions/runs/3'
        @($result.inspected) | Should -Be @(3)
        $result.notices.Count | Should -Be 1
    }

    It 'Rejects a <Event> skip chain that points to the opposite coverage mode' -ForEach @(
        @{ Coverage = $true; Event = 'pull_request'; OtherEvent = 'push' },
        @{ Coverage = $false; Event = 'push'; OtherEvent = 'pull_request' }
    ) {
        $result = Invoke-ReuseCheck -Coverage $Coverage -Runs @(@{ id = 2; event = $Event }, @{ id = 3; event = $Event }, @{ id = 4; event = $OtherEvent }) `
            -Candidates @(2, 3) -Links @{ '2' = 4 }
        $result.result | Should -Be 'https://github.com/microsoft/VFSForGit/actions/runs/3'
        @($result.inspected) | Should -Be @(2, 3)
    }

    It 'Follows a same-mode chain to its actual artifact producer' {
        $result = Invoke-ReuseCheck -Coverage $true -Runs @(@{ id = 2; event = 'pull_request' }, @{ id = 3; event = 'pull_request' }) `
            -Candidates @(2) -Links @{ '2' = 3 }
        $result.result | Should -Be 'https://github.com/microsoft/VFSForGit/actions/runs/3'
        @($result.inspected) | Should -Be @(2, 3)
    }

    It 'Does not claim reusable evidence when a skip chain cannot be inspected' {
        $result = Invoke-ReuseCheck -Coverage $false -Runs @(@{ id = 2; event = 'push' }) -Candidates @(2) -Unreadable @(2)
        $result.result | Should -BeNullOrEmpty
        $result.notices.Count | Should -Be 0
    }

    It 'Runs unit tests through <ExpectedCommand> and preserves their failure exit code' -ForEach @(
        @{ Mode = 'true'; ExpectedCommand = 'collector' },
        @{ Mode = 'false'; ExpectedCommand = 'cmd' }
    ) {
        $unitStep = [regex]::Match($workflow, '(?m)^    - name: Run unit tests\r?\n(?:      [^\r\n]*\r?\n)*      run: \|\r?\n(?<body>(?:        [^\r\n]*(?:\r?\n|$))+)')
        if (-not $unitStep.Success) { throw 'Cannot find the unit-test workflow script.' }
        $unitScript = ($unitStep.Groups['body'].Value -replace '(?m)^ {8}', '').
            Replace('${{ matrix.configuration }}', 'Release').Replace('${{ matrix.architecture }}', 'x64')
        $runner = Join-Path $TestDrive 'unit-step.ps1'
        $capture = Join-Path $TestDrive 'unit-command.json'
        @'
param([string]$Mode, [string]$Capture)
$env:AotMsCodeCoverageInstrumentation = $Mode
$env:MsCodeCoverageSessionId = 'test-session'
function cmd.exe {
    @{Command = 'cmd'; Arguments = @($args)} | ConvertTo-Json | Set-Content -LiteralPath $Capture
    $global:LASTEXITCODE = 7
}
function Microsoft.CodeCoverage.Console.exe {
    @{Command = 'collector'; Arguments = @($args)} | ConvertTo-Json | Set-Content -LiteralPath $Capture
    $global:LASTEXITCODE = 7
}
'@ + "`n$unitScript" | Set-Content -LiteralPath $runner
        pwsh -NoLogo -NoProfile -File $runner -Mode $Mode -Capture $capture
        $exitCode = $LASTEXITCODE
        $exitCode | Should -Be 7
        $command = Get-Content -LiteralPath $capture -Raw | ConvertFrom-Json
        $command.Command | Should -Be $ExpectedCommand
        $expected = @('/d', '/c', 'src\scripts\RunUnitTests.bat', 'Release', 'x64')
        if ($Mode -eq 'true') { $expected = @('connect', 'test-session', 'cmd.exe') + $expected }
        @($command.Arguments) | Should -Be $expected
    }
}
