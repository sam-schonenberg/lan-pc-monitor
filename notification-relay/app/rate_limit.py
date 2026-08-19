import threading
import time
from collections import defaultdict, deque

from fastapi import HTTPException, Request, status


class InMemoryRateLimiter:
    """Per-process protection; production proxies should enforce a second global limit."""

    def __init__(self, limit: int, window_seconds: int = 60) -> None:
        self._limit = limit
        self._window = window_seconds
        self._requests: dict[str, deque[float]] = defaultdict(deque)
        self._lock = threading.Lock()

    def check(self, request: Request) -> None:
        address = request.client.host if request.client else "unknown"
        key = f"{address}:{request.url.path}"
        now = time.monotonic()
        with self._lock:
            entries = self._requests[key]
            while entries and entries[0] <= now - self._window:
                entries.popleft()
            if len(entries) >= self._limit:
                raise HTTPException(status.HTTP_429_TOO_MANY_REQUESTS, "Rate limit exceeded")
            entries.append(now)
