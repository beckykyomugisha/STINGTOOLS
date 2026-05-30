# Phase B Round 4 — TAGGING tab redesign plan

Brief: `docs/UI_PHASE_B_PATTERNS.md`. TAGGING tab is lines 538-1271 of
`StingTools/UI/StingDockPanel.xaml` (after Phase A reorg). This tab is
the post-tagging manipulation surface — tag operations / leaders /
analysis / annotation color / tag appearance / tag-type swap +
several supporting sections that grew up around it (legends, colouriser,
view tag style, tag style engine, pattern learning, batch view
processing, etc.).

**Distinct from CREATE TAGS (Round 3)**: CREATE TAGS is token-writer /
smart-placement / legend-builder / ISO-compliance entry points. TAGGING
is the visual / manipulation / appearance / analysis layer. No section
in this plan overlaps with the CREATE TAGS audit doc; the only shared
area is the ISO/anomaly status strip near the bottom, which stays as
plain action chips here because the workflows route through totally
different commands.

## Section list + pattern decision

Sections walked top-to-bottom against the decision tree from
`UI_PHASE_B_PATTERNS.md`. Pattern 6 (short labels + ToolTips) is
applied universally on top of every other call.

| # | Section (line ≈) | Decision | Why |
|---|---|---|---|
| 1 | AI ORGANISE ENGINE (544) | Pattern 1 (chips) | 3 mode buttons (Quick / Deep / Anneal) are independent runners. Already concise. Keep primary SMART ORGANISE proud. |
| 2 | Tag family combo (572) | Leave | Already Pattern 3-shaped (combo + refresh). |
| 3 | DATA TAGGING (588) | Pattern 1 (chips) + keep primary | 4 primary green/purple/teal buttons proud + secondary row tightened. Tie-In + LPS sub-clusters already grouped — left as-is per "do not duplicate Round 3" and because tags here drive read-only / status writers, not pickers. |
| 4 | VISUAL TAG PLACEMENT (650) | Pattern 1 (chips) | 9 buttons, all independent actions. Just tighten labels. |
| 5 | TAG OPERATIONS (669) | Pattern 5 (Expander) | 11 buttons; primary TagSelected + DelTags stay proud; rest (Renumb / Smart Numbering / Clone / Apply Clone / Audit / AuditCSV / MultiView / Clashing / Orphans) fold into "More tag ops" Expander. |
| 6 | TAG7 RICH DISPLAY (687) | Pattern 1 (chips) | 4 buttons; just tighten. |
| 7 | TAG SEGMENT DISPLAY (699) | Pattern 1 (chips) | 2 buttons; leave structurally. |
| 8 | COLOR LEGENDS (707) | Pattern 5 (Expander) | 8 buttons, 1 primary (Create Legend) + 7 secondaries (Auto All / From View / HTML / To Sheet / Sheet Legend / All Sheets / Batch Per-Sheet). |
| 9 | TAG LEGENDS (722) | Pattern 5 (Expander) | 4 buttons, 1 primary (Tag Legend) + 3 secondaries. |
| 10 | MEP & ARCH LEGENDS (734) | Pattern 5 (Expander) | 6 buttons, 1 primary (★ Master) + 5 thematic legends inside. |
| 11 | VG / FILTER LEGENDS (747) | Pattern 1 (chips) | 4 buttons; all independent. |
| 12 | LEGEND INTELLIGENCE (758) | Pattern 5 (Expander) | 7 buttons, 1 primary (★ Flexible) + 6 secondaries (From Preset / Components / Color Ref / Sync Audit / Status / Worksets). |
| 13 | SYSTEM PARAM PUSH (772) | Pattern 1 (chips) | 3 buttons. |
| 14 | ORIENTATION & TEXT ALIGNMENT (782) | Pattern 2 (RadioButton ring) | Orientation has 3 mutually-exclusive global modes (H↔V toggle / All H / All V) + text alignment has 3 mutually-exclusive choices (L / C / R) — natural 2-radio-group fit. New runners `Tagging_OrientApply` + `Tagging_TextAlignApply` read radios and dispatch existing tags. FlipH / FlipV / SmartHV remain as proud action chips. |
| 15 | NUDGE (799) | Pattern 1 (chips) | Already directional + tight. |
| 16 | ALIGN & DISTRIBUTE (814) | Pattern 2 (RadioButton ring) | Align direction is a 6-way mutually-exclusive pick (←L / R→ / ↓T / B↑ / ←CH / |CV). One radio ring + single "Apply align" runner. Distribute and Arrange chips kept as Pattern 1. |
| 17 | LEADERS (845) | Pattern 5 (Expander) | 23 buttons across 4 sub-clusters (ADD/REMOVE/ELBOW/SNAP/LENGTH). Primary `+Leader` / `-Leader` / `Toggle` + `Auto-Align` stay proud; ELBOW + SNAP TAG POSITION + LENGTH fold into "Advanced leader ops" Expander to recover ~6 rows of vertical space. |
| 18 | AI/AUTO (886) | Pattern 1 (chips) | 3 brain buttons. |
| 19 | TAG APPEARANCE (897) | Pattern 5 (Expander) | 22 buttons across 4 rows. Primary `By Disc` + `Clear Colors` + `Apply Styles` stay proud; "Param-Driven" row + advanced style preset row + box/quick/line-weight row fold into "Advanced appearance" Expander. |
| 20 | ANALYSE (932) | Pattern 4 (CheckBox grid + run) | 12 independent audit flags (Score / Clashes / Crossings / Density / Clusters / Stats / By Discipline / Pin / Reset Pos / Disc Compliance / Workflow Trend / Linked Manifest). Natural multi-flag-pick. New `Tagging_AnalyseSuite` runner walks checkboxes and dispatches each. |
| 21 | TAG POSITION (954) | Pattern 2 (RadioButton ring) | 4 position presets (Pos1 Above / Pos2 Right / Pos3 Below / Pos4 Left) are mutually exclusive. Radio ring + `Tagging_PosApply` runner. Switch-Pos-Dialog + Align Bands + Cluster / Decluster / Export Pos remain proud chips. |
| 22 | PATTERN LEARNING (969) | Pattern 1 (chips) | 2 buttons. |
| 23 | BATCH VIEW PROCESSING (977) | Pattern 1 (chips) | 2 buttons, primary proud. |
| 24 | ROOM TAG POSITION SYNC (986) | Pattern 2 (RadioButton ring) | 3 mutually-exclusive room-tag anchor positions (Centroid / Top-Left / Top-Centre) + 2 leader-binding modes (Lock / Free). Two small radio rings + single "Apply" runner. Smaller-payoff but consistent. |
| 25 | LINKED MODEL ELEMENTS (998) | Pattern 1 (chips) | 4 buttons. |
| 26 | VIEW TAG STYLE (1010) | Pattern 1 (chips) | 2 buttons. |
| 27 | PARAMETER ANOMALY DETECTION (1019) | Leave | Already Pattern 3-shaped (combo + run + export). |
| 28 | AI CONTEXT-AWARE TAG PLACEMENT (1041) | Pattern 1 (chips) | 3 chips + options. |
| 29 | COLOURISER (1057) | Leave structurally | Already a rich data-driven section with combos + swatches. Just trim chip labels (Pattern 6). |
| 30 | TAG STYLE ENGINE (1221) | Pattern 5 (Expander) | 18 buttons across 6 SubLabel groups. Primary `Apply Tag Style` + `Apply Scheme` + `Clear Scheme` proud; rest (Box / Pattern Mode / System B Tier writers / By Variable / Set Depth / By Discipline / Style Report / Batch Scheme) fold into "Advanced tag style engine" Expander. |

## Cross-reference to Round 3 (CREATE TAGS)

Reviewed `docs/CREATE_TAGS_REDESIGN_PLAN.md`. Round 3 covers token
writers / smart-placement settings / ISO header / suite runners
`CreateTags_ScopeApply` + `CreateTags_OverwriteApply` — none of those
sections appear in the TAGGING tab line range (538-1271). Zero
overlap; this round only edits TAGGING-tab XAML.

## Code-behind additions

One new suite runner needed: `RunTaggingRunner(string)` in
`StingDockPanel.xaml.cs`, parallel to `RunCreateTagsRunner` /
`RunBimRunner` / etc. Handles four new dispatch tags introduced here:

| New tag | Wired by | Reads | Dispatches |
|---|---|---|---|
| `Tagging_OrientApply` | Pattern 2 orientation radio + Apply button | `rbOrient*` radio group (HV / AllH / AllV) | `ToggleTagOrientation` / `AllH` / `AllV` |
| `Tagging_TextAlignApply` | Pattern 2 text-align radio + Apply button | `rbTextAlign*` radio group (Left / Centre / Right) | `AlignTextLeft` / `AlignTextCenter` / `AlignTextRight` |
| `Tagging_AlignApply` | Pattern 2 align ring + Apply button | `rbAlign*` radio group (L/R/T/B/CH/CV) | `AlignLeft` / `AlignRight` / `AlignTop` / `AlignBottom` / `AlignCenterH` / `AlignCenterV` |
| `Tagging_PosApply` | Pattern 2 position ring + Apply button | `rbTagPos*` (1/2/3/4) | `SwitchTagPos1` / `SwitchTagPos2` / `SwitchTagPos3` / `SwitchTagPos4` |
| `Tagging_RoomTagApply` | Pattern 2 room-tag radio pair + Apply | `rbRoomTag*` + `rbRoomLeader*` | one of `RoomTagCentroid` / `RoomTagTopLeft` / `RoomTagTopCentre` PLUS one of `RoomTagLeaderLock` / `RoomTagLeaderFree` (sequential dispatch) |
| `Tagging_AnalyseSuite` | Pattern 4 checkbox grid + Run | `chkAnalyse*` flags | sequential dispatch of any subset of `AnalyseScore` / `AnalyseClashes` / `AnalyseCrossings` / `AnalyseDensity` / `AnalyseClusters` / `TagStats` / `SelectByDiscipline` / `PinTags` / `ResetTagPositions` / `DiscComplianceReport` / `WorkflowTrend` / `ExportLinkedManifest` |

Every dispatched concrete tag is one that ALREADY exists in
`StingCommandHandler` (verified against the existing case-switch in
the BIM/CREATE TAGS rounds — these are pre-existing tags used by the
buttons we're folding). No invented orphan tags. The new
`Tagging_*` runner tags are the only additions to the dispatch-tag
set and are routed via `RunTaggingRunner` before they ever reach
`StingCommandHandler`, mirroring the Round-1-3 pattern.

`OnRadioRouteChecked` is REUSED for the small "set discipline scope"
style radio groups where each radio carries a fully-formed dispatch
tag (orientation discipline preselection on radio-checked, etc.) —
but I expect to lean on the Apply-button + runner approach for all
five rings to keep the XAML+code-behind diff minimal and to match
Round 3's pattern.

## Hard rules compliance

- One commit per audit doc + one commit for XAML/code-behind.
- Verification gate (`grep -oE 'Tag="..."' | sort -u` superset) will
  be confirmed in the moves commit message.
- No `dotnet build` (Linux sandbox).
- Only TAGGING tab edited. No CREATE TAGS, no INTEROP, no SETUP, no
  BIM, no MODEL, no DOCS, no TAG STUDIO, no HEALTHCARE, no SELECT,
  no dedicated panels, no `StingButtonStyles.xaml`.
- All Phase A locks + Phase B Rounds 1/2/3 helpers preserved.
- Every new tag is wired in `RunTaggingRunner` (no unwired
  CREATE-TAGS-style hot-patch needed).
