"""SEQ minting — assign the 8th tag segment from server-held counters.

Without this the bridge produces 7-segment tags (`M-BLD1-Z01-L02-HVAC-SUP-AHU`)
while the Revit plugin produces 8 (`…-AHU-0003`), so the same physical building
is numbered in one host and not the other.

The counter keys here MUST match `SeqAssigner.BuildSeqKey` in the Revit plugin
(`StingTools/Core/SeqAssigner.cs`) exactly — the whole point is that ArchiCAD
and Revit draw from the *same* per-key counters, so a key-format divergence
would silently restart numbering per host and mint duplicates across them.

Numbers are reserved in one batched call per run, and the server bumps each
counter atomically, so two runs can never be handed the same number.
"""
from __future__ import annotations

import logging

log = logging.getLogger(__name__)

# Matches TagConfig.NumPad in the plugin. A 4-digit pad gives 9999 per key.
SEQ_PAD = 4
_MAX_SEQ = 10 ** SEQ_PAD - 1


def build_seq_key(
    disc: str,
    sys: str,
    lvl: str,
    zone: str = "",
    loc: str = "",
    include_zone: bool = False,
    include_loc: bool = False,
) -> str:
    """Port of ``SeqAssigner.BuildSeqKey`` (StingTools/Core/SeqAssigner.cs).

    Key shapes: ``DISC_SYS_LVL`` · ``DISC_ZONE_SYS_LVL`` · ``DISC_LOC_SYS_LVL``
    · ``DISC_LOC_ZONE_SYS_LVL``.

    The placeholder normalisation (missing → ``A`` / ``GEN`` / ``L00``, and
    ``XX``/``ZZ`` → defaults) is copied deliberately: it is what makes an
    untagged ArchiCAD element land in the same bucket as its Revit counterpart
    instead of opening a private counter.
    """
    disc = disc or "A"
    sys = sys or "GEN"
    if not lvl or lvl == "XX":
        lvl = "L00"

    loc_part = None
    if include_loc:
        loc_part = loc
        if not loc_part or loc_part == "XX":
            loc_part = "BLD1"

    if include_zone:
        if not zone or zone in ("XX", "ZZ"):
            zone = "Z01"
        return (f"{disc}_{loc_part}_{zone}_{sys}_{lvl}" if include_loc
                else f"{disc}_{zone}_{sys}_{lvl}")

    return (f"{disc}_{loc_part}_{sys}_{lvl}" if include_loc
            else f"{disc}_{sys}_{lvl}")


def format_seq(n: int) -> str:
    """Zero-pad a sequence number to the plugin's 4-digit convention."""
    return str(n).zfill(SEQ_PAD)


def assign_sequences(
    ps_client,
    token_dicts: list[dict],
    include_zone: bool = False,
    include_loc: bool = False,
) -> int:
    """Fill in the ``seq`` token for every element that lacks one.

    ``token_dicts`` is mutated in place — each entry is the per-element token
    dict the callers already build (``disc``/``sys``/``lvl``/``zone``/``loc``/…).

    Returns how many elements were assigned a number.

    Idempotent with respect to already-numbered elements: anything that already
    carries a ``seq`` is skipped and consumes nothing, so re-running over the
    same model does not burn the counter or renumber stable elements.
    """
    # Group the unnumbered elements by counter key. Order within a key is the
    # caller's iteration order, which is stable for a given model.
    pending: dict[str, list[dict]] = {}
    for tokens in token_dicts:
        if str(tokens.get("seq") or "").strip():
            continue  # already numbered — leave it alone
        key = build_seq_key(
            disc=tokens.get("disc", ""),
            sys=tokens.get("sys", ""),
            lvl=tokens.get("lvl", ""),
            zone=tokens.get("zone", ""),
            loc=tokens.get("loc", ""),
            include_zone=include_zone,
            include_loc=include_loc,
        )
        pending.setdefault(key, []).append(tokens)

    if not pending:
        return 0

    # One request for the whole run, not one per element.
    reservations = {key: len(group) for key, group in pending.items()}
    blocks = ps_client.reserve_seq(reservations)

    if not blocks:
        log.warning(
            "No SEQ numbers reserved for %d group(s) - tags stay 7-segment",
            len(pending),
        )
        return 0

    assigned = 0
    for key, group in pending.items():
        block = blocks.get(key)
        if not block:
            # Missing key = not reserved. Must not invent a number here: doing
            # so is exactly how two runs end up minting the same value.
            log.warning("No SEQ block returned for %r - %d element(s) stay unnumbered",
                        key, len(group))
            continue

        start, count = block["start"], block["count"]
        if count < len(group):
            log.warning("SEQ block for %r is short (%d < %d) - numbering what we can",
                        key, count, len(group))

        for offset, tokens in enumerate(group[:count]):
            n = start + offset
            if n > _MAX_SEQ:
                # The plugin treats this as an overflow rather than widening the
                # pad, because a wider tag breaks every downstream schedule.
                log.warning("SEQ overflow for %r at %d (pad-%d capacity) - "
                            "remaining elements stay unnumbered", key, n, SEQ_PAD)
                break
            tokens["seq"] = format_seq(n)
            assigned += 1

    return assigned
