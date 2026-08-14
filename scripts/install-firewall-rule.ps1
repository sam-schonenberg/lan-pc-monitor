param(
    [ValidateRange(1, 65535)]
    [int]$Port = 5005
)

$ErrorActionPreference = 'Stop'
$ruleName = 'LAN PC Monitor - API'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Administrator privileges are required to modify Windows Firewall.'
    exit 5
}

try {
    Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction Stop

    New-NetFirewallRule `
        -DisplayName $ruleName `
        -Description 'Allows LAN PC Monitor API access from the local subnet on private networks only.' `
        -Direction Inbound `
        -Action Allow `
        -Protocol TCP `
        -LocalPort $Port `
        -Profile Private `
        -RemoteAddress LocalSubnet | Out-Null

    Write-Host "Firewall rule '$ruleName' installed for private local-subnet TCP traffic on port $Port."
    exit 0
}
catch {
    Write-Error "Failed to install firewall rule: $($_.Exception.Message)"
    exit 1
}
