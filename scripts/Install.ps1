param([string]$SourceDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $SourceDirectory) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $root 'src\Afmc.Find.App\Afmc.Find.App.csproj')
    $SourceDirectory = Join-Path $root ('artifacts\AFMC-FIND-' + $project.Project.PropertyGroup.Version + '-win-x64')
}
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$target = Join-Path $env:LOCALAPPDATA 'Programs\AFMC FIND'
$exe = Join-Path $source 'AFMC.Find.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Release executable missing.' }
if (Test-Path -LiteralPath (Join-Path $source 'Streaming/stream_worker.py')) {
    & (Join-Path $PSScriptRoot 'Verify-StreamingRuntime.ps1') -RuntimeDirectory (Join-Path $source 'components/libtorrent')
}
. (Join-Path $PSScriptRoot 'ApplicationTransaction.ps1')
$preflight=Start-Process -FilePath $exe -ArgumentList '--check-config' -WindowStyle Hidden -PassThru
if(-not $preflight.WaitForExit(15000)){Stop-Process -InputObject $preflight -Force;throw 'Configuration preflight timed out; old application untouched.'}
if($preflight.ExitCode -ne 0){throw 'New version cannot read current profile; installation stopped, old application untouched.'}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$backup = Join-Path $env:LOCALAPPDATA "AFMC\Find-install-backups\$stamp"
New-Item -ItemType Directory -Force -Path $backup | Out-Null
$profile = Join-Path $env:LOCALAPPDATA 'AFMC\Find'
# Only verified project releases and the fixed installation are in scope.
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + '\'
$installRoot = [IO.Path]::GetFullPath($target) + '\'
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class AfmcUpdate {
 public delegate bool EnumProc(IntPtr hwnd,IntPtr parameter);
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern uint RegisterWindowMessage(string name);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
}
'@
$message = [AfmcUpdate]::RegisterWindowMessage('AFMC.Find.ExitForUpdate.v1')
$old = @(Get-Process AFMC.Find -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and ($_.Path.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase) -or $_.Path.StartsWith($installRoot,[StringComparison]::OrdinalIgnoreCase))
})
$oldIds = @($old | ForEach-Object Id)
$callback = [AfmcUpdate+EnumProc]{ param($hwnd,$parameter)
    [uint32]$ownerId=0
    [void][AfmcUpdate]::GetWindowThreadProcessId($hwnd,[ref]$ownerId)
    if ($oldIds -contains [int]$ownerId) { [void][AfmcUpdate]::PostMessage($hwnd,$message,[IntPtr]::Zero,[IntPtr]::Zero) }
    return $true
}
[void][AfmcUpdate]::EnumWindows($callback,[IntPtr]::Zero)
foreach ($process in $old) { if (-not $process.HasExited) { [void]$process.CloseMainWindow() } }
$forced = @()
foreach ($process in $old) {
    if (-not $process.WaitForExit(15000)) {
        # Pre-0.6.5 closes to tray and has no update-exit protocol.
        $forced += $process.Id
        Stop-Process -InputObject $process -Force
        if (-not $process.WaitForExit(5000)) { throw "Could not stop AFMC FIND $($process.Id)." }
    }
}
$script:installedProcess=$null
$payloadStarted=$false
try {
    # Snapshot after normal exit: the embedded engine owns an exclusive lock while running.
    if (Test-Path -LiteralPath $profile) {
        $profileBackup=Join-Path $backup 'profile';New-Item -ItemType Directory -Force -Path $profileBackup | Out-Null
        Get-ChildItem -LiteralPath $profile | Where-Object {$_.Name -notlike 'history*'} | Copy-Item -Destination $profileBackup -Recurse
    }
    $payloadStarted=$true
    Install-ApplicationPayload -Source $source -Target $target -BackupDirectory $backup -AllowedTargetRoot (Join-Path $env:LOCALAPPDATA 'Programs') -ValidateInstalled {
        param($path)
        $script:installedProcess=Start-Process -FilePath (Join-Path $path 'AFMC.Find.exe') -WorkingDirectory $path -PassThru
        try {
            if(-not $script:installedProcess.WaitForInputIdle(15000)){throw 'Installed application did not become ready.'}
            Start-Sleep -Seconds 2
            $script:installedProcess.Refresh()
            if($script:installedProcess.HasExited -or $script:installedProcess.MainWindowHandle -eq [IntPtr]::Zero){throw 'Installed application has no main window.'}
        } catch {
            if(-not $script:installedProcess.HasExited){Stop-Process -InputObject $script:installedProcess -Force;[void]$script:installedProcess.WaitForExit(5000)}
            throw
        }
    }
    $installedExe=Join-Path $target 'AFMC.Find.exe'
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'AFMC FIND.lnk'))
    $shortcut.TargetPath=$installedExe; $shortcut.WorkingDirectory=$target; $shortcut.IconLocation=$installedExe; $shortcut.Save()
    [ordered]@{ Target=$target; Backup=$backup; Version=(Get-Item (Join-Path $target 'AFMC.Find.dll')).VersionInfo.ProductVersion; Closed=$oldIds; LegacyForced=$forced; NewProcess=$script:installedProcess.Id; ConfigurationPreflight='Passed'; StagedHashes='Passed' } | ConvertTo-Json | Tee-Object -FilePath (Join-Path $backup 'install.json')
} catch {
    if((!$payloadStarted -or (Test-Path -LiteralPath (Join-Path $backup 'rollback.json'))) -and (Test-Path -LiteralPath (Join-Path $target 'AFMC.Find.exe'))){Start-Process -FilePath (Join-Path $target 'AFMC.Find.exe') -WorkingDirectory $target | Out-Null}
    throw "Install failed; transaction evidence and backup at $backup. $($_.Exception.Message)"
}
