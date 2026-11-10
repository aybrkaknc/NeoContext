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
    [string]$Configuration = 'Release'
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

    $publishDir = Join-Path $root "src/NeoContext.App/bin/$platform/$Configuration/net10.0-windows10.0.19041.0/$rid/publish"
    $zip = Join-Path $artifacts "NeoContext-$Version-$rid.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip
}

# Checksums, so anyone can verify downloads by hand as well.
$sums = Get-ChildItem $artifacts -File -Filter *.zip | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
}
Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') $sums
Get-ChildItem $artifacts | Format-Table Name, Length
