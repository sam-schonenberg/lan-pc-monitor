# Release checklist

[Back to project overview](../README.md) · [Installer and updates](INSTALLER.md)

This checklist keeps the Windows installer, Android bundle, source tag, and GitHub release on the same version. Public releases use semantic tags such as `v0.1.1`.

## 1. Prepare the source

- Confirm `VersionPrefix`, `AssemblyVersion`, and `FileVersion` in `Directory.Build.props`.
- Confirm `ApplicationDisplayVersion` and the monotonically increasing `ApplicationVersion` in `PCMonitor.Application/PCMonitor.Application.csproj`.
- Update the README download filename and release notes when the public version changes.
- Review `git status` carefully and remove accidental build output or private files.
- Never commit keystores, password files, certificates, or `AndroidSigning.Local.props`.

## 2. Verify the code

```powershell
dotnet test .\PCMonitor.Service.Tests\PCMonitor.Service.Tests.csproj --configuration Release
dotnet test .\PCMonitor.Application.Tests\PCMonitor.Application.Tests.csproj `
  --configuration Release -f net10.0-windows10.0.19041.0
dotnet build .\PCMonitor.Application\PCMonitor.Application.csproj `
  --configuration Release -f net10.0-android
```

Test the Android app on a physical device. Verify setup, reconnect, dashboard editing, every sensor picker, history synchronization, offline history, alerts, and dark/light themes.

## 3. Build and test the Windows installer

```powershell
.\scripts\build-installer.ps1 -Version 0.1.1
```

Use Windows Sandbox or a disposable VM to verify fresh install, service startup, tray controls, the setup page, firewall scope, upgrade behavior, and uninstall behavior. Follow the detailed checklist in [Installer and updates](INSTALLER.md).

Public Windows packages should be Authenticode-signed before upload. Until signing is configured, clearly disclose the Windows unknown-publisher warning in the release notes.

## 4. Build the Play bundle

The Play upload keystore and password must remain outside the repository. The local `PCMonitor.Application/AndroidSigning.Local.props` file may hold paths and the key alias; it is ignored by Git.

Build an Android App Bundle in Release configuration, using the existing Play upload key. Verify:

- package name `dev.schonenberg.lanpcmonitor`;
- version name matches the release;
- version code is higher than every bundle previously uploaded to Play Console; and
- the upload-certificate SHA-256 fingerprint matches the previous release.

Upload the `.aab` to the intended Play testing or production track and complete Play's review flow.

## 5. Create the Git tag and GitHub release

Only tag the exact commit that passed verification:

```powershell
git tag -a v0.1.1 -m "LAN PC Monitor v0.1.1"
git push origin main
git push origin v0.1.1
```

On GitHub, choose **Releases → Draft a new release**, select `v0.1.1`, use `LAN PC Monitor v0.1.1` as the title, and paste the prepared release notes.

Upload these assets:

- `LanPcMonitor-0.1.1-win-x64.msi`
- `LanPcMonitor-0.1.1-win-x64.msi.sha256`

Do not upload the Play `.aab` to GitHub unless there is a deliberate reason to distribute the upload artifact publicly. Google Play is the distribution channel for the Android app.

Publish as a pre-release when testing is incomplete. Otherwise publish it as the latest release, then test the README's **Latest release** link in a signed-out browser.

## 6. Post-release checks

- Download the MSI back from GitHub and verify its SHA-256 checksum.
- Confirm the GitHub release is marked Latest and the README badge resolves.
- Confirm the Play release uses the expected version code and rollout track.
- Install or upgrade both products from their real distribution channels.
- Keep an offline backup of the Play upload key and its password in a secure location.
