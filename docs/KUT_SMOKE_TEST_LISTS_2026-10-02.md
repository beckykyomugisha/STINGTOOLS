# KUT live build — smoke test lists (tagging + ACC)

**Written 2026-10-02, updated 2026-10-04** against the build Revit loads: `C:\Dev\STING_KUT_LIVE` at **`47cd237f5`**.
That is the ACC integration branch (`e95e08d43`) + **PR #1048** (data-schema drift, *still open*) + main through #1052,
then PR #1053 (drawing types round 11) and PR #1054 (Tag Family Audit counts). PR #1056 (TAGACC-27, the Batch Tag
binding pre-flight) is **not** in it yet. Every item below shipped in that DLL and has not been run in Revit, unless it says otherwise.

**Rules for every run**
- **Use a copy of the KUT model**, never the central. T-F2, T-F3 and T-B1 write to families or SEQ numbers.
- Before you start, check the DLL with `Select-String -Path "$env:APPDATA\Autodesk\Revit\Addins\*\StingTools.addin" -Pattern "<Assembly>"`. Every line should show `C:\Dev\STING_KUT_LIVE\CompiledPlugin\StingTools.dll`.
- After loading, run **Load Shared Parameters** once (PR #1048 brings the count to 3,686), then **DOCS › DRAWING TYPES › Self-Test**.
- Log each line as *date · test id · PASS/FAIL · evidence*. For a FAIL, send the dialog text and the matching lines from `StingTools_yyyyMMdd.log` (the log sits beside the DLL).
- **Stop rule:** if T-0 or A-1 fails, stop there. Everything after them depends on them.

---

## List 1 — Tagging (about 45 min, one person)

### T-0 Gate
| # | Do | PASS looks like |
|---|---|---|
| T-0 | Open the copy. Run **Load Shared Parameters** **on this model** — it binds per document; running it on another open model does not count (2026-10-02 run: 0 of 3,121 tagged for exactly this reason). Then **TAGGING › Batch Tag** (Skip) on one view | Report opens; new elements get `DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ`; no exception in the log |

### T-C Config that used to load and do nothing (TAGACC-19, 22–26, #1048)
| # | Do | PASS looks like | Id |
|---|---|---|---|
| T-C1 | Hand-add `"SEQ_SCHEME": "PerLevel"`, `"ZZ_KEEP_ME": 1` to `project_config.json`. Run **Project Cfg › Save Config to Project**, then toggle the auto-tagger twice | Both keys still there afterwards. With the JSON deliberately broken (delete a brace), Save is **refused** and the file is unchanged | TAGACC-19 |
| T-C2 | Run **Tag Format** (`ConfigurableTagFormat`). Set separator `_` and pad 5. Save, restart Revit, then Batch Tag one new element | `project_config.json` has `TAG_FORMAT` with lowercase `separator` / `num_pad` / `segment_order`. The new tag uses `_` and a **5-digit** SEQ after the restart | TAGACC-23, -24 |
| T-C3 | Run the **Tag Rule Engine** with the JSON broken, as in T-C1 | Refused. The file is **not** replaced by a one-key file | TAGACC-22 |
| T-C4 | In `DISCIPLINE_PROFILES`, give `M` a `"CollisionMode": "Skip"` and a retired `"SeqPadWidth": 3`. Reload, then Batch Tag mechanical elements without choosing a mode | M uses Skip. The log has **one** warning naming `SeqPadWidth` as retired, with its replacement | TAGACC-25 |
| T-C5 | Add a typo key, `"SEQ_SCHEMEX": 1`. Reload | One warning names the unknown key. Real keys such as `COST_*` and `BOQ_TENDER_*` raise **no** warning | TAGACC-26 |
| T-C6 | Set `"SEQ_SCHEME": "Nonsense"` | Warning that it names no scheme; the previous scheme is kept | #1048 |
| T-C7 | Set the auto-tagger discipline filter as a JSON array, `["M","E"]`. Turn the auto-tagger on, place one M element and one A element | M is tagged and A is skipped. Before the fix, **everything** was skipped | #1048 |
| T-C8 | Add `"SEQ_RANGE_ALLOCATION": {"M": [1000, 1002]}`. Batch Tag 4 new M elements, then run **Pre-Audit**, **Fix Dupes** and **Validate** | 3 get 1000–1002 and the 4th is **refused, not duplicated**. Pre-Audit lists `SEQ_RANGE_FULL`. Validate flags a hand-typed M SEQ of 0050 as out of range | SEQ range |

### T-S Scope boxes and token confidence
| # | Do | PASS looks like | Id |
|---|---|---|---|
| T-S1 | Scope box `STING-ZONE::Z02` with no rooms. Tag an element inside it, then run **Token Conf** | ZONE = Z02, band **High** ("inside a STING-ZONE:: scope box"). CSV has `ZONE_BAND` / `ZONE_REASON` | TAGACC-18 |
| T-S2 | Rename a box `STING-LOC::Block A` (space) and another `STING-LOCATION::X`. Batch Tag, then run the Drawing Doctor | The tagging report lists both under **SCOPE BOXES** as unparsed. The Doctor shows "Unparsed box names" | DTW-144 |
| T-S3 | Two untagged ducts on L1 next to a tagged duct with a room LOC, and a third on L2 | Only the two L1 ducts inherit, with band **Medium** | TAGACC-11/18/21 |

### T-F Tag families (writes to families)
| # | Do | PASS looks like | Id |
|---|---|---|---|
| T-F1 | **Load** tag families into a copy that still has the old `… Tag Tag` tie-in / Specialty Equipment names | Renamed **in place**: placed tags stay put and no second family appears. If both names exist, it is reported and left alone | TAGFAM-7, -10 |
| T-F2 | **Create Tag Fams** in a throwaway project | Much faster than the old 124 s per door tag. New families have `TAG_STYLE_CODE_TXT` and **no** 128 `TAG_*_BOOL` switches. Type params are TYPE scope | TAGFAM-9 |
| T-F3 | Place a Room Finish, Fire Compartment and Fire / Accessible Door tag at 2.5 and 3.5 mm | Both sizes show; door tags draw their box; labels are left / middle aligned | TAGFAM-3 (headless export drew no room tags — this is the open look) |
| T-F4 | Run a drawing-type annotation pass on a plan with rooms, spaces and areas | Room / space / area tags placed with **no** per-room exception and no duplicates | DTW-83 |

### T-P Placement
| # | Do | PASS looks like | Id |
|---|---|---|---|
| T-P1 | On the Tag Studio **Scale** tab, set DUCTS to 2.0. Run **Smart Place** on ducts and pipes | Duct tag offsets are visibly about double; pipes unchanged | #1048 / scale |
| T-P2 | With no `TAG_PLACEMENT_PRESETS.json`, open placement presets. Then learn and save one | A **Default** preset is listed. The saved user file does **not** contain "Default" | DSCH-W3 |

### T-A Automated (Revit closed)
| # | Do | PASS looks like |
|---|---|---|
| T-A1 | `pwsh tools/run_revit_smoke.ps1` | 4/4 tagging-accuracy tests pass and harness integrity passes. Last run: 11/13 annotation; the 2 spot-slope tests are known inconclusive |

### T-B Two users (needs two people on a central) — open since TAGACC-12
Run `docs/TAGGING_ACCURACY_TEST_PROTOCOL.md` Part B steps 1–6 as written. These are SEQ borrow / defer, sync, Reload Latest, `warn` mode and copy across users.

---

## List 2 — ACC (about 90 min; needs the ACC Project Admin)

**A-1 to A-13 are `docs/KUT_ACC_SETUP_AND_SMOKE_TEST.md` §5 S1–S13. Run those first, unchanged.** Below are the
changes that list does not exercise. Run each one after the S-test it depends on.

### A-R Fortnightly issue workflow (on a copy)
| # | Do | PASS looks like | Id |
|---|---|---|---|
| A-R1 | Break one sheet's revision stamp, then run *KUT fortnightly issue* | Step 1 fails and names the sheet. Steps 6–7 show **BLOCKED**, and nothing reaches ACC | R1 |
| A-R2 | Run with **no** clouds | Step 2 fails with "no sheet carries a cloud"; no revision number is burnt | R7 |
| A-R3 | Lock a title block (`PRJ_TB_LOCK`) with a stale revision, then click **BIM › Revision Management › Leak Chk** | Listed as LOCKED, not a leak, and step 1 passes | R10, R12 |
| A-R4 | Cancel at the ACCPublish picker (step 6) | Step 7 uploads **nothing**. Last fortnight's ZIP is not re-sent and no transmittal is re-marked SENT | R2 |
| A-R5 | Supersede a deliverable whose PDF + DWG went up through the Export Centre | Both are archived in ACC; the ledger shows `retiredUtc` | R11 |

### A-D Data contract (rounds 4–5)
| # | Do | PASS looks like | Id |
|---|---|---|---|
| A-D1 | After S3/S4: **void** the issue in ACC, then run Sync Issue Status, then Pull Clashes | "Untrack + hold". Pull reports **CLOSED IN ACC, STILL CLASHING** and creates **no** new issue. Delete an issue in ACC, then Sync: NOT_FOUND, un-tracked | E1, F1 |
| A-D2 | Pull Clashes with the scope file temporarily renamed | The pull is **not** recorded complete, and no holds are released | F1 |
| A-D3 | Approve `…-C01.pdf` in a review mapped to A1, then Read Reviews and Accept. Repeat with the register row at C02 | The first sets A1. The second is NOT applied and gives the revision reason | E4 |
| A-D4 | Import an ACC issue several weeks old | `issues.json` keeps ACC's created date and creator; priority blank (`priority_defaulted`) | E6 |
| A-D5 | Import twice | The log shows the ACC Date watermark and a skew line; the state file has `lastSkewSeconds` | E8 |
| A-D6 | Add **only** a comment to an imported issue, then **Push Issue Changes** twice | The comment appears in ACC once; the second run posts nothing | E9 |
| A-D7 | MIDP CSV containing `05/03/2027`, then one with the Ref column renamed | Lands on **5 March**. The second file is refused, naming the column | E5, E7 |
| A-D8 | Open any ACC CSV export where a name starts with `=` | Cell shown as text, not run as a formula | E10 |

### A-T Tokens, transport and settings
| # | Do | PASS looks like | Id |
|---|---|---|---|
| A-T1 | On the ACC card: **Find my ACC project**, then pick a different project | The status names the cleared settings, and `acc_settings.json` loses `coordContainerId` / `folderUrn` / `cdeFolders`. Picking the same project clears nothing | P5 |
| A-T2 | With a sign-in over 3 days old, open a model and Save a new hub at once | A minute later the credentials file holds the new hub **and** a new refresh token | P11, D1 |
| A-T3 | Put an unknown key in `acc_settings.json` | The card says the file is ignored and names it. It does **not** say "not configured" | A4 |
| A-T4 | `ACC_UploadModel` twice on the same file, then a changed file at the same revision | The second run says "not uploaded again"; the third is refused | A12, C5 |
| A-T5 | WIP upload with no `cdeFolders.WIP` | Refused (UnroutedWipRefusal) | C9 |
| A-T6 | **🏷 Federation Tags** on the ACC card | Per-model / DISC tally, invalid codes, cross-model duplicates. A failed index reads **INCOMPLETE**, never "0 elements" | Fed. compliance |

### A-M One-off manual checks
| # | Do | PASS looks like | Id |
|---|---|---|---|
| A-M1 | Look in `_data\coord` for `transmittals.json.corrupt.*` / `deliverables.json.corrupt.*` | None. If there are any, merge their rows back into the live array, keeping the newer row on an id clash | C1 recovery |
| A-M2 | Export one sheet twice, unchanged, then compare with `certutil -hashfile x.pdf SHA256` | If the hashes match, nothing to do. If they differ, re-exports land as HELD, which is reported and does not fail the run | C6 |
| A-M3 | NW-1: Export Centre profile, NWC only | Opens at shared coordinates with an Element ID tab. CurrentView with no 3D view fails "needs a 3D view" | NW-1 |

### A-S Server (only if server sync / webhooks are used)
S11–S13 from the setup doc, then:
- **A-S1:** with `accClosedBetweenSweeps=create`, raise and close a Planscape issue between syncs. ACC accepts it as closed, or the failure gives the reason.
- **A-S2:** `GET /api/acc/reconnect-required` returns `count: 0`.
