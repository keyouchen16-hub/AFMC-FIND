param([switch]$Live, [switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    & (Join-Path $PSScriptRoot 'Verify-StreamingRuntime.ps1')
    & $dotnet build Afmc.Find.slnx -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --search-responsiveness
    if ($LASTEXITCODE -ne 0) { throw 'Search responsiveness checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --resource-groups
    if ($LASTEXITCODE -ne 0) { throw 'Resource grouping checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --resource-display
    if ($LASTEXITCODE -ne 0) { throw 'Resource display checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --ui-motion
    if ($LASTEXITCODE -ne 0) { throw 'Motion preference checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --browser
    if ($LASTEXITCODE -ne 0) { throw 'Browser discovery checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --discovery
    if ($LASTEXITCODE -ne 0) { throw 'Website discovery checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --discovery-deep
    if ($LASTEXITCODE -ne 0) { throw 'Deep discovery regression checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --discovery-ui
    if ($LASTEXITCODE -ne 0) { throw 'Website discovery UI checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --dialog-glass
    if ($LASTEXITCODE -ne 0) { throw 'Dialog glass checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --jackett
    if ($LASTEXITCODE -ne 0) { throw 'Jackett configuration checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --global
    if ($LASTEXITCODE -ne 0) { throw 'Core checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --expanded
    if ($LASTEXITCODE -ne 0) { throw 'Expanded source checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --reliable
    if ($LASTEXITCODE -ne 0) { throw 'Reliable source checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --media
    if ($LASTEXITCODE -ne 0) { throw 'Media source checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --piratebay
    if ($LASTEXITCODE -ne 0) { throw 'Pirate Bay checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --foss
    if ($LASTEXITCODE -ne 0) { throw 'FOSS source checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --ui
    if ($LASTEXITCODE -ne 0) { throw 'Native UI checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --quality
    if ($LASTEXITCODE -ne 0) { throw 'Search quality and privacy checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --integrity
    if ($LASTEXITCODE -ne 0) { throw 'Metadata and download integrity checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --workbench
    if ($LASTEXITCODE -ne 0) { throw 'Workbench interaction checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --tray
    if ($LASTEXITCODE -ne 0) { throw 'Tray and settings checks failed' }
    & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --playback
    if ($LASTEXITCODE -ne 0) { throw 'Native progressive playback checks failed' }
    if ($Live) {
        & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --discovery-live
        if ($LASTEXITCODE -ne 0) { throw 'Website discovery live checks failed' }
        & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --engine-live
        if ($LASTEXITCODE -ne 0) { throw 'Embedded engine live checks failed' }
        & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --engine-ui
        if ($LASTEXITCODE -ne 0) { throw 'Embedded engine native UI checks failed' }
        & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --live
        if ($LASTEXITCODE -ne 0) { throw 'Live checks failed' }
        & $dotnet run --project tests/Afmc.Find.Checks -c Release --no-build -- --ui --live
        if ($LASTEXITCODE -ne 0) { throw 'Native live checks failed' }
    }
    if ($Publish) {
        [xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/Afmc.Find.App/Afmc.Find.App.csproj')
        $release = Join-Path $root ('artifacts/AFMC-FIND-' + $project.Project.PropertyGroup.Version + '-win-x64')
        & $dotnet publish src/Afmc.Find.App -c Release -r win-x64 --self-contained true -o $release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
        & (Join-Path $PSScriptRoot 'Verify-StreamingRuntime.ps1') -RuntimeDirectory (Join-Path $release 'components/libtorrent')
        & (Join-Path $PSScriptRoot 'Bundle-Engine.ps1') -ReleaseDirectory $release
        & (Join-Path $PSScriptRoot 'Package.ps1') -ReleaseDirectory $release
        & (Join-Path $PSScriptRoot 'Install.ps1') -SourceDirectory $release
    }
} finally { Pop-Location }


