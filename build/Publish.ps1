<#
.SYNOPSIS
    Builds the release artifacts: a portable, self-contained zip per architecture.
.DESCRIPTION
    Output goes to ./artifacts:
      NeoContext-<version>-win-<arch>.zip    portable, self-contained (no installer needed)
      SHA256SUMS.txt                         checksums of the zips
.EXAMPLE
    pwsh build/Publish.ps1 -Version 0.1.0
#>
param(
    [string]$Version = '0.1.0',
    [string[]]$Architectures = @('x64', 'arm64'),
    [string]$Configuration = 'Release',
    [switch]$SmokeTest
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src/NeoContext.App/NeoContext.App.csproj'
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

foreach ($arch in $Architectures) {
    $platform = if ($arch -eq 'arm64') { 'ARM64' } else { 'x64' }
    $rid = "win-$arch"

    # Do not pass -o to the build itself: it changes the compiled XAML paths in the resource index.
    # Publish to the default folder and copy from there.
    Write-Host "== Portable $arch" -ForegroundColor Cyan
    dotnet publish $project -c $Configuration -r $rid -p:Platform=$platform -p:WindowsPackageType=None `
        -p:Version=$Version --self-contained
    if ($LASTEXITCODE) { throw "Portable publish failed for $arch" }

    $outDir = Join-Path $root "src/NeoContext.App/bin/$platform/$Configuration/net10.0-windows10.0.19041.0/$rid"
    $publishDir = Join-Path $outDir 'publish'

    # `dotnet publish` leaves out the app's compiled XAML (App.xbf) and its resource index (NeoContext.pri).
    # Without them the app closes right at start-up. Both sit in the build output of this same build.
    foreach ($name in 'NeoContext.pri', 'App.xbf') {
        $source = Join-Path $outDir $name
        if (-not (Test-Path $source)) { throw "Missing $name in $outDir" }
        Copy-Item $source $publishDir -Force
    }

    $zip = Join-Path $artifacts "NeoContext-$Version-$rid.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip

    # Optional: unpack the zip and start the app, to catch a package that closes at once.
    # Off by default because it needs a desktop session; the architecture must match this machine.
    if ($SmokeTest -and $arch -eq 'x64' -and $env:PROCESSOR_ARCHITECTURE -eq 'AMD64') {
        $check = Join-Path ([System.IO.Path]::GetTempPath()) "neocontext-smoke-$arch"
        if (Test-Path $check) { Remove-Item $check -Recurse -Force }
        Expand-Archive -Path $zip -DestinationPath $check
        $process = Start-Process -FilePath (Join-Path $check 'NeoContext.exe') -PassThru
        Start-Sleep -Seconds 10
        if ($process.HasExited) { throw "Smoke test failed: NeoContext.exe exited with code $($process.ExitCode)" }
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(10000) | Out-Null
        Write-Host "Smoke test passed for $arch" -ForegroundColor Green

        # Cleanup is best effort: Windows may hold a file for a moment after the process ends.
        Remove-Item $check -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Checksums, so anyone can verify downloads by hand as well.
$sums = Get-ChildItem $artifacts -File -Filter *.zip | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
}
Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') $sums
Get-ChildItem $artifacts | Format-Table Name, Length
