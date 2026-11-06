function Test-InstalledPlayerDrag($player, $overlay, [bool]$playing) {
    $hostHandle = [IntPtr]$player.Current.NativeWindowHandle
    $overlayHandle = [IntPtr]$overlay.Current.NativeWindowHandle
    [void][PlayerAcceptance]::SetWindowPos($hostHandle, [IntPtr](-1), 0, 0, 0, 0, 0x43)
    try {
        [void][PlayerAcceptance]::SetForegroundWindow($overlayHandle)
        Start-Sleep -Milliseconds 300
        $brand = (Id $player 'PlayerDragArea').Current.BoundingRectangle
        $point = [Drawing.Point]::new([int]($brand.Left + 100), [int]($brand.Top + $brand.Height / 2))
        [void][PlayerAcceptance]::SetCursorPos($point.X, $point.Y)
        Start-Sleep -Milliseconds 100
        if ([PlayerAcceptance]::WindowFromPoint($point) -ne $overlayHandle) { throw 'Drag point is not the installed player overlay; no input sent' }
        $before = $player.Current.BoundingRectangle
        $timeBefore = (Id $player 'TimeLabel').Current.Name
        [PlayerAcceptance]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
        $samples = @()
        try {
            for ($step = 1; $step -le 24; $step++) {
                [void][PlayerAcceptance]::SetCursorPos($point.X + $step * 8, $point.Y + $step * 3)
                Start-Sleep -Milliseconds 40
                $rect = $player.Current.BoundingRectangle
                $samples += [pscustomobject]@{Step=$step;X=$rect.Left;Y=$rect.Top}
            }
        } finally { [PlayerAcceptance]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero) }
        Start-Sleep -Milliseconds 650
        $after = $player.Current.BoundingRectangle
        $layer = $overlay.Current.BoundingRectangle
        if ($after.Left - $before.Left -lt 100) { throw 'Installed player did not actually move' }
        if ([Math]::Abs($after.Left - $layer.Left) -gt 2 -or [Math]::Abs($after.Top - $layer.Top) -gt 2) { throw 'Installed overlay not aligned after drag' }
        if ($playing) { Wait-For { (Id $player 'TimeLabel').Current.Name -ne $timeBefore } 'installed playback clock continues after drag' 5 }
        $brandAfter = (Id $player 'PlayerDragArea').Current.BoundingRectangle
        $hitPoint = [Drawing.Point]::new([int]($brandAfter.Left + 100),[int]($brandAfter.Top+$brandAfter.Height/2))
        if ([PlayerAcceptance]::WindowFromPoint($hitPoint) -ne $overlayHandle) { throw 'Overlay input not restored after drag' }
        [pscustomobject]@{Playing=$playing;Before=$before.ToString();After=$after.ToString();Overlay=$layer.ToString();TimeBefore=$timeBefore;TimeAfter=(Id $player 'TimeLabel').Current.Name;Samples=$samples;InputRestored=$true} |
            ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $evidence "installed-drag-$playing.json")
        Capture $player "installed-drag-$playing"
        Write-Output "PASS installed window drag playing=$playing; real mouse movement, final overlay alignment and input restored"
    } finally { [void][PlayerAcceptance]::SetWindowPos($hostHandle, [IntPtr](-2), 0, 0, 0, 0, 0x03) }
}
