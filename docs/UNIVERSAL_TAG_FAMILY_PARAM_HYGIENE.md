# Which parameters belong in the universal tag — and how the gates must be scoped

Answering, from the Family Types screenshot of 2026-09-17: *which rows should be deleted
before propagating, and should every `TAG_PARA_STATE_*_BOOL` be ticked by default?*

Measured against `main` @ `114e38244`: `MR_PARAMETERS.csv`, `TagFamilyConfig`,
`ParagraphDepthCommand`, and the pre-flight line from the 21:21 run.

---

## The short answer

| Question | Answer |
|---|---|
| Which rows to delete | Every **non-`TAG_*`** shared parameter. A tag reads element data from the *tagged element*, not from its own family parameters. ~72 of the master's 210 are of this kind. |
| Should all the gates be ticked | **No — tick `_1` and `_2` only.** Depth is cumulative and `Set depth` overwrites all ten on every run, so the ticks only decide what a freshly placed tag shows before anyone runs it. All ten ticked means a 65-row tag on first placement. |
| The finding neither question asked about | **`_1`, `_2` and `TAG_WARN_VISIBLE_BOOL` are Instance-scoped; `_4`–`_10` are Type.** `Set depth` writes to element **types**, so those three tiers cannot be driven at all. |

---

## 1 · The scope split is the real defect

From the screenshot, reading Revit's `(default)` suffix as "instance parameter":

| Parameter | Scope in the master | `MR_PARAMETERS.csv` declares |
|---|---|---|
| `TAG_PARA_STATE_1_BOOL` | **Instance** | Type |
| `TAG_PARA_STATE_2_BOOL` | **Instance** | Type |
| `TAG_PARA_STATE_3_BOOL` | **absent** | Type |
| `TAG_PARA_STATE_4_BOOL` … `_10_BOOL` | Type | Type |
| `TAG_WARN_VISIBLE_BOOL` | **Instance** | Type |

`SetParagraphDepthCommand` sweeps `ElementType`s — *"These are Type parameters — changes
apply to every instance sharing the type"* — and writes all ten gates on each carrier. An
Instance-scoped gate is therefore **present, scored by the conformance audit, and
undrivable**: `Set depth` writes nothing to it, and the tier looks broken for a reason no
audit was reporting.

**It would have propagated.** `AddMissingParams` only adds what a family does not already
have, so an existing Instance-scoped gate is skipped and stays Instance — in all 206
families. Two changes close that:

- `PropagateUniversalTagCommand.MakeVisibilityParamsType` converts any Instance-scoped gate
  in the clone to Type via `FamilyManager.MakeType`, counts it, and reports the count in the
  done dialog.
- The conformance audit now checks the **scope** of each gate, not just its presence, and
  warns when one is Instance.

`_3` being absent is not a defect: the 65-row label has **no T3 rows** (T1/T2, then T4
onward), so nothing gates on it. It is worth knowing that **depth 3 does nothing** on this
master even though `ParagraphDepthCommand` documents it as "Comprehensive". Propagation adds
`_3` as a Type parameter to every clone, so the clones will carry a gate with no rows.

---

## 2 · Which rows to delete

**The rule:** a tag family needs `TAG_*` parameters and nothing else.

- **Keep** — `TAG_PARA_STATE_1..10_BOOL`, `TAG_WARN_VISIBLE_BOOL`, the 128
  `TAG_{size}{style}_{colour}_BOOL` style matrix, and the box/leader parameters
  (`TAG_BOX_*`, `TAG_LEADER_*`). That is the 138 `TagFamilyConfig.StyleParams +
  VisibilityParams` set that propagation adds and the style engine drives.
- **Delete** — every parameter that describes the *tagged element*: `MEP_*`, `MNT_*`,
  `PRJ_*`, `RGL_*`, `STR_*`, `PER_*`, `HVC_*`, `ELC_*`, `PLM_*`, `ASS_*`, `CST_*`,
  `WARN_<discipline>_*`.

**Why they are safe to delete.** A tag's label fields come from the **tagged category's**
parameters — that is what the left-hand pane of Edit Label lists. The family's own copy of
`MNT_FREQUENCY_TXT` is not what row N prints; the duct's copy is. The family copy does two
things, both bad:

1. it is offered to the project on load, which is where the twelve type conflicts came from;
2. it makes the family offer **210 shared parameters** (the pre-flight line from the 21:21
   run) where ~138 is the designed footprint.

**Why the deletion is safe to attempt.** Revit refuses to delete a parameter that a label
row or a formula still references. So the refusal *is* the gate: delete in batches, and
anything Revit protects is something to look at rather than force. Confirm with the label
row count afterwards — it must still be 65.

**Two to check before deleting.** `MNT_TYPE_TXT` and `WARN_HVC_DCT_SOUNDLVL_DB` are greyed
out in the screenshot, and greying means something else owns the row. Select each and open
Modify (the pencil) to see what it is before touching it.

---

## 3 · Should every gate be ticked?

**No. `_1` and `_2` ticked; `_3`–`_10` clear.** Reasons, in order of weight:

1. **All ten ticked means depth 10 on first placement** — all 65 rows print. A tag like that
   is unusable on a drawing and reads as a broken family to anyone who places one.
2. **The ticks are not the control.** `Set depth` writes all ten gates on every carrier each
   time it runs, so whatever the family ships with is overwritten the first time anyone sets
   a depth. The ticks only decide the out-of-box state.
3. **Depth 2 is the documented "Standard" mode** — T1 identity plus T2 dimensions, materials,
   thermal, acoustic. That is a sensible tag; T5 cost and T8 clash data are not what you want
   printing by default.
4. `TAG_WARN_VISIBLE_BOOL` **ticked** is right: a warning that has to be switched on is a
   warning nobody sees.

This recommendation holds whichever way the open question in §4 falls, which is why it is
worth doing now.

---

## 4 · The open question — and the 30-second test

Which copy of a gate does a label row actually read: **the tag type's, or the tagged
element's?** A tag label's calculated value is evaluated against the tagged element's
parameters, which suggests the tagged element's — and `MR_PARAMETERS` binds all ten gates
across model categories, so both copies exist.

It matters:

- **If the tagged element's copy wins**, the tag family's own gates are inert, and tier
  visibility is controlled entirely by `Set depth` on model types. The smoke test's V3
  ("flip `TAG_PARA_STATE_4_BOOL` on the Duct tag type") would then be testing the wrong
  parameter and would fail for a reason that is not a defect.
- **If the tag type's copy wins**, the scope fix in §1 is load-bearing, not hygiene.

**The test**, once a tag is placed on a duct: flip `TAG_PARA_STATE_7_BOOL` on the **tag
type** and see whether the T7 rows disappear. Then flip it back, flip it on the **duct
type** instead, and see whether they disappear then. Whichever one moves the tag is the one
that matters, and the answer belongs in this document rather than in anyone's memory.

Until it is answered, keep both copies Type-scoped and consistent — which is what §1 now
enforces.
