# Verify Phase 1 — three fixes that have shipped and never been observed working

Deployed **22:34**, `ee68c932…`, from `C:\Dev\wt-kibale-integration\CompiledPlugin\StingTools.dll`.
Manifest confirmed on 2025/2026/2027. **Restart Revit before starting.**

Each step says what to record and what each outcome means. Report the observation, not the
conclusion — 1.2 has failed to run three times and twice the failure was in the method.

---

## 1.2 · G-47 — FUNC and SEQ. **The gate. Run this one first.**

**The previous test was invalid, and this is why.** The run was started from
`L1 - Architectural`, and the log read `processed=120 tagged=23 skipped=97 errors=0`.
`AutoTagCommand.cs:65` filters by view discipline, so mechanical elements were never
processed — the air terminal was almost certainly among the 97 skipped, and the tag
on screen was left over from an earlier run. Nothing about G-47 was tested.

1. Open a **Mechanical** view — a plan with the air terminals visible, not `L1 - Architectural`.
2. Select the air terminal. **Before tagging**, record from Properties:
   - `ASS_FUNC_TXT` = ______
   - `ASS_SEQ_NUM_TXT` = ______
3. Run **Tag & Combine**. Record from the result dialog: `processed` / `tagged` / `skipped`.
   **If `skipped` is most of them, you are still in the wrong view — stop and change view.**
4. Record the same two parameters **after**:
   - `ASS_FUNC_TXT` = ______
   - `ASS_SEQ_NUM_TXT` = ______
5. Record the **rendered tag** on the drawing: ______

| What you see | What it means |
|---|---|
| Both parameters populated **and** the tag renders them | **G-47 confirmed.** Staleness was the cause. Phase 2 unblocks. |
| Parameters populated, tag still shows blanks | The parameters are right and the **rendering** is stale — G-43, the display string, not G-47. |
| `ASS_FUNC_TXT` still empty | Not staleness at all. `FuncMap["HVAC"]="SUP"` exists, so this would mean the element's System Type is `Undefined` and SYS never resolved — a different defect (G-39). |
| `ASS_SEQ_NUM_TXT` still empty | G-40 is independent of G-47 and stays open. |

---

## 1.1 · G-51 — the 260 extended-param defaults

`LoadExtendedParams` was replacing 260 compiled defaults with the JSON's 17, so ~243 `Ext()`
keys resolved to an empty parameter name.

1. Run **Tag & Combine** (the same run as 1.2 is fine).
2. Search `StingTools.log` for `not found`. Record the count: ______
   - Specifically: are the **`DOOR_FUNC not found`** warnings gone? ______
3. Look at the tag block that previously showed `Rate:0`, `Cert#:0`, `Val:0`, `0UGX/0USD`.
   Record which of those changed: ______

> **Do not assume all four are Ext-fed.** `Rate:` and `0UGX` are likely rate-resolution
> (K-16b), which is a different fix — the rate table is sparse, so a zero there may be
> correct-and-honest rather than a bug. `Cert#:` and `Val:` are the more likely Ext keys.
> Record which changed; that distinguishes the two causes.

---

## 1.3 · Propagation — never run. **One family first.**

`STING - Air Terminal Tag` is currently a **Generic Model Tag**, so Revit never offers it for
Air Terminals.

1. Open **Propagate Universal Tag**. In the scope picker select **only**
   `STING - Air Terminal Tag`.
2. **Before running**, record what the results table says about category mismatch across all
   206 families: ______ mismatched.
3. Run it on that one family. Record:
   - `IsMismatch` note for it: ______
   - Its category after: ______
4. Place it on an air terminal. Does Revit offer it? ______

| Outcome | Meaning |
|---|---|
| Category becomes Air Terminal Tags, Revit offers it | Fix works — run the rest. |
| Category unchanged | The declared category did not resolve; send the results table. |
| Mismatch count is 0 across 206 | Suspicious — the resolver found no declarations. Send the note. |

**Do not run all 206 until the single family is confirmed.**

---

## 1.4 · D11 — shipped gate defaults *(no action, just confirm)*

Newly tagged elements should now carry `TAG_PARA_STATE_1` **and** `_2` ON, `_3`–`_10` OFF,
and `TAG_WARN_VISIBLE_BOOL` **ON**.

Confirm on the air terminal from 1.2: ______

Warn was shipping **OFF**, which is why the tag-completeness enforcement was invisible on
drawings — the counts were produced and the family's warning row was gated off.

---

## D10 · Flow units *(confirm while you are there)*

The tag previously rendered `Flow:0.882867` for an element whose Air Flow is `25.00 L/s`
— that is 25 ÷ 28.3168, Revit's internal ft³/s.

Record what the tag shows now: ______

Expected `25.00 L/s` or similar. If it still reads `0.882867`, the leak is on a path
other than the three fixed (`ParameterHelpers`, `DrawingTokenContext`, `BOQParagraphEnhancer`)
— send the tag and the parameter name.

---

## Send back

Just these:

1. **1.2** — the four parameter values (before/after), the processed/tagged/skipped counts, and the rendered tag
2. **1.1** — `not found` count, and which of the four zeros changed
3. **1.3** — the pre-run mismatch count, and whether Revit offers the tag afterwards
4. **1.4 / D10** — the gate defaults, and what Flow renders as
