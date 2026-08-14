$ErrorActionPreference = 'Stop'
$ruleName = 'LAN PC Monitor - API'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Administrator privileges are required to modify Windows Firewall.'
    exit 5
}

try {
    $rules = @(Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)
    if ($rules.Count -eq 0) {
        Write-Host "Firewall rule '$ruleName' is not installed."
        exit 0
    }

    $rules | Remove-NetFirewallRule -ErrorAction Stop
    Write-Host "Firewall rule '$ruleName' removed."
    exit 0
}
catch {
    Write-Error "Failed to remove firewall rule: $($_.Exception.Message)"
    exit 1
}
