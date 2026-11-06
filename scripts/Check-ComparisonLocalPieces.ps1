$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$torrentPath = Join-Path $root '.tools\comparison.torrent'
$script:encoded = [IO.File]::ReadAllBytes($torrentPath)
if ($script:encoded.Length -gt 2MB) { throw 'Unexpected metadata size' }
$script:cursor = 0
$script:infoStart = 0
$script:infoEnd = 0
function Read-Bencode([int]$Depth = 0) {
    if ($Depth -gt 64 -or $script:cursor -ge $script:encoded.Length) { throw 'Invalid bencode boundary' }
    $tag = [char]$script:encoded[$script:cursor]
    if ($tag -eq 'd') {
        $script:cursor++
        $dict = @{}
        while ([char]$script:encoded[$script:cursor] -ne 'e') {
            $key = [Text.Encoding]::UTF8.GetString([byte[]](Read-Bencode ($Depth + 1)))
            $start = $script:cursor
            $dict[$key] = Read-Bencode ($Depth + 1)
            if ($Depth -eq 0 -and $key -eq 'info') { $script:infoStart = $start; $script:infoEnd = $script:cursor }
        }
        $script:cursor++
        return ,$dict
    }
    if ($tag -eq 'l') {
        $script:cursor++
        $list = [Collections.Generic.List[object]]::new()
        while ([char]$script:encoded[$script:cursor] -ne 'e') { $list.Add((Read-Bencode ($Depth + 1))) }
        $script:cursor++
        return ,($list.ToArray())
    }
    if ($tag -eq 'i') {
        $script:cursor++
        $start = $script:cursor
        while ([char]$script:encoded[$script:cursor] -ne 'e') { $script:cursor++ }
        $value = [long]::Parse([Text.Encoding]::ASCII.GetString($script:encoded, $start, $script:cursor - $start))
        $script:cursor++
        return $value
    }
    $start = $script:cursor
    while ([char]$script:encoded[$script:cursor] -ne ':') { $script:cursor++ }
    $count = [int]::Parse([Text.Encoding]::ASCII.GetString($script:encoded, $start, $script:cursor - $start))
    $script:cursor++
    if ($count -lt 0 -or $script:cursor + $count -gt $script:encoded.Length) { throw 'Invalid string boundary' }
    $value = [byte[]]::new($count)
    [Array]::Copy($script:encoded, $script:cursor, $value, 0, $count)
    $script:cursor += $count
    return ,$value
}
$meta = Read-Bencode
$sha = [Security.Cryptography.SHA1]::Create()
$actualHash = [Convert]::ToHexString($sha.ComputeHash($script:encoded, $script:infoStart, $script:infoEnd - $script:infoStart))
if ($actualHash -ne '2EE672984434179AA8A7C5D2EBD0A94B38EB2960') { throw 'Metadata does not match requested magnet' }
$info = $meta['info']
$video = $null
$offset = 0L
foreach ($entry in $info['files']) {
    $name = ($entry['path'] | ForEach-Object { [Text.Encoding]::UTF8.GetString([byte[]]$_) }) -join '/'
    if ($name.EndsWith('.mp4', [StringComparison]::OrdinalIgnoreCase)) { $video = @{ Offset = $offset; Length = [long]$entry['length'] } }
    $offset += [long]$entry['length']
}
if (!$video) { throw 'No MP4 in metadata' }
$folder = 'D:\迅雷下载\Spider-Man Brand New Day (2026) [1080p] [WEBRip] [5.1] [YTS.GG - YTS.BZ]'
$file = @(Get-ChildItem -LiteralPath $folder -Filter '*.mp4' -File)
if ($file.Count -ne 1 -or $file[0].Length -ne $video.Length) { throw 'Local video length or file count mismatch' }
$pieceLength = [int]$info['piece length']
$first = [int][Math]::Ceiling($video.Offset / [double]$pieceLength)
$endExclusive = [int][Math]::Floor(($video.Offset + $video.Length) / [double]$pieceLength)
$buffer = [byte[]]::new($pieceLength)
$checked = 0
$stream = [IO.File]::OpenRead($file[0].FullName)
try {
    $null = $stream.Seek($first * [long]$pieceLength - $video.Offset, [IO.SeekOrigin]::Begin)
    for ($index = $first; $index -lt $endExclusive; $index++) {
        $stream.ReadExactly($buffer, 0, $pieceLength)
        $actual = [Convert]::ToHexString($sha.ComputeHash($buffer))
        $expected = [Convert]::ToHexString([byte[]]$info['pieces'], $index * 20, 20)
        if ($actual -ne $expected) { throw "Local video piece mismatch: $index" }
        $checked++
    }
} finally { $stream.Dispose(); $sha.Dispose() }
$result = [pscustomobject]@{
    InfoHash = $actualHash; PieceLength = $pieceLength; CheckedCompleteVideoPieces = $checked
    CheckedBytes = $checked * [long]$pieceLength; VideoLength = $video.Length
    Passed = $true; BoundaryPiecesExcluded = 'Only whole pieces entirely inside the selected video are checked; shared file-boundary pieces are excluded.'
}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'docs\evidence\channel-bench\local-piece-verification.json') -Encoding utf8
$result | Format-List
