param([string]$Name = 'baseline', [switch]$DeadlineScheduler, [switch]$ShortBlockTimeout, [switch]$RelaxEndGame)
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[a-z0-9-]+$') { throw 'Invalid run name' }
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root '.tools\channel-bench\baseline-0820'
$target = Join-Path $root ".tools\piece-trace\$Name"
if (Test-Path -LiteralPath $target) { throw 'Preserve existing diagnostic snapshot' }
New-Item -ItemType Directory -Path $target | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse
$worker = Join-Path $target 'Streaming\stream_worker.py'
$original = [IO.File]::ReadAllText($worker)
$trace = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PieceTrace.py'))
if ($DeadlineScheduler) {
    $flag = 'self.handle.set_flags(lt.torrent_flags.sequential_download)'
    if (!$original.Contains($flag)) { throw 'Sequential flag boundary missing' }
    $original = $original.Replace($flag, 'self.handle.unset_flags(lt.torrent_flags.sequential_download)')
    $trace += "`n" + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PieceDeadlineCandidate.py'))
}
if ($ShortBlockTimeout) {
    $trace += @'

# Test-only: bound native block reassignment wait without banning peers.
_timeout_original_select = Worker.select
def _timeout_select(self, index):
    self.session.apply_settings({'request_timeout': 15, 'piece_timeout': 10})
    return _timeout_original_select(self, index)
Worker.select = _timeout_select
'@
}
if ($RelaxEndGame) {
    $trace += @'

# Test-only: permit idle peers to duplicate occupied blocks in the bounded window.
_endgame_original_select = Worker.select
def _endgame_select(self, index):
    self.session.apply_settings({'strict_end_game_mode': False})
    return _endgame_original_select(self, index)
Worker.select = _endgame_select
'@
}
$marker = "`ndef main():"
if (!$original.Contains($marker)) { throw 'Worker injection boundary missing' }
[IO.File]::WriteAllText($worker, $original.Replace($marker, "`n$trace`n$marker"), [Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path (Join-Path $root "docs\evidence\piece-scheduling\$Name") -Force | Out-Null
Write-Output "Prepared isolated diagnostic: $Name"
