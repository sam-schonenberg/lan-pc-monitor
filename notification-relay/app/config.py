from functools import lru_cache

from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="LPM_RELAY_", env_file=".env", extra="ignore")

    database_url: str = "sqlite:///./relay.db"
    token_encryption_key: SecretStr
    secret_hash_key: SecretStr
    firebase_credentials: str | None = None
    registration_ttl_days: int = Field(default=90, ge=1, le=365)
    max_requests_per_minute: int = Field(default=30, ge=1, le=10_000)


@lru_cache
def get_settings() -> Settings:
    return Settings()  # type: ignore[call-arg]
