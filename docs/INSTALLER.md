# Installer and updates

[Back to project overview](../README.md) · [Security](SECURITY.md)

LAN PC Monitor ships its Windows service and tray companion as one self-contained x64 MSI. The installer is authored with WiX Toolset 6.0.1 and does not require WiX or the .NET runtime on the destination PC.

## Installer behavior

The MSI:

- installs under `%ProgramFiles%\LAN PC Monitor` by default;
- registers `PCMonitor` as an automatically started Windows service;
- installs the official signed PawnIO driver dependency before starting the service, enabling supported CPU and motherboard temperatures, fan speeds, voltages, and other low-level sensors;
- stops the service safely during upgrades and uninstallation;
- configures three restart-on-failure attempts with a five-second delay;
- creates the `LAN PC Monitor - API` inbound firewall rule for TCP port `5005`;
- restricts that rule to the local subnet while allowing all Windows network profiles, including Public;
- adds a Start Menu shortcut for the optional tray companion;
- starts the tray companion automatically whenever any user signs in, using the machine-wide Windows Run entry;
- asks every running tray companion to exit during installation and upgrades, waits up to fifteen seconds, then
  terminates any remaining instance so locked files do not block setup;
- immediately before an update starts, checks for tray processes once per second, force-closes every instance it
  finds, and refuses to replace files if an instance remains after fifteen seconds;
- prevents duplicate tray companion instances within the same Windows session;
- provides one context-sensitive tray command to enable or disable the service and its automatic startup;
- launches the MSI-managed Windows uninstall flow from the tray companion;
- uses LAN PC Monitor branding throughout setup and in Windows Installed Apps;
- opens the local pairing page in the default browser when setup finishes;
- lets the user disable automatic Windows service startup from the setup completion page;
- registers LAN PC Monitor in Windows Installed Apps/Add or Remove Programs;
- prevents an older MSI from replacing a newer installed version;
- detects an earlier installation and presents a dedicated updater confirmation with an **Update** action;
- retains the existing installation directory and skips fresh-install choices during an update;
- starts the updated tray companion in the signed-in user's session after a successful update;
- confirms during an update that existing configuration and monitoring history are preserved.

Installation requires administrator approval because it writes to Program Files, registers a service, and changes Windows Firewall.

PawnIO is redistributed unmodified under its own GPL-2.0-or-later license. Its setup program, license, checksum, and source links are installed under `ThirdParty\PawnIO`. PawnIO is a shared system driver and remains installed when LAN PC Monitor is removed so uninstalling this application does not break other hardware-monitoring software.

The installer does not enable router forwarding, UPnP, public-network access, an update service, or any outbound network rule.

## Preserved data

`appsettings.json` is marked permanent and never-overwrite so local configuration survives repair, upgrade, and uninstall. Runtime history lives separately under `%ProgramData%\LanPcMonitor` and is not owned or removed by the MSI.

History cleanup preserves the configured retention period and minute-level readings. Routine cleanup runs at most once
per day after load-session detection has reported an idle PC continuously for five minutes. If the history file reaches
128 MB, cleanup may run sooner, with at least one hour between size-triggered passes. Cleanup removes expired and
duplicate records through a temporary file and replaces the live file only after the rewrite succeeds.

Installed files are grouped under `Service` and `Tray` subdirectories so each self-contained application keeps its own runtime dependencies. The shared `appsettings.json` remains at the installation root for compatibility with existing installations.

This conservative behavior prevents upgrades or accidental uninstallations from deleting monitoring history. A future installer UI may offer an explicit, separately confirmed “remove all local data” action.

## Build an MSI

From the repository root:

```powershell
.\scripts\build-installer.ps1 -Version 0.1.7
```

The script:

1. publishes the service and tray as self-contained `win-x64` applications;
2. builds the versioned MSI;
3. validates the MSI through the WiX build; and
4. generates a lowercase SHA-256 checksum file.

Outputs:

```text
artifacts\installer\LanPcMonitor-0.1.7-win-x64.msi
artifacts\installer\LanPcMonitor-0.1.7-win-x64.msi.sha256
```

MSI versions must use three numeric parts. Build each public version from a clean tagged commit.

## Manual verification

Installer behavior should be tested in Windows Sandbox or a disposable Windows VM before every release:

1. Install version N and confirm UAC identifies the expected publisher.
2. Confirm PawnIO is installed and running before the `PCMonitor` service starts.
3. Confirm the `PCMonitor` service is automatic and running.
4. Confirm `/status` responds locally and `/api/v1/sensors` contains supported CPU temperatures and motherboard sensors.
5. Confirm the firewall rule is All profiles + LocalSubnet + TCP 5005.
6. Launch the tray shortcut and exercise service controls.
7. Modify `appsettings.json` and create history.
8. Install version N+1 and verify configuration/history survive.
9. Leave the tray running while updating and confirm the updater identifies itself as an update, offers an **Update**
   action, closes the tray, stops the service, and does not show the installation-directory or initial-setup choices.
10. Confirm the completion page says the update succeeded and both the service and tray use version N+1.
11. Attempt to install N again and verify downgrade blocking.
12. Uninstall N+1 and confirm the service, firewall rule, binaries, and shortcut are removed.
13. Confirm configuration, `%ProgramData%` history, and the shared PawnIO driver remain.

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
