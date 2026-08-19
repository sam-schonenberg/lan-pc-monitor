# LAN PC Monitor notification relay

Privacy-focused FastAPI relay for delivering structured LAN PC Monitor hardware alerts through Firebase Cloud
Messaging. It deliberately has no accounts, analytics, PC control, arbitrary-message endpoint, or notification-history
table.

## Data model

The relay stores one row per app installation: a random UUID, encrypted FCM token, HMAC hashes of independent send
and deletion capabilities, platform, minimum severity, and lifecycle timestamps. It does not store names, email
addresses, PC names, hardware inventory, alert history, or IP addresses. Uvicorn access logging is disabled in the
container command.

Notification payloads pass through process memory only. Firebase still receives the destination token and payload.
Reverse proxies and hosting providers must be configured consistently with this privacy model.

## API

| Method | Route | Authentication |
|---|---|---|
| `POST` | `/v1/installations` | Rate-limited public registration |
| `PUT` | `/v1/installations/{id}/token` | Deletion/management capability |
| `DELETE` | `/v1/installations/{id}` | Deletion/management capability |
| `POST` | `/v1/notifications` | Send capability |
| `GET` | `/health` | None |

Capabilities use `Authorization: Bearer <secret>`. They are returned only at registration and cannot be recovered
from the database. Invalid authorization deliberately returns the same `404` as an unknown installation.

The notification endpoint accepts only known event types and bounded sensor/value fields. It does not accept an
arbitrary title or message body.

## Local setup

Python 3.12 or newer is required.

```powershell
cd notification-relay
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -e ".[test]"
```

Generate independent secrets:

```powershell
python -c "from cryptography.fernet import Fernet; print(Fernet.generate_key().decode())"
python -c "import secrets; print(secrets.token_urlsafe(48))"
```

Copy `.env.example` to `.env`, replace both placeholder secrets, set the Firebase credential path, then run:

```powershell
uvicorn app.main:create_app --factory --reload
```

Do not reuse or rotate the token-encryption key without a migration: losing it makes stored FCM tokens unreadable.
Rotating the HMAC key revokes all existing capabilities.

## Tests

Tests use a fake Firebase sender and never contact Google:

```powershell
pytest --cov=app --cov-report=term-missing
```

## Deployment

`compose.example.yml` demonstrates a single relay instance behind Caddy with automatic HTTPS. Before use:

1. Point a DNS name at the vServer and replace `notify.example.com` through `RELAY_DOMAIN`.
2. Copy `compose.example.yml` to `compose.yml` and `.env.example` to `.env`.
3. Store the Firebase JSON at `secrets/firebase-service-account.json` with restrictive host permissions.
4. Generate unique encryption/HMAC keys and place them only in `.env` or a secrets manager.
5. Restrict SSH, enable unattended security updates, and expose only ports 80/443 publicly.
6. Add proxy-level global rate limiting before operating more than one relay process.

The example trusts forwarded client addresses because the relay container has no published host port and is reachable
only through Caddy on its private Docker network. Do not expose port `8000` directly while accepting arbitrary
forwarding headers.

The built-in limiter is intentionally small and retains addresses only in process memory for its 60-second window.
It is not a distributed abuse-control system. A production deployment should also set request, connection, and
delivery quotas at the reverse proxy or firewall.

Registrations expire after 90 days by default. Expired or Firebase-rejected tokens are deleted. Disabling
notifications in the app should call the deletion endpoint immediately.

`GET /delete-data` serves the public, human-readable deletion instructions and a capability-authenticated deletion
form suitable for linking from a store listing. Keep this page publicly reachable over HTTPS.

This first schema is created automatically. Add an explicit migration tool before changing a deployed database
schema; `create_all` is not a production migration strategy.
