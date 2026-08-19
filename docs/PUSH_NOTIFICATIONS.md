# Push notification setup

[Back to project overview](../README.md) · [API reference](API.md) · [Security](SECURITY.md)

The mobile app uses Firebase Cloud Messaging, while the Windows service sends structured alerts through the hosted
notification relay. The Firebase Admin credential exists only on the relay server.

The hosted relay at `https://138-201-94-167.sslip.io/` exists only to deliver opt-in notifications when the app is in
the background. Live monitoring, history synchronization, dashboards, pairing, and service control do not pass through
it. The relay cannot initiate a connection to the PC. For each alert, it receives only the destination capability,
event type, severity, sensor name, measured value, threshold, and unit; it does not receive the PC's sensor history.
Alert payloads are forwarded to Firebase and are not stored by the relay.

## Firebase project

1. Create a Firebase project and enable Cloud Messaging.
2. Add an Android app with package name `dev.schonenberg.lanpcmonitor` and download `google-services.json` to
   `PCMonitor.Application/Platforms/Android/google-services.json`.
3. Add an iOS app with bundle ID `dev.schonenberg.lanpcmonitor` and download `GoogleService-Info.plist` to
   `PCMonitor.Application/Platforms/iOS/GoogleService-Info.plist`.
4. In Apple Developer, enable Push Notifications for the App ID and provisioning profiles. Create an APNs
   authentication key and upload it in Firebase Project Settings > Cloud Messaging.
5. Store the Firebase service-account key only on the notification relay server. Do not copy it to a monitored PC or
   bundle it into the app.

The two client configuration files are ignored by Git. When a platform file is absent, that platform still builds,
but Settings reports that Firebase is not configured.

## Windows service

Configure the service with the HTTPS notification relay URL:

```json
"Notifications": {
  "Enabled": true,
  "MinimumSeverity": "Critical",
  "MinimumIntervalSeconds": 60,
  "RelayBaseUrl": "https://138-201-94-167.sslip.io/"
}
```

Restart the service, then verify that `GET /api/v1/notifications/status` reports both `enabled` and `configured` as
`true`.

## Device enrollment

Build and install the app on a physical device. In Settings, select **Enable notifications** and approve the operating
system prompt. The app registers its FCM token with the relay, stores the resulting capability secrets using secure
storage, and gives the configured PC only the installation ID and send capability. It refreshes the relay token and PC
registration whenever the app becomes active.

Disabling notifications unregisters that installation. Changing PC also attempts to unregister it before removing
the old endpoint.

Push delivery therefore requires outbound HTTPS access from the Windows PC to the configured relay and from the phone
to Firebase. Monitoring and local app access continue to work if either internet service is unavailable.

Android 13 and later require runtime notification permission. iOS requires a push-enabled provisioning profile and
must be tested on a physical device. Debug iOS builds use the APNs development environment; release builds use the
production environment.
