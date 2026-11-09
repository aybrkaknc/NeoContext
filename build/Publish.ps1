<#
.SYNOPSIS
    Builds the release artifacts: a portable zip and an MSIX package per architecture.
.DESCRIPTION
    Output goes to ./artifacts:
      NeoContext-<version>-win-<arch>.zip    portable, self-contained (no installer needed)
      NeoContext-<version>-win-<arch>.msix   package; signed when a certificate is supplied
      SHA256SUMS.txt                         checksums of everything above

    To sign the MSIX, pass -CertificatePath and -CertificatePassword (a .pfx whose subject matches
    the Publisher in Package.appxmanifest). Unsigned packages are still produced for testing, but
    Windows only installs signed ones.
.EXAMPLE
    pwsh build/Publish.ps1 -Version 0.1.0
#>
param(
    [string]$Version = '0.1.0',
    [string[]]$Architectures = @('x64', 'arm64'),
    [string]$Configuration = 'Release',
    [string]$CertificatePath,
    [string]$CertificatePassword
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src/NeoContext.App/NeoContext.App.csproj'
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

# MSIX versions have four numeric parts.
$msixVersion = (($Version -replace '-.*$', '') + '.0.0.0').Split('.')[0..3] -join '.'

foreach ($arch in $Architectures) {
    $platform = if ($arch -eq 'arm64') { 'ARM64' } else { 'x64' }
    $rid = "win-$arch"

    # Portable build. Do not pass -o to the build itself: it changes the compiled XAML paths
    # in the resource index. Publish to the default folder and copy from there.
    Write-Host "== Portable $arch" -ForegroundColor Cyan
    dotnet publish $project -c $Configuration -r $rid -p:Platform=$platform -p:WindowsPackageType=None `
        -p:Version=$Version --self-contained
    if ($LASTEXITCODE) { throw "Portable publish failed for $arch" }

    $publishDir = Join-Path $root "src/NeoContext.App/bin/$platform/$Configuration/net10.0-windows10.0.19041.0/$rid/publish"
    $zip = Join-Path $artifacts "NeoContext-$Version-$rid.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip

    # MSIX package.
    Write-Host "== MSIX $arch" -ForegroundColor Cyan
    $packageDir = Join-Path $root "src/NeoContext.App/AppPackages/$arch/"
    $msixArgs = @(
        'publish', $project, '-c', $Configuration, '-r', $rid, "-p:Platform=$platform",
        '-p:GenerateAppxPackageOnBuild=true', '-p:AppxBundle=Never', "-p:AppxPackageDir=$packageDir",
        "-p:Version=$Version", "-p:AppxPackageVersion=$msixVersion"
    )
    if ($CertificatePath) {
        $msixArgs += @('-p:AppxPackageSigningEnabled=true', "-p:PackageCertificateKeyFile=$CertificatePath", "-p:PackageCertificatePassword=$CertificatePassword")
    } else {
        $msixArgs += '-p:AppxPackageSigningEnabled=false'
    }

    dotnet @msixArgs
    if ($LASTEXITCODE) { throw "MSIX publish failed for $arch" }

    $msix = Get-ChildItem $packageDir -Recurse -Filter *.msix | Sort-Object LastWriteTime | Select-Object -Last 1
    Copy-Item $msix.FullName (Join-Path $artifacts "NeoContext-$Version-$rid.msix") -Force
}

# Checksums, so anyone can verify downloads by hand as well.
$sums = Get-ChildItem $artifacts -File | Where-Object Extension -in '.zip', '.msix' | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
}
Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') $sums
Get-ChildItem $artifacts | Format-Table Name, Length
