param([Parameter(Mandatory)][string]$Version)

$root    = $PSScriptRoot
$project = Join-Path $root "TheIsleStatReader\TheIsleStatReader.csproj"
$stage   = Join-Path $root "_release_stage"
$zipPath = Join-Path $root "TheIsleStatReader-$Version-win-x64.zip"

# Strip-list: files that must never ship
$stripNames = @('settings.json', 'oodle-data-shared.dll', 'zlib-ng2.dll')
$stripExts  = @('.pdb', '.xml')

Write-Host ""
Write-Host "=== Building release $Version ===" -ForegroundColor Cyan
Write-Host ""

# ── Clean stage ───────────────────────────────────────────────────────────────
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item $stage -ItemType Directory | Out-Null

# ── dotnet publish ────────────────────────────────────────────────────────────
Write-Host "[1/4] Publishing..."
dotnet publish $project `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $stage

if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed"; exit 1 }

# ── Strip unwanted files ──────────────────────────────────────────────────────
Write-Host "[2/4] Stripping dev/personal files..."
Get-ChildItem $stage | Where-Object {
    $stripNames -contains $_.Name -or $stripExts -contains $_.Extension
} | Remove-Item -Force

# ── Add distribution extras ───────────────────────────────────────────────────
Write-Host "[3/4] Adding AES key, mappings..."
$aes = Join-Path $root "AES_Key.txt"
if (Test-Path $aes) { Copy-Item $aes (Join-Path $stage "AES_Key.txt") }
else { Write-Warning "AES_Key.txt not found at $aes - skipping" }

Get-ChildItem $root -Filter "*.usmap" | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $stage $_.Name)
    Write-Host "  + $($_.Name)"
}

$licenses = Join-Path $root "TheIsleStatReader\THIRD_PARTY_LICENSES"
if (Test-Path $licenses) {
    Copy-Item $licenses (Join-Path $stage "THIRD_PARTY_LICENSES") -Recurse
    Write-Host "  + THIRD_PARTY_LICENSES\"
} else { Write-Warning "THIRD_PARTY_LICENSES not found - skipping" }

# ── Zip ───────────────────────────────────────────────────────────────────────
Write-Host "[4/4] Zipping..."
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zipPath

# ── Verify contents ───────────────────────────────────────────────────────────
$entries = [System.IO.Compression.ZipFile]::OpenRead($zipPath).Entries |
    Select-Object -ExpandProperty FullName
Write-Host ""
Write-Host "Release contents:" -ForegroundColor Green
$entries | ForEach-Object { Write-Host "  $_" }

# ── Clean up stage ────────────────────────────────────────────────────────────
Remove-Item $stage -Recurse -Force

Write-Host ""
Write-Host "Done: $zipPath" -ForegroundColor Green
Write-Host ""
