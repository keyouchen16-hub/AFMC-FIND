from pathlib import Path
p=Path('scripts/Install.ps1');s=p.read_text(encoding='utf-8-sig')
s=s.replace("if (-not (Test-Path -LiteralPath $exe)) { throw 'Release executable missing.' }", """if (-not (Test-Path -LiteralPath $exe)) { throw 'Release executable missing.' }
. (Join-Path $PSScriptRoot 'ApplicationTransaction.ps1')
$preflight=Start-Process -FilePath $exe -ArgumentList '--check-config' -WindowStyle Hidden -PassThru
if(-not $preflight.WaitForExit(15000)){Stop-Process -InputObject $preflight -Force;throw 'Configuration preflight timed out; old application untouched.'}
if($preflight.ExitCode -ne 0){throw 'New version cannot read current profile; installation stopped, old application untouched.'}""")
s=s.replace("if (Test-Path -LiteralPath $profile) { Copy-Item -LiteralPath $profile -Destination (Join-Path $backup 'profile') -Recurse }", """if (Test-Path -LiteralPath $profile) {
    $profileBackup=Join-Path $backup 'profile';New-Item -ItemType Directory -Force -Path $profileBackup | Out-Null
    Get-ChildItem -LiteralPath $profile | Where-Object {$_.Name -notlike 'history*'} | Copy-Item -Destination $profileBackup -Recurse
}""")
start=s.index("if (Test-Path -LiteralPath $target) { Copy-Item")
s=s[:start]+'''$script:installedProcess=$null
try {
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
    if((Test-Path -LiteralPath (Join-Path $backup 'rollback.json')) -and (Test-Path -LiteralPath (Join-Path $target 'AFMC.Find.exe'))){Start-Process -FilePath (Join-Path $target 'AFMC.Find.exe') -WorkingDirectory $target | Out-Null}
    throw "Install failed; transaction evidence and backup at $backup. $($_.Exception.Message)"
}
'''
p.write_text(s,encoding='utf-8')
