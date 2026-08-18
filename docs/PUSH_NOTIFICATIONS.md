# Push notification setup

[Back to project overview](../README.md) · [API reference](API.md) · [Security](SECURITY.md)

The notification code is optional at build and runtime. Android, iOS, and the Windows service must use the same
Firebase project.

## Firebase project

1. Create a Firebase project and enable Cloud Messaging.
2. Add an Android app with package name `dev.schonenberg.lanpcmonitor` and download `google-services.json` to
   `PCMonitor.Application/Platforms/Android/google-services.json`.
3. Add an iOS app with bundle ID `dev.schonenberg.lanpcmonitor` and download `GoogleService-Info.plist` to
   `PCMonitor.Application/Platforms/iOS/GoogleService-Info.plist`.
4. In Apple Developer, enable Push Notifications for the App ID and provisioning profiles. Create an APNs
   authentication key and upload it in Firebase Project Settings > Cloud Messaging.
5. Generate a Firebase service-account key and keep that JSON file on the monitored PC. Do not add it to this
   repository or bundle it into the app.

The two client configuration files are ignored by Git. When a platform file is absent, that platform still builds,
but Settings reports that Firebase is not configured.

## Windows service

Configure the service using an absolute service-account path:

```json
"Notifications": {
  "Enabled": true,
  "MinimumSeverity": "Critical",
  "FirebaseProjectId": "your-firebase-project-id",
  "FirebaseServiceAccountFile": "C:\\ProgramData\\LanPcMonitor\\secrets\\firebase-service-account.json"
}
```

Restrict access to the service-account file to administrators and the Windows service identity. Restart the service,
then verify that `GET /api/notifications/status` reports both `enabled` and `configured` as `true`.

## Device enrollment

Build and install the app on a physical device. In Settings, select **Enable notifications** and approve the operating
system prompt. The app registers a stable installation ID and its current FCM token with the configured PC. It
refreshes the registration whenever the app becomes active, which handles FCM token rotation and service restarts.

Disabling notifications unregisters that installation. Changing PC also attempts to unregister it before removing
the old endpoint.

Android 13 and later require runtime notification permission. iOS requires a push-enabled provisioning profile and
must be tested on a physical device. Debug iOS builds use the APNs development environment; release builds use the
production environment.
