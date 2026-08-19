from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.responses import HTMLResponse

from .config import Settings, get_settings
from .database import Base, create_database_engine, create_session_factory, session_dependency
from .dependencies import get_session
from .firebase import FirebaseNotificationSender, NotificationSender
from .rate_limit import InMemoryRateLimiter
from .routes import delete_expired, router
from .security import SecretHasher, TokenCipher


DELETE_DATA_PAGE = """<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="index,follow">
  <title>Delete notification data — LAN PC Monitor</title>
  <style>
    :root { color-scheme: light dark; font-family: system-ui, sans-serif; }
    body { max-width: 720px; margin: 0 auto; padding: 32px 20px 64px; line-height: 1.55; }
    h1 { line-height: 1.15; } .card { border: 1px solid #8886; border-radius: 12px; padding: 20px; margin: 20px 0; }
    label { display: block; margin-top: 14px; font-weight: 600; }
    input { box-sizing: border-box; width: 100%; padding: 10px; margin-top: 5px; }
    button { margin-top: 18px; padding: 10px 18px; font-weight: 700; }
    #result { min-height: 1.5em; font-weight: 600; } code { overflow-wrap: anywhere; }
  </style>
</head>
<body>
  <main>
    <h1>Delete LAN PC Monitor notification data</h1>
    <p>LAN PC Monitor does not create user accounts. Monitoring history and dashboard data remain on your devices and
       are never stored by this notification server.</p>
    <section class="card">
      <h2>Delete from the app</h2>
      <p>Open LAN PC Monitor, go to <strong>Settings</strong>, and disable notifications. The app immediately requests
         deletion of its notification registration.</p>
    </section>
    <section class="card">
      <h2>Delete using your registration details</h2>
      <p>If you retained the installation ID and deletion secret issued when notifications were enabled, enter them
         below. The request is sent directly to this server and the secret is not placed in the URL.</p>
      <form id="delete-form">
        <label for="installation-id">Installation ID</label>
        <input id="installation-id" autocomplete="off" required pattern="[0-9a-fA-F-]{36}">
        <label for="delete-secret">Deletion secret</label>
        <input id="delete-secret" type="password" autocomplete="off" required>
        <button type="submit">Permanently delete notification data</button>
      </form>
      <p id="result" role="status" aria-live="polite"></p>
    </section>
    <h2>Data removed</h2>
    <p>Deletion removes the encrypted Firebase token, hashed capability secrets, platform, notification preference,
       and registration timestamps. Alert payloads and sensor history are not stored by the relay.</p>
    <h2>Retention</h2>
    <p>Registrations not deleted manually expire automatically after 90 days. No notification registration data is
       retained after deletion or expiration.</p>
  </main>
  <script>
    document.getElementById('delete-form').addEventListener('submit', async (event) => {
      event.preventDefault();
      const result = document.getElementById('result');
      const id = document.getElementById('installation-id').value.trim();
      const secret = document.getElementById('delete-secret').value.trim();
      result.textContent = 'Deleting…';
      try {
        const response = await fetch('/v1/installations/' + encodeURIComponent(id), {
          method: 'DELETE', headers: { 'Authorization': 'Bearer ' + secret }
        });
        if (response.status === 204 || response.status === 404) {
          result.textContent = 'Notification data deleted. No matching registration remains.';
          event.target.reset();
        } else {
          result.textContent = 'Deletion could not be completed. Check the registration details and try again.';
        }
      } catch (_) { result.textContent = 'The server could not be reached. Please try again later.'; }
    });
  </script>
</body>
</html>"""


def create_app(settings: Settings | None = None, sender: NotificationSender | None = None) -> FastAPI:
    configured = settings or get_settings()
    engine = create_database_engine(configured.database_url)
    session_factory = create_session_factory(engine)

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
        Base.metadata.create_all(engine)
        with session_factory() as session:
            delete_expired(session)
        yield
        engine.dispose()

    app = FastAPI(
        title="LAN PC Monitor Notification Relay",
        version="0.1.0",
        description="Account-free, privacy-focused delivery of structured hardware alerts.",
        lifespan=lifespan,
        docs_url=None,
        redoc_url=None,
    )
    app.state.settings = configured
    app.state.cipher = TokenCipher(configured.token_encryption_key.get_secret_value())
    app.state.hasher = SecretHasher(configured.secret_hash_key.get_secret_value())
    app.state.rate_limiter = InMemoryRateLimiter(configured.max_requests_per_minute)
    if sender is None:
        if not configured.firebase_credentials:
            raise ValueError("LPM_RELAY_FIREBASE_CREDENTIALS is required")
        sender = FirebaseNotificationSender(configured.firebase_credentials)
    app.state.sender = sender

    def configured_session():  # type: ignore[no-untyped-def]
        yield from session_dependency(session_factory)

    app.dependency_overrides[get_session] = configured_session
    app.include_router(router)

    @app.get("/health", include_in_schema=False)
    def health() -> dict[str, str]:
        return {"status": "ok"}

    @app.get("/delete-data", response_class=HTMLResponse, include_in_schema=False)
    def delete_data_page() -> HTMLResponse:
        return HTMLResponse(DELETE_DATA_PAGE, headers={
            "Cache-Control": "no-store",
            "Referrer-Policy": "no-referrer",
            "X-Content-Type-Options": "nosniff",
            "Content-Security-Policy": "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'",
        })

    return app
