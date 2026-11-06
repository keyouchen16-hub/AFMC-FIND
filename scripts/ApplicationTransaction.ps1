function Install-ApplicationPayload {
    param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Target,[Parameter(Mandatory)][string]$BackupDirectory,[Parameter(Mandatory)][string]$AllowedTargetRoot,[Parameter(Mandatory)][scriptblock]$ValidateInstalled)
    $ErrorActionPreference='Stop'
    $targetPath=[IO.Path]::GetFullPath($Target).TrimEnd('\')
    $allowed=[IO.Path]::GetFullPath($AllowedTargetRoot).TrimEnd('\')+'\'
    $backupPath=[IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')
    $sourcePath=(Resolve-Path -LiteralPath $Source).Path.TrimEnd('\')
    if(-not $targetPath.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or $targetPath -eq $sourcePath -or $backupPath.StartsWith($targetPath+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe application transaction paths.'}
    foreach($path in @($targetPath,$backupPath)){
        if((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Refusing a redirected installation or backup directory.'}
    }
    $stage=Join-Path $backupPath 'staged-application'
    $previous=Join-Path $backupPath 'application'
    $failed=Join-Path $backupPath 'failed-application'
    if((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $previous) -or (Test-Path -LiteralPath $failed)){throw 'Transaction backup paths must be new.'}
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    $movedOld=$false;$movedNew=$false
    try {
        Get-ChildItem -LiteralPath $sourcePath | Copy-Item -Destination $stage -Recurse -Force
        foreach($file in Get-ChildItem -LiteralPath $sourcePath -Recurse -File){
            $relative=$file.FullName.Substring($sourcePath.Length).TrimStart('\')
            if((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $stage $relative)).Hash){throw "Staging hash mismatch: $relative"}
        }
        if(Test-Path -LiteralPath $targetPath){Move-Item -LiteralPath $targetPath -Destination $previous;$movedOld=$true}
        Move-Item -LiteralPath $stage -Destination $targetPath;$movedNew=$true
        & $ValidateInstalled $targetPath
    } catch {
        $failure=$_
        if($movedNew -and (Test-Path -LiteralPath $targetPath)){Move-Item -LiteralPath $targetPath -Destination $failed}
        if($movedOld){Move-Item -LiteralPath $previous -Destination $targetPath}
        [ordered]@{RestoredPrevious=$movedOld;FailedApplication=$failed;Target=$targetPath} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $backupPath 'rollback.json')
        throw $failure
    }
}
