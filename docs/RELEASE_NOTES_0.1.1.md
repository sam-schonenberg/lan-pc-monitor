# LAN PC Monitor v0.1.1

This release makes hardware monitoring easier to understand and improves the first-run experience across Windows and Android.

## Highlights

- Added readable, hardware-independent sensor names for CPU, GPU, memory, fan, power, voltage, clock, and temperature readings.
- Redesigned sensor pickers with concise measurement labels such as `GPU Core · Temperature` and `GPU Fan · Speed`.
- Improved sensor presentation in History, Settings, and dashboard widget configuration.
- Compacted the Dashboard header so PC status and controls use less space.
- Added refreshed LAN PC Monitor branding to the app, tray companion, and Windows installer.
- Improved Windows installer behavior, service controls, firewall management, upgrade safety, and setup guidance.

## Downloads

- **Windows:** download `LanPcMonitor-0.1.1-win-x64.msi` from the Assets section below.
- **Android:** install or update through the configured Google Play testing or production track.

> [!IMPORTANT]
> The Windows package is not yet Authenticode-signed, so Windows may display an unknown-publisher warning. The LAN API is also currently unencrypted and unauthenticated; use it only on a trusted local network and never expose port `5005` to the internet.

Existing Windows configuration and monitoring history are preserved during an MSI upgrade.
