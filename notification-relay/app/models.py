from datetime import datetime

from sqlalchemy import DateTime, LargeBinary, String
from sqlalchemy.orm import Mapped, mapped_column

from .database import Base


class Installation(Base):
    __tablename__ = "installations"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    fcm_token_ciphertext: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)
    send_secret_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    delete_secret_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    platform: Mapped[str] = mapped_column(String(16), nullable=False)
    minimum_severity: Mapped[str] = mapped_column(String(16), nullable=False, default="critical")
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    expires_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False, index=True)
