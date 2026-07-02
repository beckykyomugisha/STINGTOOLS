# StingTools MCP v2 — Capability Exposure Architecture (addendum to MCP_V2_AGENT_BRIEF.md)

> Answers: "how do we let an agent drive most/all of StingTools' 1,580 commands without
> defining 1,580 tools?" Read alongside `docs/MCP_V2_AGENT_BRIEF.md`. This document is the
> design Phase 2+ implements.

---

## 1. Principle: reachability ≠ tool count

Defining one MCP tool per command is a trap — agent tool-selection accuracy collapses past
~40–50 tools and every definition burns context on every call. The goal is **reach the
whole command surface through a small, well-designed tool set.** We do that with three
tiers.

| Tier | Tools | Covers | Quality |
|---|---|---|---|
| **1 — Generic reads** | ~10 parameterized readers | ~95% of everything the model can *show* | First-class, always read-back |
| **2 — Curated domain actions** | ~25–35 hand-built verbs | The daily high-frequency workflows | First-class, dry-run + read-back |
| **3 — Capability meta-tools** | **3** (`search`/`describe`/`invoke`) | **All 1,580 commands**, discovered dynamically | Gradient — clean where a dialog-free engine path exists |

**Honest limit:** with Tiers 1–3 every command is *invokable*; the ones with dialog-free
engine entry points are *cleanly driven* with structured read-back. The gap between those
two is the ongoing "dialog→engine" work in §7.

---

## 2. Tier 1 — generic read tools

Reads generalize: one filterable reader replaces hundreds of hypothetical "find X" tools.

| Tool | Args | Returns |
|---|---|---|
| `get_model_info` | — | *(built — Phase 1)* doc/project/active-view |
| `query_elements` | `category?, paramFilters?, viewScope?, limit?, cursor?` | summarized element set: total count, per-category/level histogram, and a page of `{id, category, family, type, keyParams}` |
| `get_element` | `id` | all params, category, family/type, level, location, bbox |
| `get_parameter` | `id, name` | value + storage type + shared/builtin flag |
| `get_selection` | — | selected ids + category summary |
| `set_selection` | `ids` | count set (non-destructive) |
| `list_views` | `filter?, type?` | views by type/level |
| `list_sheets` | `filter?` | sheets + numbers + names |
| `get_schedule_data` | `name, limit?, cursor?` | schedule rows (paginated) |
| `get_compliance` | `byDiscipline?` | RAG %, tagged/untagged counts, top issues, per-disc breakdown |
| `run_validator` | `name` | structured findings (pass/warn/fail + element refs) |
| `get_tag_status` | `discipline?` | untagged / incomplete-tag element ids by discipline |

**`paramFilters` grammar** (keep simple, extensible): array of `{name, op, value}` where
`op ∈ {eq, ne, gt, lt, contains, empty, notEmpty}`. Example: find undersized ducts →
`query_elements({category:"Ducts", paramFilters:[{name:"Diameter", op:"lt", value:100}]})`.

---

## 3. Tier 2 — curated domain action tools (Phase 3)

Hand-built verbs for the daily workflows, each `dryRun`-capable, gated, rolled back:
`auto_tag`, `tag_scheme_render`, `size_ducts`, `size_pipes`, `size_cables`, `export_boq`,
`generate_panel_schedules`, `run_workflow`, `place_penetrations`, `renumber`,
`set_parameter`. (Full spec in the main brief §6.) These call **engines**, never dialog
commands (§7).

---

## 4. Tier 3 — the 3 capability meta-tools (the "expose everything" layer)

This is how the agent reaches all 1,580 commands without 1,580 definitions.

| Tool | R/W | Args | Returns | Backed by |
|---|---|---|---|---|
| `search_capabilities` | R | `query, limit?` | ranked `{tag, description, triggers, category, hasReadBack}` | `NLPEngine.IntentPatterns` (444 entries — **already ships**) |
| `describe_capability` | R | `tag` | `{tag, description, inputContract, opensUI, readOnly, engineBacked}` | catalogue metadata (§5) |
| `invoke_capability` | W | `tag, args?, dryRun?, confirm?` | typed read-back or, for UI commands, `{status:"dispatched_ui", note}` | `McpJobBridge` + allowlist + `WorkflowEngine.ResolveCommand` / `CommandRegistry` |

**The unlock you already own:** `NLPEngine.IntentPatterns` is a ready-made capability
index (tag + description + trigger phrases, offline). `search_capabilities` is a thin
wrapper over it. `WorkflowEngine.ResolveCommand` + `CommandRegistry` provide the dispatch
side. Tier 3 is mostly plumbing over existing assets.

**Agent flow:** `search_capabilities("panel schedule")` → picks a tag →
`describe_capability(tag)` (learns inputs + whether it opens UI) → `invoke_capability(tag,
args, dryRun:true)` → reviews → real invoke. The agent loads only the ~40 Tier 1+2 tool
defs plus whatever it discovers on demand.

---

## 5. Capability metadata (the catalogue)

`search`/`describe` need per-command facts beyond the NLP description. Build a lightweight
`McpCapabilityCatalogue` that, per command tag, resolves:
- `description`, `triggers` — from `NLPEngine.IntentPatterns`.
- `category` — from the tag namespace / module (Select/Docs/Tags/Electrical/…).
- `readOnly` — from the command's `[Transaction]` attribute (`ReadOnly` vs `Manual`) via reflection.
- `opensUI` — heuristic: annotate a curated set of known dialog/wizard tags as `opensUI:true`; default false. (Grow this list over time; it's the driver for §7.)
- `engineBacked` — whether a dialog-free engine path is wired into `invoke_capability` (starts small, grows).
- `inputContract` — for engine-backed tools, the JSON arg shape; for the rest, `"none / uses active selection+view"`.

Cache the catalogue per session. It is the single source `describe_capability` reads.

---

## 6. Cross-cutting rules (apply to every tool)

1. **Summarize + paginate by default.** Never return 8,000 rows. `query_elements` /
   `get_schedule_data` return counts + histogram + one `limit`-sized page + a `cursor`.
   Full rows only when explicitly asked. This protects the agent's context.
2. **Async for heavy ops (Phase 3).** `invoke_capability` may return `{jobId}` for
   long-running work; add `get_job_status(jobId)`. Keep Phase 2 reads under the 15s bridge
   timeout by summarizing.
3. **Typed error taxonomy** — reuse + extend: `no_document`, `revit_busy`, `timeout`,
   `not_licensed`, plus `needs_selection`, `not_found`, `opens_ui`, `not_allowed`
   (allowlist), `bad_args`. The agent recovers on codes, not prose.
4. **License gate inside every job** (`McpSafety.RequireLicense`) — the non-negotiable from
   the brief.
5. **Read-back always.** Real counts + element IDs. Ground the agent; never let it guess.
6. **Annotate** read-only vs destructive so MCP clients can auto-approve safe reads.

---

## 7. The quality lever: dialog → engine

The ceiling on "cleanly driven" is that many commands mix engine logic with WPF input.
StingTools already keeps most logic in `Core/*` engines — so the rule is: **`invoke_capability`
and every Tier 2 tool call the engine directly, never the dialog command.** For a command
that has no dialog-free path, `describe_capability` reports `opensUI:true`, and
`invoke_capability` runs it fire-and-forget returning `{status:"dispatched_ui"}` — clearly
labelled, never a faked result. Growing the `engineBacked` set over time is what moves
commands from "reachable" to "cleanly driven."

---

## 8. Protocol features beyond tools (opportunistic)

- **MCP Resources** — expose big read-only artefacts (compliance report, sheet register,
  BOQ export, validator run) as *resources* the agent attaches, keeping large data out of
  tool-call context.
- **MCP Prompts** — expose `WORKFLOW_*.json` presets ("Morning Health Check", "Weekly Data
  Drop") as named prompts so a whole curated chain fires by name.
- **Tool annotations** — `readOnlyHint` / `destructiveHint` on each tool.

These are additive; schedule after Tiers 1–3 work.

---

## 9. Phasing against this model

- **Phase 2 (next):** Tier 1 read tools + pagination helper + Tier 3 **read-only**
  discovery (`search_capabilities`, `describe_capability`) + `invoke_capability` limited to
  **read-only / dry-run** tags. This alone lets the agent *see* the whole model and
  *discover* the whole command surface safely.
- **Phase 3:** Tier 2 curated write verbs + full write `invoke_capability` (guardrails,
  async jobs) + the `engineBacked` expansion.
- **Phase 4+:** Resources, Prompts, annotations; stdio bridge (from the main brief).

---

## 10. Dialog→Engine Extraction Pattern (the reusable template)

§7's "call the engine, never the dialog" needs a repeatable recipe for turning a
UI-bound command into a headless, Document-taking engine that is the **single source of
truth**. First instance: **cable sizing** (`CableSizerApplyEngine`). Follow these 4 steps
for ducts / pipes / panels next.

**Step 1 — Is the pure calc already separable? → expose it in minutes.**
If the math is already a pure function (no Revit), wire it directly as a read-only MCP
tool. Cable sizing: `CableSizerEngine.Calculate(CableSizeInput) → CableSizeResult` was
already pure → exposed as `size_cable_calc` (license-gated, no job bridge, no document).

**Step 2 — Extract `Apply(Document doc, TScope scope, TAssumptions, bool dryRun) → TResult`.**
- `scope` is a **parameter** (enum/struct: Project | ActiveView | ElementIds) — NEVER a
  static UI field (no `CurrentScope`-style coupling). Selection is resolved to ElementIds
  by the *caller* (which has the `UIDocument`), so the engine stays `Document`-only.
- Map model params → the calc input; call the existing pure calc; on a **real run** write
  results back to bound params only (skip missing/read-only, record the reason).
- **No `TaskDialog`/`MessageBox` anywhere in the engine.** `dryRun` computes + returns the
  plan (per-item proposal + ≤25 samples) and writes nothing.
- Return a structured `{ inspected, sized/planned, skipped[reasons], errors[], sampleChanges[] }`.
- The engine opens **its own `Transaction`** (standalone-safe) — never a `TransactionGroup`,
  so it nests cleanly when MCP wraps it in `McpSafety.RunInTransactionGroup` (no double group).
- Define the **skipped contract** for inputs the model doesn't carry (e.g. cable run length
  from an undrawn circuit path → `skipped: missing circuit length`).

**Step 3 — Refactor the existing UI writer to DELEGATE (single source of truth).**
If a command already writes those params, change it to call `Apply(...)` and render its
`TaskDialog` from the returned result — identical button behaviour for UI users. If **no
writer exists** (cable sizing today: `CableSizerCommand` is `[ReadOnly]` and only displays
the calc — it never writes the model), the engine is net-new; state that and skip. Do not
duplicate sizing/apply logic in two places.

**Step 4 — Wire into MCP via the engine registry.**
Add one `McpEngineRegistry` handler keyed on an **existing catalogue tag** (so
`search_capabilities`/`invoke_capability` resolve it — cable sizing reused the shipped NLP
tag `ElecCableSize`). The handler adapts `args → scope/assumptions`, runs the `dryRun` plan
for the confirm count, applies the Phase-3a guardrails (confirm on bulk/project,
`RunInTransactionGroup`, structured read-back), and calls `Apply`. Add a thin named verb
(`size_cables`) that routes through `DispatchWrite(tag, args)`. Because `engineBacked`
derives from the registry, `invoke_capability` picks it up automatically — one handler,
both surfaces.

**Backlog remaining (same recipe):** `size_ducts` / `size_pipes` (extract `Apply` from the
inline `MepAutoSize*Command.Execute` + drop the `StingHvacCommandHandler.CurrentScope`
coupling), `generate_panel_schedules` (extract from `BatchPanelSchedulesCommand`),
`export_boq(xlsx)` (extract a `(boq, path)` workbook writer), `run_workflow` (needs a
non-modal, engine-backed-steps-only execution path first).
