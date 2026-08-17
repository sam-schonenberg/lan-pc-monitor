param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'installer\LanPcMonitor.Installer.wixproj'

dotnet build $project `
    --configuration $Configuration `
    -p:InstallerVersion=$Version

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$installer = Join-Path $repositoryRoot "artifacts\installer\LanPcMonitor-$Version-win-x64.msi"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Installer build completed but the expected MSI was not found: $installer"
}

$hash = Get-FileHash -LiteralPath $installer -Algorithm SHA256
Set-Content -LiteralPath "$installer.sha256" -Value "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $installer)"

Write-Host "Installer: $installer"
Write-Host "SHA-256:  $($hash.Hash.ToLowerInvariant())"
