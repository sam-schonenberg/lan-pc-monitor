from datetime import datetime
from enum import StrEnum
import unicodedata

from pydantic import BaseModel, ConfigDict, Field, field_validator


class Severity(StrEnum):
    WARNING = "warning"
    CRITICAL = "critical"


class EventType(StrEnum):
    TEMPERATURE_WARNING = "temperature-warning"
    TEMPERATURE_CRITICAL = "temperature-critical"
    MEMORY_WARNING = "memory-warning"
    MEMORY_CRITICAL = "memory-critical"
    FAN_WARNING = "fan-warning"
    FAN_CRITICAL = "fan-critical"
    UTILIZATION_WARNING = "utilization-warning"
    UTILIZATION_CRITICAL = "utilization-critical"


class InstallationCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")
    fcm_token: str = Field(min_length=20, max_length=4096)
    platform: str = Field(default="android", pattern="^android$")
    minimum_severity: Severity = Severity.CRITICAL


class InstallationCreated(BaseModel):
    installation_id: str
    send_secret: str
    delete_secret: str
    expires_at: datetime


class TokenUpdate(BaseModel):
    model_config = ConfigDict(extra="forbid")
    fcm_token: str = Field(min_length=20, max_length=4096)


class NotificationRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    installation_id: str = Field(min_length=36, max_length=36)
    event_type: EventType
    sensor: str = Field(min_length=1, max_length=80)
    value: float = Field(ge=-1000, le=100_000)
    unit: str = Field(max_length=12)

    @field_validator("sensor", "unit")
    @classmethod
    def reject_control_characters(cls, value: str) -> str:
        if any(unicodedata.category(character).startswith("C") for character in value):
            raise ValueError("control characters are not allowed")
        return value


class NotificationAccepted(BaseModel):
    accepted: bool = True
