"""Tests for StingTokenLockError and BonsaiBridge.write_tag_segment()."""
import pytest
import sys
import os
from unittest.mock import MagicMock, patch, call

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from core.exceptions import StingTokenLockError


class TestStingTokenLockError:
    def test_attributes(self):
        err = StingTokenLockError("ASS_DISC_TXT", "M", "E")
        assert err.param_name == "ASS_DISC_TXT"
        assert err.locked_value == "M"
        assert err.attempted_value == "E"

    def test_message_contains_names(self):
        err = StingTokenLockError("ASS_DISC_TXT", "M", "E")
        msg = str(err)
        assert "ASS_DISC_TXT" in msg
        assert "M" in msg
        assert "E" in msg

    def test_is_exception(self):
        with pytest.raises(StingTokenLockError):
            raise StingTokenLockError("X", "old", "new")


class TestWriteTagSegment:
    """Unit tests for BonsaiBridge.write_tag_segment() without a live IFC file."""

    def _make_bridge(self):
        from core.bonsai import BonsaiBridge
        bridge = BonsaiBridge.__new__(BonsaiBridge)
        bridge._caps = None
        return bridge

    def test_raises_when_locked_and_value_differs(self):
        bridge = self._make_bridge()
        element = MagicMock()

        def fake_read(el, pset, prop):
            if prop == bridge._TOKEN_LOCK_PROP:
                return "True"
            if prop == "ASS_DISC_TXT":
                return "M"
            return None

        bridge._read_pset_property = fake_read
        bridge._write_pset_property = MagicMock(return_value=True)

        with pytest.raises(StingTokenLockError) as exc_info:
            bridge.write_tag_segment(element, "ASS_DISC_TXT", "E")
        assert exc_info.value.locked_value == "M"
        assert exc_info.value.attempted_value == "E"

    def test_allows_same_value_when_locked(self):
        bridge = self._make_bridge()
        element = MagicMock()

        def fake_read(el, pset, prop):
            if prop == bridge._TOKEN_LOCK_PROP:
                return "True"
            if prop == "ASS_DISC_TXT":
                return "M"
            return None

        bridge._read_pset_property = fake_read
        bridge._write_pset_property = MagicMock(return_value=True)

        # Same value — should not raise
        result = bridge.write_tag_segment(element, "ASS_DISC_TXT", "M")
        assert result is True

    def test_writes_audit_trail_on_value_change(self):
        bridge = self._make_bridge()
        element = MagicMock()
        written = {}

        def fake_read(el, pset, prop):
            if prop == bridge._TOKEN_LOCK_PROP:
                return "False"
            if prop == "ASS_DISC_TXT":
                return "M"
            return None

        def fake_write(el, pset, prop, val):
            written[prop] = val
            return True

        bridge._read_pset_property = fake_read
        bridge._write_pset_property = fake_write

        bridge.write_tag_segment(element, "ASS_DISC_TXT", "E")

        assert written.get(bridge._PREV_TAG_PROP) == "M"
        assert bridge._MODIFIED_AT_PROP in written
        assert written["ASS_DISC_TXT"] == "E"

    def test_no_audit_when_first_write(self):
        bridge = self._make_bridge()
        element = MagicMock()
        written = {}

        def fake_read(el, pset, prop):
            return None  # no existing value

        def fake_write(el, pset, prop, val):
            written[prop] = val
            return True

        bridge._read_pset_property = fake_read
        bridge._write_pset_property = fake_write

        bridge.write_tag_segment(element, "ASS_DISC_TXT", "M")

        # Only the target property written — no previous-tag audit trail
        assert bridge._PREV_TAG_PROP not in written
        assert written["ASS_DISC_TXT"] == "M"
