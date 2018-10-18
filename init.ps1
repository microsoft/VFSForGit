# Delegate the work to init.cmd and save off the environment it creates to a temp file.
$cwd = (Get-Location)
$tmp = [IO.Path]::GetTempFileName()
cmd.exe /c "$PSScriptRoot\init.cmd && set>$tmp"

# Read the env vars from the temp file and set it into this shell.
$lines = [System.IO.File]::ReadAllLines("$tmp")
Set-Location Env:
foreach ($line in $lines) {
    $envVar = $line.Split('=')
    Set-Item -Path $envVar[0] -Value $envVar[1]
}
Set-Location $cwd

# Set up macros.
& "$PSScriptRoot\Scripts\Macros.ps1"

$Host.UI.RawUI.WindowTitle = "VFS for Git Developer Shell ($Env:VFS_SRCDIR)"
