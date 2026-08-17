# Installer and updates

[Back to project overview](../README.md) · [Security](SECURITY.md)

LAN PC Monitor ships its Windows service and tray companion as one self-contained x64 MSI. The installer is authored with WiX Toolset 6.0.1 and does not require WiX or the .NET runtime on the destination PC.

## Installer behavior

The MSI:

- installs under `%ProgramFiles%\LAN PC Monitor` by default;
- registers `PCMonitor` as an automatically started Windows service;
- stops the service safely during upgrades and uninstallation;
- configures three restart-on-failure attempts with a five-second delay;
- creates the `LAN PC Monitor - API` inbound firewall rule for TCP port `5005`;
- restricts that rule to the local subnet while allowing all Windows network profiles, including Public;
- adds a Start Menu shortcut for the optional tray companion;
- starts the tray companion automatically whenever a user signs in;
- prevents duplicate tray companion instances within the same Windows session;
- provides one context-sensitive tray command to enable or disable the service and its automatic startup;
- launches the MSI-managed Windows uninstall flow from the tray companion;
- uses LAN PC Monitor branding throughout setup and in Windows Installed Apps;
- opens the local pairing page in the default browser when setup finishes;
- lets the user disable automatic Windows service startup from the setup completion page;
- registers LAN PC Monitor in Windows Installed Apps/Add or Remove Programs; and
- prevents an older MSI from replacing a newer installed version.

Installation requires administrator approval because it writes to Program Files, registers a service, and changes Windows Firewall.

The installer does not enable router forwarding, UPnP, public-network access, an update service, or any outbound network rule.

## Preserved data

`appsettings.json` is marked permanent and never-overwrite so local configuration survives repair, upgrade, and uninstall. Runtime history lives separately under `%ProgramData%\LanPcMonitor` and is not owned or removed by the MSI.

Installed files are grouped under `Service` and `Tray` subdirectories so each self-contained application keeps its own runtime dependencies. The shared `appsettings.json` remains at the installation root for compatibility with existing installations.

This conservative behavior prevents upgrades or accidental uninstallations from deleting monitoring history. A future installer UI may offer an explicit, separately confirmed “remove all local data” action.

## Build an MSI

From the repository root:

```powershell
.\scripts\build-installer.ps1 -Version 0.1.1
```

The script:

1. publishes the service and tray as self-contained `win-x64` applications;
2. builds the versioned MSI;
3. validates the MSI through the WiX build; and
4. generates a lowercase SHA-256 checksum file.

Outputs:

```text
artifacts\installer\LanPcMonitor-0.1.1-win-x64.msi
artifacts\installer\LanPcMonitor-0.1.1-win-x64.msi.sha256
```

MSI versions must use three numeric parts. Build each public version from a clean tagged commit.

## Manual verification

Installer behavior should be tested in Windows Sandbox or a disposable Windows VM before every release:

1. Install version N and confirm UAC identifies the expected publisher.
2. Confirm the `PCMonitor` service is automatic and running.
3. Confirm `/status` responds locally.
4. Confirm the firewall rule is All profiles + LocalSubnet + TCP 5005.
5. Launch the tray shortcut and exercise service controls.
6. Modify `appsettings.json` and create history.
7. Install version N+1 and verify configuration/history survive.
8. Attempt to install N again and verify downgrade blocking.
9. Uninstall N+1 and confirm the service, firewall rule, binaries, and shortcut are removed.
10. Confirm configuration and `%ProgramData%` history remain.

Do not test installation on a production PC until the package is signed and the behavior has passed a disposable-machine test.

## Update model

Updates are full MSI major upgrades. Every version gets a new MSI product identity while retaining the stable `UpgradeCode` in `Package.wxs`. Do not change that upgrade code after the first public release.

The monitoring service never checks the internet. The initial update experience is intentionally explicit:

1. a user visits the GitHub Releases page;
2. downloads the newer signed MSI;
3. verifies its signature/checksum if desired; and
4. runs it with normal UAC confirmation.

A future tray command may perform a manual or opt-in GitHub Releases check. It should never run in the service, silently install an update, or bypass signature verification.

## Signing

Public releases should Authenticode-sign the service executable, tray executable, and final MSI with SHA-256 and an RFC 3161 timestamp. Keep signing credentials outside the repository—preferably in protected CI secrets or hardware-backed key storage.

The signing certificate's verified subject must identify `Schonenberg Developments` for Windows UAC to display that publisher name. The MSI manufacturer and .NET application metadata use the same identity, but metadata alone does not establish a verified publisher.

Unsigned development MSIs will show an unknown-publisher warning. A checksum detects accidental corruption but does not establish publisher identity; it is not a substitute for Authenticode.

## Current installer limitations

- x64 Windows only;
- fixed firewall port `5005` during MSI installation;
- no data-removal checkbox yet;
- no signing certificate configured; and
- no in-product update checker yet.
