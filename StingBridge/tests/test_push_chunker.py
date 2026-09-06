"""Client-side push chunking + retry (plan §1.4.4).

The behaviours under test are the ones a 10k-element IFC actually hits: bodies
too big for the server, a transient blip mid-run, and a permanent rejection that
must not be hammered.
"""
from __future__ import annotations

import sys
from pathlib import Path

import pytest

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from StingBridge.sync.push_chunker import (  # noqa: E402
    DEFAULT_CHUNK_SIZE, MAX_CHUNK_SIZE, backoff_delay, chunked_push,
    is_transient, should_split,
)


class _Resp:
    def __init__(self, status_code): self.status_code = status_code


class _HttpError(Exception):
    """Stands in for requests.HTTPError — carries a .response like the real one."""
    def __init__(self, status): super().__init__(f"HTTP {status}"); self.response = _Resp(status)


class ConnectionResetError_(Exception):
    """Type name contains 'connection' — the transport-error sniff path."""


class _Sink:
    """Records chunks; can be told to fail on schedule."""

    def __init__(self, fail_plan=None):
        self.chunks: list[list] = []
        self.attempts = 0
        self._plan = list(fail_plan or [])

    def __call__(self, chunk):
        self.attempts += 1
        if self._plan:
            exc = self._plan.pop(0)
            if exc is not None:
                raise exc
        self.chunks.append(list(chunk))

    @property
    def delivered(self):
        return [e for c in self.chunks for e in c]


def _els(n):
    return [f"e{i}" for i in range(n)]


def _no_sleep(_seconds):  # keep the suite fast; backoff maths is tested directly
    pass


# ── chunking ──────────────────────────────────────────────────────────────────

def test_splits_into_chunks_and_delivers_everything_in_order():
    sink = _Sink()
    res = chunked_push(sink, _els(250), chunk_size=100, sleep=_no_sleep)

    assert [len(c) for c in sink.chunks] == [100, 100, 50]
    assert sink.delivered == _els(250), "chunking must not reorder or drop elements"
    assert res.sent == 250 and res.ok


def test_empty_input_sends_nothing():
    sink = _Sink()
    res = chunked_push(sink, [], sleep=_no_sleep)
    assert sink.attempts == 0 and res.sent == 0 and res.ok


@pytest.mark.parametrize("given,expected", [(0, DEFAULT_CHUNK_SIZE), (-5, 1),
                                            (10_000, MAX_CHUNK_SIZE)])
def test_chunk_size_is_clamped_to_something_sendable(given, expected):
    """A nonsense chunk size must not produce a zero-size loop that never ends."""
    sink = _Sink()
    chunked_push(sink, _els(expected + 1), chunk_size=given, sleep=_no_sleep)
    assert len(sink.chunks[0]) == expected


# ── retry ─────────────────────────────────────────────────────────────────────

@pytest.mark.parametrize("status", [408, 425, 429, 500, 502, 503, 504])
def test_transient_statuses_are_retried_and_then_succeed(status):
    sink = _Sink(fail_plan=[_HttpError(status)])
    res = chunked_push(sink, _els(5), chunk_size=5, max_retries=3, sleep=_no_sleep)

    assert res.sent == 5, f"HTTP {status} is transient and should have been retried"
    assert res.retries == 1 and res.ok


def test_transport_errors_without_a_status_are_retried():
    """A timeout carries no HTTP status — it is classified by exception type."""
    sink = _Sink(fail_plan=[ConnectionResetError_("reset by peer")])
    res = chunked_push(sink, _els(3), chunk_size=3, max_retries=2, sleep=_no_sleep)
    assert res.sent == 3 and res.retries == 1


def test_retries_are_bounded_and_the_chunk_is_reported_lost():
    sink = _Sink(fail_plan=[_HttpError(503)] * 10)
    res = chunked_push(sink, _els(4), chunk_size=4, max_retries=2, sleep=_no_sleep)

    assert sink.attempts == 3, "1 initial attempt + 2 retries, not an unbounded loop"
    assert res.sent == 0 and res.failed == 4 and not res.ok
    assert res.errors, "a lost chunk must be reported, never silently dropped"


@pytest.mark.parametrize("status", [400, 401, 403, 404, 422])
def test_permanent_rejections_are_not_retried(status):
    """The server has decided. Re-sending the same body just multiplies the failure."""
    sink = _Sink(fail_plan=[_HttpError(status)] * 5)
    res = chunked_push(sink, _els(2), chunk_size=2, max_retries=3, sleep=_no_sleep)

    assert sink.attempts == 1, f"HTTP {status} must not be retried"
    assert res.retries == 0 and res.failed == 2


def test_backoff_grows_and_is_capped():
    delays = [backoff_delay(i) for i in range(1, 8)]
    assert delays == sorted(delays), "backoff must be non-decreasing"
    assert delays[0] < delays[3], "backoff must actually grow"
    assert delays[-1] <= 8.0, "backoff must be capped or a long outage stalls the watcher"


def test_backoff_is_actually_waited_between_retries():
    waited: list[float] = []
    sink = _Sink(fail_plan=[_HttpError(503), _HttpError(503)])
    chunked_push(sink, _els(1), chunk_size=1, max_retries=3, sleep=waited.append)

    assert len(waited) == 2 and waited[1] > waited[0], (
        "retries must back off, not hot-loop against a struggling server")


# ── 413 splitting ─────────────────────────────────────────────────────────────

def test_413_halves_the_chunk_instead_of_giving_up():
    """The one failure where a *smaller* body is the fix, not a later one."""
    sink = _Sink(fail_plan=[_HttpError(413)])
    res = chunked_push(sink, _els(8), chunk_size=8, sleep=_no_sleep)

    assert res.sent == 8, "a too-large chunk should have been split and delivered"
    assert res.splits == 1
    assert [len(c) for c in sink.chunks] == [4, 4]
    assert sink.delivered == _els(8), "splitting must preserve order"


def test_413_splits_repeatedly_until_the_body_fits():
    # Every chunk larger than 2 is rejected; the run must converge on its own.
    class _PickySink(_Sink):
        def __call__(self, chunk):
            self.attempts += 1
            if len(chunk) > 2:
                raise _HttpError(413)
            self.chunks.append(list(chunk))

    sink = _PickySink()
    res = chunked_push(sink, _els(8), chunk_size=8, sleep=_no_sleep)

    assert res.sent == 8 and res.ok
    assert all(len(c) <= 2 for c in sink.chunks)
    assert sink.delivered == _els(8)


def test_413_on_a_single_element_fails_rather_than_looping_forever():
    """Nothing left to halve. Must terminate and report, not recurse."""
    sink = _Sink(fail_plan=[_HttpError(413)] * 20)
    res = chunked_push(sink, _els(1), chunk_size=1, max_retries=1, sleep=_no_sleep)

    assert res.failed == 1 and not res.ok
    assert res.errors


# ── partial failure ───────────────────────────────────────────────────────────

def test_one_dead_chunk_does_not_abandon_the_rest():
    """Losing one slice of a large ingest beats losing everything after it."""
    sink = _Sink(fail_plan=[None, _HttpError(400), None])
    res = chunked_push(sink, _els(30), chunk_size=10, max_retries=0, sleep=_no_sleep)

    assert res.sent == 20 and res.failed == 10
    assert sink.delivered == _els(10) + _els(30)[20:], "the surviving chunks still went"
    assert res.chunks_failed == 1


# ── classification helpers ────────────────────────────────────────────────────

def test_classifiers_disagree_on_413_by_design():
    """413 must split, NOT retry — retrying the identical body can never work."""
    err = _HttpError(413)
    assert should_split(err) and not is_transient(err)
