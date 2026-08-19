from pathlib import Path

from cryptography.fernet import Fernet
from fastapi.testclient import TestClient
from pydantic import SecretStr
from sqlalchemy import select

from app.config import Settings
from app.database import create_database_engine, create_session_factory
from app.firebase import InvalidFcmTokenError
from app.main import create_app
from app.models import Installation


class FakeSender:
    def __init__(self) -> None:
        self.sent: list[tuple[str, object]] = []
        self.reject_tokens: set[str] = set()

    def send(self, token: str, request: object) -> None:
        if token in self.reject_tokens:
            raise InvalidFcmTokenError
        self.sent.append((token, request))


def settings(tmp_path: Path) -> Settings:
    return Settings(
        database_url=f"sqlite:///{tmp_path / 'relay.db'}",
        token_encryption_key=SecretStr(Fernet.generate_key().decode()),
        secret_hash_key=SecretStr("test-secret-hash-key-that-is-at-least-32-bytes"),
        registration_ttl_days=90,
        max_requests_per_minute=100,
    )


def register(client: TestClient, token: str = "fcm-token-that-is-long-enough-for-validation") -> dict:
    response = client.post("/v1/installations", json={"fcm_token": token})
    assert response.status_code == 201
    return response.json()


def notification(installation_id: str, event_type: str = "temperature-critical") -> dict:
    return {
        "installation_id": installation_id,
        "event_type": event_type,
        "sensor": "GPU Core Temperature",
        "value": 96,
        "unit": "°C",
    }


def test_registration_encrypts_token_and_notification_delivery(tmp_path: Path) -> None:
    configured = settings(tmp_path)
    sender = FakeSender()
    with TestClient(create_app(configured, sender)) as client:
        created = register(client)
        response = client.post(
            "/v1/notifications",
            json=notification(created["installation_id"]),
            headers={"Authorization": f"Bearer {created['send_secret']}"},
        )
        assert response.status_code == 202
        assert sender.sent[0][0] == "fcm-token-that-is-long-enough-for-validation"

    engine = create_database_engine(configured.database_url)
    with create_session_factory(engine)() as session:
        installation = session.scalar(select(Installation))
        assert installation is not None
        assert b"fcm-token" not in installation.fcm_token_ciphertext
        assert created["send_secret"] not in installation.send_secret_hash
    engine.dispose()


def test_wrong_capability_does_not_reveal_installation(tmp_path: Path) -> None:
    with TestClient(create_app(settings(tmp_path), FakeSender())) as client:
        created = register(client)
        response = client.post(
            "/v1/notifications",
            json=notification(created["installation_id"]),
            headers={"Authorization": "Bearer incorrect-secret"},
        )
        assert response.status_code == 404


def test_token_update_and_deletion_require_management_capability(tmp_path: Path) -> None:
    sender = FakeSender()
    with TestClient(create_app(settings(tmp_path), sender)) as client:
        created = register(client)
        auth = {"Authorization": f"Bearer {created['delete_secret']}"}
        response = client.put(
            f"/v1/installations/{created['installation_id']}/token",
            json={"fcm_token": "replacement-fcm-token-that-is-long-enough"},
            headers=auth,
        )
        assert response.status_code == 204
        response = client.delete(f"/v1/installations/{created['installation_id']}", headers=auth)
        assert response.status_code == 204
        response = client.post(
            "/v1/notifications",
            json=notification(created["installation_id"]),
            headers={"Authorization": f"Bearer {created['send_secret']}"},
        )
        assert response.status_code == 404


def test_critical_preference_suppresses_warning(tmp_path: Path) -> None:
    sender = FakeSender()
    with TestClient(create_app(settings(tmp_path), sender)) as client:
        created = register(client)
        response = client.post(
            "/v1/notifications",
            json=notification(created["installation_id"], "temperature-warning"),
            headers={"Authorization": f"Bearer {created['send_secret']}"},
        )
        assert response.status_code == 202
        assert sender.sent == []


def test_utilization_alert_is_supported(tmp_path: Path) -> None:
    sender = FakeSender()
    with TestClient(create_app(settings(tmp_path), sender)) as client:
        created = register(client)
        response = client.post(
            "/v1/notifications",
            json=notification(created["installation_id"], "utilization-critical"),
            headers={"Authorization": f"Bearer {created['send_secret']}"},
        )
        assert response.status_code == 202
        assert len(sender.sent) == 1


def test_invalid_fcm_token_revokes_installation(tmp_path: Path) -> None:
    token = "expired-fcm-token-that-is-long-enough"
    sender = FakeSender()
    sender.reject_tokens.add(token)
    with TestClient(create_app(settings(tmp_path), sender)) as client:
        created = register(client, token)
        auth = {"Authorization": f"Bearer {created['send_secret']}"}
        assert client.post("/v1/notifications", json=notification(created["installation_id"]), headers=auth).status_code == 410
        assert client.post("/v1/notifications", json=notification(created["installation_id"]), headers=auth).status_code == 404


def test_health_does_not_require_authentication(tmp_path: Path) -> None:
    with TestClient(create_app(settings(tmp_path), FakeSender())) as client:
        assert client.get("/health").json() == {"status": "ok"}


def test_notification_rejects_control_characters(tmp_path: Path) -> None:
    with TestClient(create_app(settings(tmp_path), FakeSender())) as client:
        created = register(client)
        payload = notification(created["installation_id"])
        payload["sensor"] = "GPU\nForged title"
        response = client.post(
            "/v1/notifications",
            json=payload,
            headers={"Authorization": f"Bearer {created['send_secret']}"},
        )
        assert response.status_code == 422
