param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [switch]$NoRestore
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
$restoreOptions = @()
if ($NoRestore) { $restoreOptions += '--no-restore' }
$webView2Installer = Join-Path $projectRoot '.tools\prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'

try {
    if (!(Test-Path -LiteralPath $webView2Installer)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $webView2Installer) -Force | Out-Null
        $savedProgress = $ProgressPreference
        try {
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/?LinkId=2124701' -OutFile $webView2Installer -UseBasicParsing
        }
        finally { $ProgressPreference = $savedProgress }
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $webView2Installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation' -or
        (Get-Item -LiteralPath $webView2Installer).Length -lt 20MB) {
        throw 'O instalador offline do WebView2 deve ter uma assinatura Microsoft válida.'
    }

    New-Item -ItemType Directory -Path (Join-Path $package 'Assets\Bridge') -Force | Out-Null

    & dotnet publish (Join-Path $projectRoot 'LeanBrowser\LeanBrowser.csproj') -c Release -r win-x64 --self-contained true -o $app "-p:Version=$Version" @restoreOptions
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o navegador.' }

    & dotnet publish (Join-Path $projectRoot 'CottonUpdater\CottonUpdater.csproj') -c Release -r win-x64 --self-contained true -o $updater "-p:Version=$Version" @restoreOptions
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o atualizador.' }

    foreach ($folder in @($app, $updater)) {
        if (Get-ChildItem -LiteralPath $folder -Recurse -File -Filter '*.dll') {
            throw "A publicação deixou dependências fora do executável: $folder"
        }
    }

    Copy-Item (Join-Path $app 'CottonBrowser.exe') $package
    Copy-Item (Join-Path $updater 'CottonUpdater.exe') $package
    Copy-Item (Join-Path $app 'Assets\Bridge\*') (Join-Path $package 'Assets\Bridge')

    if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload }
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $payload

    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Copy-Item $payload (Join-Path $output 'CottonBrowser-win-x64.zip') -Force

    & dotnet publish (Join-Path $installerRoot 'CottonInstaller.csproj') -c Release -r win-x64 --self-contained true -o $output "-p:Version=$Version" "-p:WebView2InstallerPath=$webView2Installer" @restoreOptions
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }

    # Reproduz o envio de somente um EXE, sem DLLs nem instalação do .NET ao lado.
    $check = Join-Path $work 'standalone-check'
    New-Item -ItemType Directory -Path $check -Force | Out-Null
    $standaloneSetup = Join-Path $check 'CottonBrowserSetup.exe'
    Copy-Item (Join-Path $output 'CottonBrowserSetup.exe') $standaloneSetup
    $savedExtraction = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
    try {
        $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $work 'bundle-extraction'
        $process = Start-Process -FilePath $standaloneSetup -ArgumentList '--verify-package' -WorkingDirectory $check -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "O instalador isolado não passou na verificação (código $($process.ExitCode))." }
    }
    finally { $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $savedExtraction }

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
