import hashlib
import hmac
import secrets

from cryptography.fernet import Fernet, InvalidToken


class TokenCipher:
    def __init__(self, key: str) -> None:
        try:
            self._fernet = Fernet(key.encode("ascii"))
        except (ValueError, UnicodeEncodeError) as exception:
            raise ValueError("LPM_RELAY_TOKEN_ENCRYPTION_KEY must be a valid Fernet key") from exception

    def encrypt(self, token: str) -> bytes:
        return self._fernet.encrypt(token.encode("utf-8"))

    def decrypt(self, ciphertext: bytes) -> str:
        try:
            return self._fernet.decrypt(ciphertext).decode("utf-8")
        except (InvalidToken, UnicodeDecodeError) as exception:
            raise ValueError("Stored FCM token cannot be decrypted") from exception


class SecretHasher:
    def __init__(self, key: str) -> None:
        encoded = key.encode("utf-8")
        if len(encoded) < 32:
            raise ValueError("LPM_RELAY_SECRET_HASH_KEY must contain at least 32 bytes")
        self._key = encoded

    @staticmethod
    def generate() -> str:
        return secrets.token_urlsafe(32)

    def digest(self, installation_id: str, secret: str) -> str:
        return hmac.new(self._key, f"{installation_id}:{secret}".encode(), hashlib.sha256).hexdigest()

    def verify(self, installation_id: str, secret: str, expected: str) -> bool:
        return hmac.compare_digest(self.digest(installation_id, secret), expected)
