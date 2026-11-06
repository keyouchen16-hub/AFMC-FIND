param([string]$Folder = 'docs/evidence/discovery-0821')
$rows = foreach ($file in Get-ChildItem -LiteralPath $Folder -Filter afmc.json -Recurse) {
    $r = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $peer = $r.Samples | Where-Object { $_.Status.Peers -gt 0 } | Select-Object -First 1
    $payload = $r.Samples | Where-Object { $_.Status.Downloaded -gt 0 } | Select-Object -First 1
    [pscustomobject]@{
        Round = [IO.Path]::GetRelativePath((Resolve-Path $Folder).Path, $file.DirectoryName)
        FirstPeer = $peer.Seconds
        FirstPayload = $payload.Seconds
        Metadata = $r.MetadataSeconds
        Decoded = $r.FirstDecodedSeconds
        Observed = $r.PostStartObservedSeconds
        Stall = $r.StallSeconds
        LongestStall = $r.LongestStallSeconds
        RoutingReused = ($r.Samples.Status.RoutingReused | Measure-Object -Maximum).Maximum
        PeersReused = ($r.Samples.Status.PeersReused | Measure-Object -Maximum).Maximum
        Complete = $r.PostStartObservationComplete
        Outcome = if (-not $r.PostStartObservationComplete -and (Test-Path -LiteralPath (Join-Path $file.Directory.Parent.FullName 'STOPPED.md'))) {
            'Intentionally stopped after candidate rejection; partial, excluded from acceptance'
        } else { $r.Outcome }
    }
}
$rows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Folder 'summary.json') -Encoding utf8
$rows | Format-Table Round,FirstPeer,Metadata,Decoded,Stall,RoutingReused,Complete -AutoSize
