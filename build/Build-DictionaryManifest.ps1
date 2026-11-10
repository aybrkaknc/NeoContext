# Copies the dictionary sources to .\dictionaries and writes manifest.json with each file's SHA-256.
# Commit the result; the app fetches these files from the main branch.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'src\NeoContext.Core\Dictionaries'
$target = Join-Path $root 'dictionaries'
New-Item -ItemType Directory -Force $target | Out-Null

$files = foreach ($name in 'EnhanceMenusDic.xml', 'DetailedEditDic.xml', 'GuidInfosDic.ini') {
    # LF only, so the hash is the same on every machine and in git (see .gitattributes).
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $source $name))
    $text = [System.Text.Encoding]::UTF8.GetString($bytes).Replace("`r`n", "`n")
    [System.IO.File]::WriteAllBytes((Join-Path $target $name), (New-Object System.Text.UTF8Encoding($false)).GetBytes($text))
    [ordered]@{ name = $name; sha256 = (Get-FileHash (Join-Path $target $name) -Algorithm SHA256).Hash.ToLowerInvariant() }
}

$json = [ordered]@{ files = @($files) } | ConvertTo-Json -Depth 3
[System.IO.File]::WriteAllText((Join-Path $target 'manifest.json'), $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote $($files.Count) files and manifest.json to $target"
