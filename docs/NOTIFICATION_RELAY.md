# Notification relay and privacy boundary

[Back to project overview](../README.md) · [Security](SECURITY.md)

The hosted notification relay lets the app receive background alerts without distributing Firebase credentials or
requiring every user to create a Firebase project. It is used only for opt-in notification delivery. Monitoring,
history, dashboards, pairing, and control remain local; the relay has no route into the monitored PC.

```text
Android app ── FCM token registration ──▶ relay
Windows service ── structured alert ────▶ relay ──▶ Firebase Cloud Messaging ──▶ Android app
```

The Android app and relay use the maintainer's Firebase project. The Firebase Admin credential exists only on the
relay host. The Windows service stores a random destination ID and sending capability received during pairing.

## Privacy boundary

Persisted relay data is limited to an encrypted FCM token, hashed capability secrets, platform, minimum severity,
and lifecycle timestamps. Alert content is not persisted. There are no user accounts or analytics. Firebase and
network providers still process notification payloads, tokens, and connection metadata as necessary for delivery.

Notifications must remain opt-in. Product privacy documentation should describe the relay hostname, transmitted
fields, Firebase processing, retention period, deletion behavior, and operator contact before public deployment.

## Protocol status

The FastAPI implementation is under `notification-relay/`. The Android app registers its Firebase token with the
relay, stores relay capability secrets with the operating-system secure-storage API, and passes only the send
capability to the paired PC. The Windows service sends bounded structured alerts through that capability and never
receives an FCM token or Firebase Admin credential.

The public service is currently configured through `https://138-201-94-167.sslip.io/`. Operators of another deployment
must use HTTPS, protect both relay secrets and Firebase credentials, restrict direct access to the application server,
and review the privacy and abuse controls described in `notification-relay/README.md`.

Users can review retention details and request deletion through the public data-deletion page:
`https://138-201-94-167.sslip.io/delete-data`. The page can delete a registration using its installation ID and
deletion capability; disabling notifications in the app performs the same authenticated deletion automatically.

## Using notifications

The hosted release requires no Firebase or relay setup from testers. On a paired Android phone, open **Settings**,
select **Enable notifications**, and approve Android's notification permission when prompted. The app registers the
phone with the relay and gives the paired PC only a scoped send capability. Disabling notifications or changing the
paired PC removes that registration.

Delivery requires outbound HTTPS access from the Windows PC to the relay and from the phone to Firebase. Monitoring,
local alerts, and history continue to work when either internet service is unavailable.

Self-hosting and Firebase credential instructions belong to the operator documentation in
[`notification-relay/README.md`](../notification-relay/README.md); Firebase administrator credentials must never be
placed on a monitored PC or bundled into the app.
