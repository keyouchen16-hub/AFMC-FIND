$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$evidence = Join-Path $root 'docs\evidence\channel-bench'
$cases = @(
    @{ Name = 'rqbit-cold1'; File = 'result.json' },
    @{ Name = 'torrserver-cold1'; File = 'result.json' },
    @{ Name = 'baseline-0820'; File = 'afmc.json' },
    @{ Name = 'rqbit-cold2'; File = 'result.json' }
)
$rows = foreach ($case in $cases) {
    $path = Join-Path (Join-Path $evidence $case.Name) $case.File
    if (!(Test-Path -LiteralPath $path)) { throw "Missing benchmark: $($case.Name)" }
    $run = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($run.Outcome -eq 'Running') { throw "Benchmark still running: $($case.Name)" }
    [pscustomobject]@{
        Name = $case.Name
        MetadataSeconds = $run.MetadataSeconds
        FirstDecodedSeconds = $run.FirstDecodedSeconds
        PostStartObservedSeconds = $run.PostStartObservedSeconds
        PostStartObservationComplete = $run.PostStartObservationComplete
        PlaybackAdvancedSeconds = $run.PlaybackAdvancedSeconds
        StallSeconds = $run.StallSeconds
        LongestStallSeconds = $run.LongestStallSeconds
        Outcome = $run.Outcome
        AuthVerified = $run.AuthVerified
        ProcessStopped = $run.ProcessStopped
        EvidenceSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
}
$rows | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'summary.json') -Encoding utf8
$rows | Select-Object Name,MetadataSeconds,FirstDecodedSeconds,StallSeconds,PostStartObservationComplete | Format-Table -AutoSize
