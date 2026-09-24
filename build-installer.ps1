param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$installerRoot = Join-Path $projectRoot 'CottonInstaller'
$work = Join-Path $installerRoot ('obj\bundle-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $work 'app'
$updater = Join-Path $work 'updater'
$package = Join-Path $work 'package'
$payload = Join-Path $installerRoot 'Payload.zip'
$output = Join-Path $installerRoot 'dist'

try {
    New-Item -ItemType Directory -Path (Join-Path $package 'Assets\Bridge') -Force | Out-Null

    & dotnet publish (Join-Path $projectRoot 'LeanBrowser\LeanBrowser.csproj') -c Release -r win-x64 --self-contained true -o $app "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o navegador.' }

    & dotnet publish (Join-Path $projectRoot 'CottonUpdater\CottonUpdater.csproj') -c Release -r win-x64 --self-contained true -o $updater "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o atualizador.' }

    Copy-Item (Join-Path $app 'CottonBrowser.exe') $package
    Copy-Item (Join-Path $updater 'CottonUpdater.exe') $package
    Copy-Item (Join-Path $app 'Assets\Bridge\*') (Join-Path $package 'Assets\Bridge')

    if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload }
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $payload

    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Copy-Item $payload (Join-Path $output 'CottonBrowser-win-x64.zip') -Force

    & dotnet publish (Join-Path $installerRoot 'CottonInstaller.csproj') -c Release -r win-x64 --self-contained true -o $output "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }

    Write-Host "Instalador: $(Join-Path $output 'CottonBrowserSetup.exe')"
    Write-Host "Pacote de atualização: $(Join-Path $output 'CottonBrowser-win-x64.zip')"
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath((Join-Path $installerRoot 'obj'))
    $resolvedWork = [IO.Path]::GetFullPath($work)
    if ($resolvedWork.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedWork)) {
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}
