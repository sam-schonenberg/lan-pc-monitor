from dataclasses import dataclass
from typing import Protocol

import firebase_admin
from firebase_admin import credentials, exceptions, messaging

from .schemas import EventType, NotificationRequest, Severity


class InvalidFcmTokenError(Exception):
    pass


class NotificationSender(Protocol):
    def send(self, token: str, request: NotificationRequest) -> None: ...


@dataclass(frozen=True)
class NotificationText:
    title: str
    body: str
    severity: Severity


def notification_text(request: NotificationRequest) -> NotificationText:
    severity = Severity.WARNING if request.event_type.value.endswith("-warning") else Severity.CRITICAL
    descriptions = {
        EventType.TEMPERATURE_WARNING: "temperature warning",
        EventType.TEMPERATURE_CRITICAL: "temperature alert",
        EventType.MEMORY_WARNING: "memory pressure warning",
        EventType.MEMORY_CRITICAL: "memory pressure alert",
        EventType.FAN_WARNING: "fan warning",
        EventType.FAN_CRITICAL: "fan alert",
        EventType.UTILIZATION_WARNING: "utilization warning",
        EventType.UTILIZATION_CRITICAL: "utilization alert",
    }
    value = f"{request.value:g}{request.unit}" if request.unit else f"{request.value:g}"
    return NotificationText(
        title=f"{severity.value.title()}: {request.sensor}",
        body=f"{request.sensor} reported a {descriptions[request.event_type]} at {value}.",
        severity=severity,
    )


class FirebaseNotificationSender:
    def __init__(self, credentials_path: str) -> None:
        try:
            self._app = firebase_admin.get_app("lan-pc-monitor-relay")
        except ValueError:
            self._app = firebase_admin.initialize_app(
                credentials.Certificate(credentials_path), name="lan-pc-monitor-relay"
            )

    def send(self, token: str, request: NotificationRequest) -> None:
        text = notification_text(request)
        message = messaging.Message(
            token=token,
            notification=messaging.Notification(title=text.title, body=text.body),
            data={
                "type": "sensorAlert",
                "severity": text.severity.value,
                "eventType": request.event_type.value,
                "sensor": request.sensor,
                "value": f"{request.value:g}",
                "unit": request.unit,
            },
            android=messaging.AndroidConfig(priority="high"),
        )
        try:
            messaging.send(message, app=self._app)
        except (messaging.UnregisteredError, messaging.SenderIdMismatchError) as exception:
            raise InvalidFcmTokenError from exception
        except exceptions.FirebaseError:
            raise
