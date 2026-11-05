param(
    [Parameter(Mandatory)]
    [string]$Path,
    [Parameter(Mandatory)]
    [string]$Repository,
    [Parameter(Mandatory)]
    [string]$SessionId,
    [Parameter(Mandatory)]
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture
)

$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$commit = git -C $sourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the compiled source commit.' }

[ordered]@{
    SchemaVersion = 1
    Repository = $Repository
    SourceCommit = $commit
    SourceRoot = $sourceRoot
    SessionId = $SessionId
    Architecture = $Architecture
    Configuration = 'Release'
} | ConvertTo-Json | Set-Content -LiteralPath $Path -Encoding utf8
