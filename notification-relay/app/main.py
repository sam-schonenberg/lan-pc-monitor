from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI

from .config import Settings, get_settings
from .database import Base, create_database_engine, create_session_factory, session_dependency
from .dependencies import get_session
from .firebase import FirebaseNotificationSender, NotificationSender
from .rate_limit import InMemoryRateLimiter
from .routes import delete_expired, router
from .security import SecretHasher, TokenCipher


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

    return app
