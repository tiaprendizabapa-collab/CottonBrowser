param([string]$DistPath = (Join-Path $PSScriptRoot 'dist'))

$ErrorActionPreference = 'Stop'

try {
    $browser = $PSScriptRoot
    $root = Split-Path $browser -Parent
    $stamp = Join-Path $DistPath '.build-stamp'
    if (!(Test-Path -LiteralPath $stamp -PathType Leaf) -or
        !(Test-Path -LiteralPath (Join-Path $DistPath 'CottonBrowser.exe') -PathType Leaf) -or
        !(Test-Path -LiteralPath (Join-Path $DistPath 'CottonUpdater.exe') -PathType Leaf)) {
        exit 1
    }

    $builtAt = (Get-Item -LiteralPath $stamp).LastWriteTimeUtc
    $sourceDirs = @(
        $browser,
        (Join-Path $browser 'Assets'),
        (Join-Path $browser 'Assets\Bridge'),
        (Join-Path $browser 'Assets\Recovery'),
        (Join-Path $root 'CottonUpdater'),
        (Join-Path $root 'Shared')
    )
    foreach ($dir in $sourceDirs) {
        if (!(Test-Path -LiteralPath $dir -PathType Container)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $dir -File) {
            if ($file.Extension -notin @('.cs', '.csproj', '.manifest', '.js', '.html', '.css',
                '.png', '.ico', '.zip', '.txt', '.mp3', '.mp4', '.cmd')) { continue }
            if ($file.LastWriteTimeUtc -gt $builtAt) { exit 1 }
        }
    }

    $bridge = Join-Path $browser 'Assets\Bridge'
    foreach ($file in Get-ChildItem -LiteralPath $bridge -File -Recurse) {
        $relative = $file.FullName.Substring($bridge.Length).TrimStart('\')
        if (!(Test-Path -LiteralPath (Join-Path $DistPath (Join-Path 'Assets\Bridge' $relative)) -PathType Leaf)) {
            exit 1
        }
        if ($file.LastWriteTimeUtc -gt $builtAt) { exit 1 }
    }
    exit 0
}
catch {
    Write-Error "Nao foi possivel verificar a compilacao local: $_"
    exit 2
}

