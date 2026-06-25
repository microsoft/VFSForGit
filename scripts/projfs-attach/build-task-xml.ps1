# build-task-xml.ps1
#
# Produces the final EnableProjFSOnAllDrives scheduled task XML by
# base64-encoding enable-projfs-on-all-drives.ps1 and substituting it
# (along with a content hash) into enable-projfs-on-all-drives-task.xml.template.
#
# Inputs and output are passed by parameter so this script is callable
# from layout.bat, MSBuild, or directly during development.
#
# The hash embedded in the task Description (via __TASK_HASH__) is
# SHA-256 over the un-encoded inputs (template + script body, in that
# order, separated by a NUL byte). Stable across re-runs with
# unchanged inputs; changes the moment either input's content
# changes. This is what the installer's drift detection compares
# against the registered task's Description marker to decide whether
# re-registration is needed.
#
# Output XML is UTF-16 LE with BOM (required by Task Scheduler's
# /XML import).

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ScriptPath,

    [Parameter(Mandatory = $true)]
    [string]$TemplatePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ScriptPath)) { throw "Script not found: $ScriptPath" }
if (-not (Test-Path $TemplatePath)) { throw "Template not found: $TemplatePath" }

# Read raw bytes so the hash and the base64 are computed over exactly
# what's on disk, regardless of line-ending or BOM conventions.
$scriptBytes = [System.IO.File]::ReadAllBytes($ScriptPath)

# Read the template as text (UTF-8 or UTF-16 with BOM both work for
# Get-Content; the template is checked in as UTF-16 to match the XML
# encoding declaration but we re-emit as UTF-16 with BOM either way).
$templateText = [System.IO.File]::ReadAllText($TemplatePath)
$templateBytes = [System.Text.Encoding]::UTF8.GetBytes($templateText)

# PowerShell -EncodedCommand expects UTF-16 LE bytes, base64 encoded.
$scriptUtf16 = [System.Text.Encoding]::Unicode.GetString($scriptBytes)
# If the source script was UTF-8 (typical for files checked into git),
# the line above produces garbage. Detect by checking for a UTF-8 BOM
# or by attempting a UTF-8 decode and re-encoding to UTF-16.
$scriptText =
    if ($scriptBytes.Length -ge 3 -and $scriptBytes[0] -eq 0xEF -and $scriptBytes[1] -eq 0xBB -and $scriptBytes[2] -eq 0xBF) {
        [System.Text.Encoding]::UTF8.GetString($scriptBytes, 3, $scriptBytes.Length - 3)
    }
    elseif ($scriptBytes.Length -ge 2 -and $scriptBytes[0] -eq 0xFF -and $scriptBytes[1] -eq 0xFE) {
        [System.Text.Encoding]::Unicode.GetString($scriptBytes, 2, $scriptBytes.Length - 2)
    }
    else {
        # Assume UTF-8 without BOM (git's default for text)
        [System.Text.Encoding]::UTF8.GetString($scriptBytes)
    }

$scriptUtf16Bytes = [System.Text.Encoding]::Unicode.GetBytes($scriptText)
$encodedCommand = [System.Convert]::ToBase64String($scriptUtf16Bytes)

# Hash inputs: template bytes + NUL + script bytes (the raw bytes,
# not re-encoded, so the hash is reproducible even if the encoding
# detection logic is changed in a future revision of this script).
$hasher = [System.Security.Cryptography.SHA256]::Create()
try {
    $combined = New-Object byte[] ($templateBytes.Length + 1 + $scriptBytes.Length)
    [System.Buffer]::BlockCopy($templateBytes, 0, $combined, 0, $templateBytes.Length)
    $combined[$templateBytes.Length] = 0
    [System.Buffer]::BlockCopy($scriptBytes, 0, $combined, $templateBytes.Length + 1, $scriptBytes.Length)
    $hashBytes = $hasher.ComputeHash($combined)
    $hashHex = ([System.BitConverter]::ToString($hashBytes)).Replace('-', '')
}
finally {
    $hasher.Dispose()
}

# Substitute placeholders. Order matters only because __SCRIPT_BASE64__
# could in theory contain the __TASK_HASH__ literal -- highly unlikely
# but trivially defended by substituting hash first.
$finalXml = $templateText.
    Replace('__TASK_HASH__', $hashHex).
    Replace('__SCRIPT_BASE64__', $encodedCommand)

# Ensure output directory exists.
$outputDir = Split-Path -Parent $OutputPath
if ($outputDir -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Write UTF-16 LE with BOM (required by schtasks /Create /XML).
[System.IO.File]::WriteAllText(
    $OutputPath,
    $finalXml,
    (New-Object System.Text.UnicodeEncoding $false, $true))

Write-Host "Wrote $OutputPath ($([System.IO.File]::ReadAllBytes($OutputPath).Length) bytes, hash=$hashHex)"
