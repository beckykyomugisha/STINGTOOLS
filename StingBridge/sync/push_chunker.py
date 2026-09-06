"""Client-side push chunking with retry (plan §1.4.4).

The IFC watcher used to push in a fixed loop of 100 with no retry: a single
transient 503 lost that slice of the model silently into ``result["errors"]``,
and a chunk the server judged too large failed permanently no matter how many
times it was re-dropped.

Three behaviours, each earning its keep on a 10k-element file:

* **Chunking** — never one multi-MB body. Size is configurable because the right
  value depends on the server's request limit, not on anything the client knows.
* **Retry on transient failures** — timeouts, connection resets and 5xx/429 are
  the network being the network. Retried with exponential backoff. A 4xx that
  is not 429/408 is a *decision* by the server and is never retried: re-sending
  a body it already rejected just multiplies the failure.
* **Split on 413** — "payload too large" is the one failure where the same
  request will never succeed but a *smaller* one will. The chunk is halved and
  both halves re-queued, down to a single element. This is what stops an
  operator having to guess ``STING_PUSH_CHUNK_SIZE`` before their first
  successful ingest.

Transport-agnostic: ``send`` is any callable taking a list of elements. It knows
nothing about Planscape, so the unit tests drive it with a fake that fails on
schedule.
"""
from __future__ import annotations

import logging
import time
from collections import deque
from dataclasses import dataclass, field
from typing import Any, Callable, Optional, Sequence

log = logging.getLogger(__name__)

DEFAULT_CHUNK_SIZE = 100
MAX_CHUNK_SIZE = 1000
DEFAULT_MAX_RETRIES = 3

_BASE_BACKOFF_S = 0.5
_MAX_BACKOFF_S = 8.0

#: Statuses worth sending the same body again for.
#: 429 = rate limited, 408 = request timeout, 425 = too early, 5xx = server side.
TRANSIENT_STATUS = frozenset({408, 425, 429, 500, 502, 503, 504})

#: The one status where a *smaller* body is the fix rather than a later one.
SPLIT_STATUS = frozenset({413})

#: Exception-type name fragments that mean "the network, not the request".
#: Matched on the type name so ``requests`` stays an implementation detail —
#: this module is used by tests that never import it.
_TRANSIENT_TYPE_HINTS = ("timeout", "connection", "chunkedencoding", "protocol")


def status_of(exc: BaseException) -> Optional[int]:
    """HTTP status carried by an exception, when it carries one.

    ``requests.HTTPError`` hangs the response off ``.response``; a bare
    transport error has none, which is the signal to fall back to type-sniffing.
    """
    resp = getattr(exc, "response", None)
    status = getattr(resp, "status_code", None)
    return status if isinstance(status, int) else None


def is_transient(exc: BaseException) -> bool:
    """True when re-sending the identical body could plausibly succeed."""
    status = status_of(exc)
    if status is not None:
        return status in TRANSIENT_STATUS
    name = type(exc).__name__.lower()
    return any(hint in name for hint in _TRANSIENT_TYPE_HINTS)


def should_split(exc: BaseException) -> bool:
    """True when the body was rejected for its size, so halving it may work."""
    return status_of(exc) in SPLIT_STATUS


def backoff_delay(attempt: int) -> float:
    """Exponential backoff for retry ``attempt`` (1-based), capped."""
    return min(_BASE_BACKOFF_S * (2 ** max(0, attempt - 1)), _MAX_BACKOFF_S)


@dataclass
class ChunkedPushResult:
    """What a push run actually managed to deliver."""

    sent: int = 0                  # elements the server accepted
    failed: int = 0                # elements in chunks that gave up
    chunks_sent: int = 0
    chunks_failed: int = 0
    retries: int = 0               # re-sends of an identical body
    splits: int = 0                # 413-driven halvings
    errors: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return self.failed == 0

    def summary(self) -> str:
        bits = [f"{self.sent} pushed in {self.chunks_sent} chunk(s)"]
        if self.retries:
            bits.append(f"{self.retries} retry/retries")
        if self.splits:
            bits.append(f"{self.splits} split(s) on 413")
        if self.failed:
            bits.append(f"{self.failed} element(s) FAILED in {self.chunks_failed} chunk(s)")
        return ", ".join(bits)


def chunked_push(
    send: Callable[[list[Any]], Any],
    elements: Sequence[Any],
    *,
    chunk_size: int = DEFAULT_CHUNK_SIZE,
    max_retries: int = DEFAULT_MAX_RETRIES,
    sleep: Callable[[float], None] = time.sleep,
    on_progress: Optional[Callable[[str], None]] = None,
) -> ChunkedPushResult:
    """Push ``elements`` through ``send`` in retrying, self-shrinking chunks.

    ``sleep`` is injected so tests do not actually wait out the backoff.

    A chunk that exhausts its retries is recorded and the run *continues* with
    the next chunk: on a large ingest, losing one slice is much better than
    abandoning the elements that would have gone through afterwards. The caller
    sees exactly what was lost via ``failed`` / ``errors``.
    """
    result = ChunkedPushResult()
    if not elements:
        return result

    size = max(1, min(int(chunk_size or DEFAULT_CHUNK_SIZE), MAX_CHUNK_SIZE))
    retries_allowed = max(0, int(max_retries))

    pending: deque[list[Any]] = deque(
        list(elements[i: i + size]) for i in range(0, len(elements), size)
    )

    while pending:
        chunk = pending.popleft()
        attempt = 0
        while True:
            try:
                send(chunk)
            except Exception as exc:  # noqa: BLE001 — classified below, never swallowed
                # Size rejection: the same body will never fit, a smaller one may.
                if should_split(exc) and len(chunk) > 1:
                    mid = len(chunk) // 2
                    # appendleft twice, second half first, so order is preserved.
                    pending.appendleft(chunk[mid:])
                    pending.appendleft(chunk[:mid])
                    result.splits += 1
                    log.info("Push chunk of %d rejected as too large — splitting into %d + %d",
                             len(chunk), mid, len(chunk) - mid)
                    if on_progress:
                        on_progress(f"Chunk too large ({len(chunk)}) — splitting")
                    break

                if is_transient(exc) and attempt < retries_allowed:
                    attempt += 1
                    result.retries += 1
                    delay = backoff_delay(attempt)
                    log.warning("Push chunk failed (%s) — retry %d/%d in %.1fs",
                                exc, attempt, retries_allowed, delay)
                    sleep(delay)
                    continue

                # Out of retries, or a failure retrying cannot fix.
                result.failed += len(chunk)
                result.chunks_failed += 1
                msg = (f"push chunk of {len(chunk)} failed after {attempt} "
                       f"retry/retries: {exc}")
                result.errors.append(msg)
                log.error(msg)
                if on_progress:
                    on_progress(msg)
                break

            result.sent += len(chunk)
            result.chunks_sent += 1
            break

    return result
