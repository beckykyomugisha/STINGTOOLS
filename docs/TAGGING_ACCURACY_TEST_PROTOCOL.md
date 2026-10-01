# Tagging accuracy — Revit test protocol (TAGACC-12)

Method only; record results in `ROADMAP.md` (TAGACC-12). Written 2026-09-29 for the
TAGACC-1..15 changes, none of which has yet run in Revit; extended 2026-10-01 so every TAGACC id
is either named at the step that exercises it or listed under **Covered elsewhere**.

## Part A — automated, one Revit session

`StingTools.Revit.SmokeTests/TaggingAccuracySmokeTests.cs` runs the real tagging pipeline
on a model built from the metric template:

| Test | What it proves |
|---|---|
| `CopiedElement_IsResequenced_AndTheOriginalKeepsItsTag` | A copied column arrives with its source's tag; a normal tagging run gives the copy a new number and leaves the original alone (TAGACC-1). |
| `ElementMovedToAnotherLevel_TakesTheNewLevel_AndKeepsItsSeq` | A pipe moved to Level 2 gets LVL = L02 in its tag and keeps its sequence number (TAGACC-5). |
| `OverwriteTwice_KeepsEverySequenceNumber` | Two Overwrite runs change no sequence number and create no duplicate (TAGACC-4). |
| `TagHeldByTwoElements_NewerIsResequenced` | The state a sync leaves when two users used one number is found by the index scan and repaired (TAGACC-2, simulated). |

Run with Revit closed:

```powershell
pwsh tools/run_revit_smoke.ps1
```

`HarnessIntegrityTests` fails the run if Revit loaded a different `StingTools.dll`.

## Part B — manual, two users (what one session cannot do) — TAGACC-2, -13, -17

Needs a workshared central model with STING parameters loaded, two Revit sessions under
two different user names, and the same build on both. Leave SEQ_LOCK_MODE at `block`.

1. **User A** places a new mechanical equipment item and runs Batch Tag (Skip). Note its tag.
   A now holds the SEQ counter element (`StingSeqLockStore`, TAGACC-13).
2. **User B**, without syncing, places another item of the same family on the same level and
   runs Batch Tag.
   - Expected: B's element is **not** given a number. The Batch Tag report lists it under
     refusals with "SEQ deferred — the SEQ counter is borrowed by *A*". With the auto-tagger
     on, it is deferred instead.
3. **User A** synchronises with central (keep "relinquish borrowed elements" ticked).
4. **User B** synchronises, then runs Batch Tag again.
   - Expected: B's element is tagged with the next number after A's. No two elements share a tag.
4a. **Reload Latest instead of sync (TAGACC-17).** Repeat 1–3, but at step 4 User B uses
   *Collaborate › Reload Latest* and does **not** sync, with the auto-tagger on.
   - Expected: B's deferred element is tagged straight after the reload, with the number after A's,
     and the STING log shows the deferred retry running from `DocumentReloadedLatest`.
5. Set SEQ_LOCK_MODE to `warn` with **Tag Rules** on one session, repeat 1–4.
   - Expected: B is numbered at step 2 (possibly the same number as A); after both sync, the
     "duplicate tags after sync" dialog appears (auto-tagger off) or the newer element is
     re-sequenced automatically (auto-tagger on).
6. **Copy / paste across users**: A copies a tagged element and syncs; B syncs and runs Batch Tag.
   - Expected: the copy (newer ElementId) is re-sequenced; the original keeps its tag.

## Part C — settings surface

1. TAGGING tab → **Tag Rules**: each of the four lines flips its setting, the dialog reopens
   showing the new state, and `project_config.json` beside the model gains the key.
2. **Batch Tag** mode picker shows "Moved elements: tags update/fixed" in its status line and
   offers "Overwrite all … and renumber"; choosing it renumbers that run only, and the saved
   RENUMBER_ON_OVERWRITE is unchanged afterwards.
3. **Project Cfg → Save Config to Project** keeps every key it does not own (TAGACC-19). Before
   saving, add `"SEQ_SCHEME": "PerLevel"`, `"CDE_FIRST_LAYOUT": false` and a made-up
   `"ZZ_KEEP_ME": 1` to `project_config.json` by hand; save; all three are still there, along with
   RETAG_MOVED_ELEMENTS, RENUMBER_ON_OVERWRITE and SEQ_LOCK_MODE. Toggle the auto-tagger twice
   (it saves through the same writer) and check again. Then break the JSON (delete a brace) and
   save: the save is refused and the file is left exactly as it was.
4. **Token Confidence Audit (TAGACC-18).** In a model with a `STING-ZONE::Z02` scope box and no
   rooms, tag an element inside it, then run *Token Confidence Audit*. Expected: its ZONE is High
   ("inside a STING-ZONE:: scope box"), not counted as a fallback; the CSV has `ZONE_BAND` /
   `ZONE_REASON` columns and lists only elements with a Medium or Low token.

## Covered elsewhere

| ID | Where |
|---|---|
| TAGACC-3 | Unit: `TagTokenPolicyTests`; gate `tools/check_token_policy_wired.py` |
| TAGACC-6 | Unit: `CategoryEnglishNamesTests` (localised name → English key) |
| TAGACC-14, -16 | **Partly.** The mapping is tested (above); the call-site conversions to `GetCategoryName` are not — check by running Batch Tag on a non-English Revit and confirming DISC / SYS match an English install. |
| TAGACC-7, -8 | Unit: `SysConnectorChoiceTests` (TAGACC-20) — connector choice and the 15 °C hydronic rule |
| TAGACC-9, -10, -15 | Unit: `SpatialNameCodesTests` |
| TAGACC-18 | Unit: `TokenConfidenceBandsTests`; manual: Part C step 4 |
| TAGACC-19 | Unit: `ConfigFileMergeTests`; manual: Part C step 3 |
| TAGACC-11 | **Not covered.** Proximity copy (same level, plan distance, derived values only) is Revit-bound and has no test; check by hand: two untagged ducts on one level next to a tagged one with a room-derived LOC, a third on another level — only the first two inherit. |
