# Security

[Back to project overview](../README.md) · [API reference](API.md)

LAN PC Monitor is intentionally self-hosted and LAN-only. Its design reduces exposure, but the current version does not authenticate clients or encrypt traffic. This document describes what is enforced, what is not, and how to deploy it safely.

## Runtime network behavior

The Windows monitoring service:

- listens for inbound HTTP and WebSocket connections on the configured port (`5005` by default);
- does not require an account, cloud backend, remote relay, analytics service, or telemetry endpoint for monitoring;
- initiates outbound HTTPS connections to the configured notification relay only when optional push notifications
  are enabled and a phone is registered;
- does not configure a router, UPnP, NAT traversal, public DNS, or port forwarding; and
- exposes read-only monitoring routes plus relay-capability registration routes—there are no public API routes for
  executing commands or changing configuration.

Development operations such as restoring NuGet packages, downloading the .NET SDK, and opening documentation links can use the internet. Those are build/user actions, not monitoring-service runtime traffic.

## Enforced controls

### Windows Firewall scope

The supplied installation script creates one inbound rule:

| Property | Value |
|---|---|
| Direction | Inbound |
| Action | Allow |
| Protocol | TCP |
| Local port | Configured service port |
| Windows profile | All (Private, Public, and Domain) |
| Remote addresses | `LocalSubnet` only |

The rule is defined in [`scripts/install-firewall-rule.ps1`](../scripts/install-firewall-rule.ps1). It permits a phone on the same local subnet even when Windows classifies the PC network as Public. Administrative privileges are required to create or remove it.

### Client endpoint validation

The MAUI client accepts plain HTTP endpoints only when the host is:

- loopback/`localhost`;
- private IPv4 in `10.0.0.0/8`, `172.16.0.0/12`, or `192.168.0.0/16`;
- a `.local` hostname; or
- an unqualified LAN hostname.

Public IP literals, normal public DNS names, HTTPS URLs, and other URI schemes are rejected. The client tests `/status` before saving an endpoint. This prevents accidental configuration of an ordinary public host; it is not server authentication, and local DNS can still influence hostname resolution.

### Data minimization and storage

- Sensor readings and history are stored locally on the monitored PC.
- Synchronized history and settings are stored in the client's local SQLite database.
- Process sampling is active only during candidate/active load sessions by default.
- Retained process data contains a normalized process name and aggregate CPU statistics—not paths, arguments, environment variables, window titles, documents, memory contents, or network activity.
- History and alert retention are bounded by configuration.
- The service has no endpoint that uploads retained data elsewhere.
- When push notifications are enabled, the service sends a destination ID, event type, sensor name, value, and unit
  to the notification relay. The relay passes the notification to Firebase Cloud Messaging. The Firebase
  service-account private key stays only on the relay server.

## Known security limitations

The current API uses `http://` and `ws://` with:

- no TLS encryption;
- no client or server authentication;
- no pairing or per-device authorization;
- no request rate limiting; and
- no origin-based WebSocket access control.

Consequently, any device that can reach the API can read its monitoring data or register/unregister notification
destinations, a hostile LAN device may observe or alter unencrypted traffic, and the client cannot cryptographically
prove that it reached the intended PC. The firewall rule is a network boundary, not an identity boundary.

## Safe deployment checklist

- Prefer trusted Private networks. On a Public network, assume every device on the local subnet is untrusted and may be able to reach the API.
- Do not expose the port through router forwarding, a reverse proxy, a tunnel, VPN publication, or a broad firewall rule.
- Disable the service when using public Wi-Fi if local monitoring access is not required.
- Keep Windows, .NET, LibreHardwareMonitor, and dependencies updated.
- Review changes to `appsettings.json` and the firewall scripts before deployment.
- Remove the firewall rule or stop/disable the service when LAN access is no longer wanted.

For stronger isolation today, place the PC and client on a trusted VLAN and allow only the client's address to reach the port with an administrator-managed firewall rule. Such a custom rule is outside the supplied installer.

## Reporting a vulnerability

Do not publish an unpatched vulnerability, exploit, private network detail, or captured monitoring data in a public issue. Contact the repository owner privately through the security-reporting method configured on GitHub—preferably GitHub Private Vulnerability Reporting.

Include the affected commit/version, reproduction steps, impact, and suggested mitigation. If no private channel is configured, open a minimal issue requesting private contact without disclosing vulnerability details.
