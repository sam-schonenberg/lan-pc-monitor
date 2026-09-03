# LAN PC Monitor 0.2.0

Version 0.2.0 introduces local Windows diagnostic collection and improves history export and management.

## Highlights

- Added compact, local collection of selected Critical and Error events from the Windows System log.
- Added configurable 20-minute scans, restart-safe checkpoints, deduplication, retention, and a hard storage limit.
- Added paged and filterable Windows diagnostics API endpoints.
- Added a dependency-free Windows diagnostics web interface at `/diagnostics`.
- Added **Open Windows Diagnostics** to the tray companion.
- Added history export improvements to the mobile application.
- Replaced cryptic installer progress placeholders with clear descriptions of the current update phase.

Windows diagnostic details remain on the monitored PC and are not sent to the notification relay. The first
implementation does not send diagnostic push notifications and does not retain Warning or Information events.

## Upgrade notes

- **Windows:** install `LanPcMonitor-0.2.0-win-x64.msi` over version 0.1.7. The installer preserves existing
  configuration, sensor history, alert settings, and other runtime data.
- **Android:** version code 8 is used for this release.

The Windows diagnostic collector uses built-in defaults when upgrading an installation whose preserved
`appsettings.json` does not yet contain a `WindowsDiagnostics` section.
