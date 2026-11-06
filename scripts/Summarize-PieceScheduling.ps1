$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$evidence = Join-Path $root 'docs\evidence\piece-scheduling'
$records = @()
foreach ($file in Get-ChildItem -LiteralPath $evidence -Recurse -File | Where-Object Name -in 'afmc.json','progress.json') {
    $r = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($r.Outcome -eq 'Running') { throw "Unfinished run: $($file.Directory.Name)" }
    $relative = [IO.Path]::GetRelativePath($evidence, $file.DirectoryName)
    $family = ($relative -split '[\\/]')[0]
    if ($family -eq 'product-visible') { $family = 'product-reuse' }
    $worker = Join-Path $root ".tools\piece-trace\$family\Streaming\stream_worker.py"
    $after = @($r.Samples | Where-Object { $null -ne $r.FirstDecodedSeconds -and $_.Status -and $_.Seconds -ge $r.FirstDecodedSeconds })
    $payloadRate = if ($after.Count -ge 2 -and $after[-1].Seconds -gt $after[0].Seconds) {
        [math]::Round(($after[-1].Status.Downloaded-$after[0].Status.Downloaded)/($after[-1].Seconds-$after[0].Seconds)/1024, 2)
    } else { $null }
    $records += [ordered]@{
        Run = $relative
        Hash = $r.Hash
        MetadataSeconds = $r.MetadataSeconds
        FirstDecodedSeconds = $r.FirstDecodedSeconds
        PostStartObservedSeconds = $r.PostStartObservedSeconds
        Complete = $r.PostStartObservationComplete
        StallSeconds = if ($null -ne $r.FirstDecodedSeconds) { $r.StallSeconds } else { $null }
        LongestStallSeconds = if ($null -ne $r.FirstDecodedSeconds) { $r.LongestStallSeconds } else { $null }
        AfterStartPayloadKiBps = $payloadRate
        VisibleSamples = $r.VisibleSamples
        OccludedSamples = $r.OccludedSamples
        PlaybackToWallRatio = $r.PlaybackToWallRatio
        Outcome = $r.Outcome
        ResultSha256 = (Get-FileHash -LiteralPath $file.FullName).Hash
        WorkerSha256 = (Get-FileHash -LiteralPath $worker).Hash
    }
}
$records | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'summary.json') -Encoding utf8
$records | ForEach-Object { [PSCustomObject]$_ } | Format-Table Run,FirstDecodedSeconds,StallSeconds,Complete
