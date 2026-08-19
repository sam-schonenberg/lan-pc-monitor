from datetime import UTC, datetime, timedelta
from typing import Annotated
from uuid import UUID, uuid4

from fastapi import APIRouter, Depends, Header, HTTPException, Request, Response, status
from sqlalchemy import delete
from sqlalchemy.orm import Session

from .dependencies import get_session
from .firebase import InvalidFcmTokenError, NotificationSender, notification_text
from .models import Installation
from .schemas import InstallationCreate, InstallationCreated, NotificationAccepted, NotificationRequest, Severity, TokenUpdate

router = APIRouter(prefix="/v1")


def _utcnow() -> datetime:
    # SQLite does not preserve timezone offsets. All persisted timestamps are naive UTC.
    return datetime.now(UTC).replace(tzinfo=None)


def _bearer(authorization: str | None) -> str:
    scheme, _, value = (authorization or "").partition(" ")
    if scheme.lower() != "bearer" or not value:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "A bearer capability is required")
    return value


def _installation(session: Session, installation_id: str) -> Installation:
    try:
        UUID(installation_id)
    except ValueError as exception:
        raise HTTPException(status.HTTP_404_NOT_FOUND, "Installation not found") from exception
    installation = session.get(Installation, installation_id)
    if installation is None or installation.expires_at <= _utcnow():
        if installation is not None:
            session.delete(installation)
            session.commit()
        raise HTTPException(status.HTTP_404_NOT_FOUND, "Installation not found")
    return installation


@router.post("/installations", response_model=InstallationCreated, status_code=status.HTTP_201_CREATED)
def create_installation(payload: InstallationCreate, request: Request,
                        session: Session = Depends(get_session)) -> InstallationCreated:
    request.app.state.rate_limiter.check(request)
    now = _utcnow()
    installation_id = str(uuid4())
    send_secret = request.app.state.hasher.generate()
    delete_secret = request.app.state.hasher.generate()
    expires_at = now + timedelta(days=request.app.state.settings.registration_ttl_days)
    installation = Installation(
        id=installation_id,
        fcm_token_ciphertext=request.app.state.cipher.encrypt(payload.fcm_token),
        send_secret_hash=request.app.state.hasher.digest(installation_id, send_secret),
        delete_secret_hash=request.app.state.hasher.digest(installation_id, delete_secret),
        platform=payload.platform,
        minimum_severity=payload.minimum_severity.value,
        created_at=now,
        updated_at=now,
        expires_at=expires_at,
    )
    session.add(installation)
    session.commit()
    return InstallationCreated(installation_id=installation_id, send_secret=send_secret,
                               delete_secret=delete_secret, expires_at=expires_at.replace(tzinfo=UTC))


@router.put("/installations/{installation_id}/token", status_code=status.HTTP_204_NO_CONTENT)
def update_token(installation_id: str, payload: TokenUpdate, request: Request,
                 authorization: Annotated[str | None, Header()] = None,
                 session: Session = Depends(get_session)) -> Response:
    request.app.state.rate_limiter.check(request)
    installation = _installation(session, installation_id)
    secret = _bearer(authorization)
    if not request.app.state.hasher.verify(installation.id, secret, installation.delete_secret_hash):
        raise HTTPException(status.HTTP_404_NOT_FOUND, "Installation not found")
    now = _utcnow()
    installation.fcm_token_ciphertext = request.app.state.cipher.encrypt(payload.fcm_token)
    installation.updated_at = now
    installation.expires_at = now + timedelta(days=request.app.state.settings.registration_ttl_days)
    session.commit()
    return Response(status_code=status.HTTP_204_NO_CONTENT)


@router.delete("/installations/{installation_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_installation(installation_id: str, request: Request,
                        authorization: Annotated[str | None, Header()] = None,
                        session: Session = Depends(get_session)) -> Response:
    request.app.state.rate_limiter.check(request)
    installation = _installation(session, installation_id)
    secret = _bearer(authorization)
    if not request.app.state.hasher.verify(installation.id, secret, installation.delete_secret_hash):
        raise HTTPException(status.HTTP_404_NOT_FOUND, "Installation not found")
    session.delete(installation)
    session.commit()
    return Response(status_code=status.HTTP_204_NO_CONTENT)


@router.post("/notifications", response_model=NotificationAccepted, status_code=status.HTTP_202_ACCEPTED)
def send_notification(payload: NotificationRequest, request: Request,
                      authorization: Annotated[str | None, Header()] = None,
                      session: Session = Depends(get_session)) -> NotificationAccepted:
    request.app.state.rate_limiter.check(request)
    installation = _installation(session, payload.installation_id)
    secret = _bearer(authorization)
    if not request.app.state.hasher.verify(installation.id, secret, installation.send_secret_hash):
        raise HTTPException(status.HTTP_404_NOT_FOUND, "Installation not found")
    text = notification_text(payload)
    if installation.minimum_severity == Severity.CRITICAL.value and text.severity != Severity.CRITICAL:
        return NotificationAccepted()
    try:
        request.app.state.sender.send(request.app.state.cipher.decrypt(installation.fcm_token_ciphertext), payload)
    except InvalidFcmTokenError:
        session.delete(installation)
        session.commit()
        raise HTTPException(status.HTTP_410_GONE, "Notification destination expired") from None
    now = _utcnow()
    installation.updated_at = now
    installation.expires_at = now + timedelta(days=request.app.state.settings.registration_ttl_days)
    session.commit()
    return NotificationAccepted()


def delete_expired(session: Session) -> int:
    result = session.execute(delete(Installation).where(Installation.expires_at <= _utcnow()))
    session.commit()
    return result.rowcount or 0
