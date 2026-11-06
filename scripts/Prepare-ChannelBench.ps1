$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root '.tools/channel-bench/bin'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$assets = @(
    @{Name='rqbit.exe'; Version='9.1.0'; Url='https://github.com/ikatson/rqbit/releases/download/v9.1.0/rqbit.exe'; Sha256='9fb0c0dbe63ea7ca04611039e0244e570d115e79d3a0d4ac2bba22b9d80f3cbc'},
    @{Name='TorrServer.exe'; Version='MatriX.149'; Url='https://github.com/YouROK/TorrServer/releases/download/MatriX.149/TorrServer-windows-amd64.exe'; Sha256='ac26f6658f6f686dc9e5f8884097ebe7e7935ce15aaf445488367300786dfbce'}
)
foreach ($asset in $assets) {
    $destination = Join-Path $folder $asset.Name
    if (-not (Test-Path -LiteralPath $destination)) {
        Invoke-WebRequest -Uri $asset.Url -OutFile $destination -TimeoutSec 180
    }
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $asset.Sha256) {
        throw "Official release digest mismatch: $($asset.Name). File will not be executed."
    }
    Write-Output "VERIFIED $($asset.Name) $($asset.Version)"
}
$assets | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'manifest.json') -Encoding utf8
