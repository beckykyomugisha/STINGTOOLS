# Healthcare Pack — Completeness Remediation (Autonomous Agent Prompt)

You are an autonomous engineering agent in the **StingTools** monorepo. Execute
every workstream below **end to end, without stopping to ask**. At each choice
pick the **most flexible, sustainable, data-driven option** — one source of truth,
reused everywhere, changeable without a recompile, matching existing repo
patterns. Do not gold-plate; each finding is either FIXED or explicitly
DEFERRED-with-a-ROADMAP-entry (see WS-7). Read every cited file before editing.

These findings come from a **verified** completeness audit (binding mechanism,
SignalR wiring, water-flush endpoint, dead-param grep, and doc alignment were all
checked against the actual code). The verified false positives are fenced off in
DO-NOT-TOUCH — respect them.

## Ground rules

- **Do not ask for confirmation.** Work autonomously to completion.
- **Branch / worktree.** Continue on the existing worktree
  `C:\Dev\STINGTOOLS-hc-fixes`, branch `claude/healthcare-gap-fixes` — confirm with
  `git -C C:\Dev\STINGTOOLS-hc-fixes rev-parse --abbrev-ref HEAD`. If absent, create
  a fresh worktree off latest `claude/pm-complete`. Never touch the shared
  `C:\Dev\STINGTOOLS` checkout; never commit to `main`. One logical commit per
  workstream; commit messages imperative, ending with the repo's `Co-Authored-By`
  trailer.
- **Build.** This machine can build the Revit plugin (Revit 2025/2026 + .NET SDK)
  and `Planscape.Server` — build both where you touch them and report `0 errors`
  or the actual errors. Do NOT apply a "no build in sandbox" caveat.
- **New shared parameters MUST be fully registered** in ALL of: `MR_PARAMETERS.txt`,
  `MR_PARAMETERS.csv`, `PARAMETER_REGISTRY.json`, and `CATEGORY_BINDINGS.csv`
  (reference), following the exact sibling pattern with a next-free deterministic
  GUID. (This gap has recurred three times — do not add a param the code reads
  without registering it.)
- **Docs.** Append a `#### Completed (Phase N — …)` block to `docs/CHANGELOG.md`
  (next free phase number — check the file), update `docs/ROADMAP.md` per WS-7, and
  correct `CLAUDE.md` where a caveat is false. Leave the branch for review — no
  merge, no PR. Finish with a summary: files changed, build status, what was fixed
  vs deferred, and any new params + their GUIDs.

---

## DO-NOT-TOUCH — verified false positives / already-correct

Do **not** "fix" these — each was verified; changing them wastes effort or
regresses:

1. **CATEGORY_BINDINGS.csv "unbound-read" claims.** An audit alleged most
   validators read params off categories they aren't bound to → silent failure.
   **This is false.** Binding is group-driven in `LoadSharedParamsCommand`: params
   bind to the broad `coreCats` set unless their *group* is explicitly narrowed in
   `BuildGroupCategoryOverrides`. The healthcare groups (RAD_PROTECTION,
   MGS_SYSTEMS, CLN_CLINICAL, CEQ_CLINICAL, LIG_BEHAVIOURAL, ICT_HEALTHIOT) are
   **not** narrowed, so they bind to the broad set (which already includes Walls,
   Pipes, Electrical, Data/Nurse-Call/Comm devices). `CATEGORY_BINDINGS.csv` is a
   validation/reference artifact (`ValidateBindingsFromCsv`), NOT the binding
   authority. Do not re-bind params to chase this.
2. **Validator wiring / findings surfacing.** All 17 validators are already
   surfaced to the user (dock panel + individual commands + RunAll/Selected).
3. **Room-class canonicalisation, QE gate, NCRP-147 / medical-gas-diversity math**
   — verified correct. Do not touch.

---

## WS-1 — Wire the two missing SignalR broadcasts (automation, quick)

**Verified gap.** `HealthcareHub` defines `BroadcastMgasAlarm` and
`BroadcastAntiLigatureAlert`, but `HealthcareController` only ever calls
`BroadcastPressureReading` (the POST pressure-log path). MGPS-fail and
anti-ligature-fail events never reach mobile clients in real time.

**Task.** In `Planscape.Server` `HealthcareController`:
- In `PostMgasVerification`, after persist, when the verification is a FAIL
  (`overallPass == false`), `await HealthcareHub.BroadcastMgasAlarm(...)` with the
  project id + zone/gas/verifier payload, mirroring the pressure-broadcast shape.
- In `PostLigAudit` (anti-ligature POST), when the audit is a FAIL, `await
  HealthcareHub.BroadcastAntiLigatureAlert(...)`.
Match the existing broadcast method signatures; do not invent new hub methods.
Confirm `Planscape.Server` builds.

**Acceptance.** Both broadcasts fire on FAIL; build clean.

## WS-2 — Documentation alignment (information, trivial)

**Verified.** `CLAUDE.md` caveat #6 ("No dedicated Healthcare tab in the dock
panel") is **stale** — the BIM-Coordination-Centre Healthcare tab is fully built
(`UI/BIMCoordinationCenter.cs` ~9181-9323, live dashboard wired to the server
`GetHealthcareDashboardAsync` endpoint, 16 validator buttons).

**Task.** Rewrite caveat #6 to reflect reality: the Healthcare tab is integrated
into the BIM Coordination Centre (gated on `PRJ_ORG_HEALTH_FACILITY_TYPE_TXT`),
surfacing pressure/MGPS/anti-ligature/RDS dashboards + validator dispatch. Keep the
note that healthcare commands also dispatch via `WorkflowEngine.ResolveCommand` /
`StingCommandHandler`. While there, confirm the migrations caveat still correctly
states the documentation-DDL / prod-EF-pipeline (backlog P3-2) status.

**Acceptance.** Caveat #6 is true; no other caveat left false.

## WS-3 — Water-flush end-to-end (integration, the main build)

**Verified gap.** `Planscape/app/healthcare/water-flush.tsx` is display-only: its
`log()` writes local state, calls no API, enqueues nothing. There is no
`water-log` endpoint in `HealthcareController`, no `HealthcareWaterLog` entity, and
no `HC_WATER_FLUSH` offline action. HTM 04-01 sentinel-flush data is lost on app
restart. The other three mobile actions (pressure/MGAS/anti-ligature) are the
reference pattern — replicate it exactly.

**Task (full round-trip, mirror the pressure-log slice end to end):**
1. **Entity** `HealthcareWaterLog` in `Planscape.Core/Entities/` (tenant + project
   FKs, room ref (bim id + optional IFC GlobalId to match siblings), outlet id,
   flush type, temperature/duration if the screen captures them, captured-at/by),
   registered as a `DbSet` in `PlanscapeDbContext` with the same model config +
   indexes as `HealthcarePressureLog`.
2. **Migration** creating `HealthcareWaterLogs`, in the same documentation-DDL
   style as `20260515000000_HealthcarePack.cs` (no `[Migration]` attribute; the
   repo builds schema from the model). Order it after the existing healthcare
   migrations. Add the table to `PlanscapeDbContextModelSnapshot` if the repo's
   convention requires it (mirror how the sibling healthcare tables appear).
3. **Endpoints** in `HealthcareController`: `POST .../healthcare/water-log`
   (persist; broadcast a SignalR reading if that fits the pattern) and
   `GET .../healthcare/water-log` (filter by since/room, capped like siblings).
   Add the count to the healthcare dashboard DTO if the other logs are counted
   there.
4. **Mobile**: add `postWaterFlush()` to the API client, an `HC_WATER_FLUSH`
   offline action type, its replay handler in the offline queue (mirror
   `HC_PRESSURE_LOG`), and wire `water-flush.tsx` to call it (POST + enqueue-on-
   failure), exactly like `pressure-live.tsx`.
Build `Planscape.Server`.

**Acceptance.** Water-flush captures persist to the server, survive offline via the
queue, and appear in GET/dashboard — parity with the pressure-log slice.

## WS-4 — Regional HTM variant gating (flexibility)

**Verified gap.** `StingTools.Standards/HTM/HtmRegionalVariants.cs` defines real
England/Wales/Scotland/NI delta tables, but **no validator reads
`PRJ_ORG_HEALTH_HTM_REGION_TXT`** — every validator hard-codes NHS-England values.
Non-England projects silently get England rules.

**Task (sustainable — one resolution point, not per-validator hacks):**
- Resolve the active region once from `PRJ_ORG_HEALTH_HTM_REGION_TXT` on
  ProjectInformation (via the existing `HtmRegionalVariants.ParseRegion` /
  `GetForRegion`), defaulting to NHS-England when unset.
- Apply the regional delta where the standards lookups feed the validators — the
  cleanest home is inside `HTMStandards` (or a thin region-aware wrapper the
  validators already call, e.g. the ACH / pressure / pipe-class / hot-water
  lookups), so a validator asking for "min ACH for room X" transparently gets the
  region-adjusted value. Prefer routing through the existing lookup methods over
  copy-pasting region checks into `PressureRegimeValidator` / `MgasFlowValidator` /
  `WaterSafetyValidator`.
- Surface the active region in the relevant audit output so the user can see which
  code base was applied.
Preserve England values exactly when region is England/unset (no behaviour change
for existing projects).

**Acceptance.** Setting `PRJ_ORG_HEALTH_HTM_REGION_TXT` to SHTM/WHTM/NHS-NI shifts
the affected thresholds per `HtmRegionalVariants`; England/unset is unchanged; the
region is one resolution point, not N.

## WS-5 — USP 797/800 recertification escalation (automation, life-safety)

**Verified gap.** Design promised: `CLN_ENV_CERT_DUE_DT` within 30 days → warning,
overdue → blocking error, for rooms with `CLN_ROOM_CLASS_TXT` = pharmacy-cleanroom
(PH-CSP-797 / PH-CSP-800). Not implemented — `IoTStalenessValidator` checks device
staleness only and never reads `CLN_ENV_CERT_DUE_DT`.

**Task.** Implement the recert-due check. Put it where it belongs sustainably:
either extend `IoTStalenessValidator` (it already owns time-based escalation) or add
a small dedicated check gated through `HealthcareValidatorGate` like its siblings —
choose the one that keeps the pack's pattern. For each cleanroom room class, read
`CLN_ENV_CERT_DUE_DT`, and emit: Info/OK if > 30 days out, Warning if within 30
days, Error if overdue or missing. Use `USPStandards` for the 6-month cycle
constant and the cleanroom room-class set (route class comparisons through the
canonical `RoomClassCodes` resolver so spelling variants match). Wire it into
RunAll/Selected + a command + `WorkflowEngine.ResolveCommand` +
`StingCommandHandler` if you add a new validator.

**Acceptance.** A pharmacy cleanroom with an overdue/near-due
`CLN_ENV_CERT_DUE_DT` raises the correct severity; absent date flags as missing;
cleanroom detection is canonical-code-safe.

## WS-6 — Penetration sign-off offline resilience (automation, minor)

**Verified gap.** `Planscape/app/penetrations/signoff.tsx` PUTs the sign-off
directly with no offline-queue fallback, unlike the healthcare screens.

**Task.** Wrap the `putPenetrationSignoff()` call so a network failure enqueues a
`PEN_SIGNOFF` offline action (add the action type + replay handler mirroring
`HC_PRESSURE_LOG`) and shows the same "saved offline" affordance the healthcare
screens use.

**Acceptance.** Offline penetration sign-offs queue and replay on reconnect.

## WS-7 — Information hygiene: orphaned params, CEQ/COBie, FGI adoption

**Verified, right-sized.** The earlier "37 dead params" is overstated — several
(`CLN_NURSECALL_TYPE_TXT`, `CLN_HOIST_TRACK_BOOL`, `CLN_BARI_DESIGN_KG_NR`,
`CLN_FGI_REF_TXT`) are consumed by the RDS renderer via
`HEALTHCARE_RDS_FIELDMAP.json` (invisible to a `.cs` grep) — they are RDS-surfaced,
not dead. The **genuinely unconsumed** items are: the whole `CEQ_CLINICAL` group
(clinical-equipment decon / endoscope / GMDN / UMDNS / SFG20 codes), the
`PRJ_ORG_HEALTH_AE_*` assigned-engineer metadata, a few CLN fields
(`CLN_OCC_VISITOR_INT`, `CLN_RT60_TARGET_S_NR`), plus two standards tables with
zero callers (`FgiAdoptionTracker`, and the COBie clinical-equipment/SFG20
overlay).

**Task — clarify status; do not silently leave dead fields:**
1. First, **verify** each candidate is truly unconsumed by BOTH `.cs` code AND the
   JSON-driven consumers (`HEALTHCARE_RDS_FIELDMAP.json`, COBie CSVs) before
   declaring it dead — the RDS fieldmap is the trap.
2. For params that ARE genuinely orphaned, add a clearly-labelled section to
   `docs/ROADMAP.md` (e.g. "Healthcare — data-model-ahead-of-logic") listing them
   and the feature each is waiting on (CEQ cluster → clinical-equipment COBie
   handover; `PRJ_ORG_HEALTH_AE_*` → assigned-engineer compliance gate; FGI
   adoption tracker → US-jurisdiction escalation). This stops specifiers populating
   fields that no logic reads.
3. **Low-cost wins if quick:** if `FgiAdoptionTracker.ResolveSeverity` can be
   called from the existing FGI-related validator path with a modest change, wire
   it; otherwise ROADMAP it. Do **not** build the full COBie clinical-equipment
   export in this pass — flag it as a scoped future item in ROADMAP with the params
   it would consume.

**Acceptance.** Every genuinely-orphaned healthcare param is either wired or listed
in ROADMAP with its blocking feature; no false "dead" claim for RDS-surfaced params.

---

## Definition of done

- WS-1..WS-6 implemented (WS-7 = verify + wire-if-cheap + ROADMAP the rest); DO-NOT-TOUCH respected.
- `Planscape.Server` and `StingTools` built where touched; results reported.
- Any new shared param registered across all four data files with a deterministic GUID.
- `docs/CHANGELOG.md` phase block; `docs/ROADMAP.md` updated; `CLAUDE.md` caveat #6 corrected.
- Final summary: fixed vs deferred, new params + GUIDs, build status, residual follow-ups.
