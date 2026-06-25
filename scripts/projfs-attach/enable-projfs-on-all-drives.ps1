# enable-projfs-on-all-drives.ps1
#
# Source of truth for the EnableProjFSOnAllDrives scheduled task body.
# This script is NOT deployed to disk in the user-mode install model;
# instead, build-task-xml.ps1 base64-encodes the contents and embeds
# them in the task XML's <Exec><Arguments> as -EncodedCommand. The
# task then runs as: powershell.exe -EncodedCommand <base64-of-this>.
#
# Runs as LocalSystem (configured by the scheduled task) so it has
# SE_LOAD_DRIVER_PRIVILEGE for FilterAttach and HKLM write access
# for the Dev Drive allowed-filters registry.
#
# Two invocation modes (selected by the task's triggers):
#   1. AT_SYSTEM_START - no DriveLetter argument. Reconciles the Dev
#      Drive allow-list (machine-wide) and attaches prjflt to every
#      eligible NTFS/ReFS volume. FilterAttach is not persistent
#      across reboots, so this is required every boot.
#   2. Event 1006 from Microsoft-Windows-Partition/Diagnostic -
#      DriveLetter argument is the drive of the newly-mounted volume.
#      Attaches prjflt to just that one drive. Avoids work on every
#      USB plug-in / VHD mount.
#
# Logs to %ProgramData%\GVFS\enable-projfs-on-all-drives.log
# (HKLM-writable from SYSTEM, persistent across reboots).
#
# Idempotent everywhere: fltmc NameCollision is treated as success,
# fsutil devdrv setFiltersAllowed is a no-op if already set. Safe to
# run repeatedly.

[CmdletBinding()]
param(
    # If provided, only attempt to attach to this single drive letter.
    # Used by the volume-mount trigger to scope work narrowly. When
    # absent, all NTFS/ReFS volumes are processed (boot trigger path),
    # and the Dev Drive allow-list is also reconciled.
    [string]$DriveLetter
)

$ErrorActionPreference = 'Stop'

$logDir = Join-Path $env:ProgramData 'GVFS'
$logPath = Join-Path $logDir 'enable-projfs-on-all-drives.log'
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

function Write-Log([string]$msg) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $msg"
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

function Set-PrjFltDevDriveAllowed {
    # Dev Drives consult a machine-wide allow-list at mount time to
    # decide which minifilters may attach. Without PrjFlt in the list,
    # GVFS cannot work on Dev Drives even if we call FilterAttach.
    # Set unconditionally; fsutil is a no-op if already set.
    try {
        $out = (& fsutil.exe devdrv setFiltersAllowed PrjFlt 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -eq 0) {
            Write-Log "DevDrive allow-list: PrjFlt allowed (output: $out)"
        }
        else {
            # Non-fatal: on older Windows builds without Dev Drive
            # support, fsutil devdrv may fail. Log and continue.
            Write-Log "DevDrive allow-list: fsutil exit=$LASTEXITCODE (likely no Dev Drive support on this OS) output=$out"
        }
    }
    catch {
        Write-Log "DevDrive allow-list: exception (likely no Dev Drive support): $_"
    }
}

function Add-PrjFltToVolume([string]$drive) {
    $output = (& fltmc.exe attach PrjFlt "${drive}:" 2>&1 | Out-String).Trim()
    $exit = $LASTEXITCODE
    # NameCollision is success-equivalent: filter is already attached.
    # Check the output BEFORE the exit code because fltmc returns exit
    # 1 for NameCollision (despite it being benign).
    if ($output -match 'instance already exists' -or
        $output -match 'instance name collision' -or
        $output -match '0x801f0012') {
        Write-Log "OK   ${drive}: already attached (NameCollision)"
        return $true
    }
    if ($exit -ne 0) {
        Write-Log "FAIL ${drive}: exit=$exit output=$output"
        return $false
    }
    Write-Log "OK   ${drive}: attached (output: $output)"
    return $true
}

try {
    Write-Log "===== enable-projfs-on-all-drives.ps1 starting (DriveLetter='$DriveLetter') ====="

    if ($DriveLetter) {
        # Single-volume mode (volume-mount trigger)
        $drive = $DriveLetter.TrimEnd(':').TrimEnd('\').ToUpperInvariant()
        if ($drive.Length -ne 1) {
            Write-Log "ERROR: invalid DriveLetter '$DriveLetter' (parsed='$drive')"
            exit 2
        }
        $vol = Get-Volume -DriveLetter $drive -ErrorAction SilentlyContinue
        if (-not $vol) {
            Write-Log "SKIP ${drive}: volume not found"
            exit 0
        }
        if ($vol.FileSystemType -notin @('NTFS', 'ReFS')) {
            Write-Log "SKIP ${drive}: filesystem=$($vol.FileSystemType) (not NTFS/ReFS)"
            exit 0
        }
        Add-PrjFltToVolume $drive | Out-Null
    }
    else {
        # All-volumes mode (boot trigger). Reconcile both the Dev Drive
        # allow-list AND per-volume attachments. Cheap; idempotent.
        Set-PrjFltDevDriveAllowed
        $volumes = Get-Volume |
            Where-Object {
                $_.DriveLetter -and
                $_.FileSystemType -in @('NTFS', 'ReFS')
            }
        Write-Log "Found $(@($volumes).Count) eligible volume(s)"
        foreach ($v in $volumes) {
            Add-PrjFltToVolume ([string]$v.DriveLetter) | Out-Null
        }
    }

    Write-Log "===== enable-projfs-on-all-drives.ps1 done ====="
}
catch {
    Write-Log "EXCEPTION: $_"
    Write-Log $_.ScriptStackTrace
    exit 3
}
