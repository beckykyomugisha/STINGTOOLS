# Tagging accuracy — Revit test protocol (TAGACC-12)

Method only; record results in `ROADMAP.md` (TAGACC-12). Written 2026-09-29 for the
TAGACC-1..15 changes, none of which has yet run in Revit.

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

## Part B — manual, two users (what one session cannot do)

Needs a workshared central model with STING parameters loaded, two Revit sessions under
two different user names, and the same build on both. Leave SEQ_LOCK_MODE at `block`.

1. **User A** places a new mechanical equipment item and runs Batch Tag (Skip). Note its tag.
2. **User B**, without syncing, places another item of the same family on the same level and
   runs Batch Tag.
   - Expected: B's element is **not** given a number. The Batch Tag report lists it under
     refusals with "SEQ deferred — the SEQ counter is borrowed by *A*". With the auto-tagger
     on, it is deferred instead.
3. **User A** synchronises with central (keep "relinquish borrowed elements" ticked).
4. **User B** synchronises (or Reload Latest), then runs Batch Tag again.
   - Expected: B's element is tagged with the next number after A's. No two elements share a tag.
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
3. **Project Cfg → Save Config to Project** keeps RETAG_MOVED_ELEMENTS, RENUMBER_ON_OVERWRITE
   and SEQ_LOCK_MODE in the file (the save rewrites the whole file).
