# StingTools MCP v2 — Autonomous Implementation Brief

> **Hand this file to a coding agent as its complete instruction set.** It is self-contained:
> mission, grounded architecture, locked decisions, guardrails, a phased plan with
> per-phase acceptance criteria, an exact file map, the target tool catalogue, and a
> definition of done. Read it top-to-bottom before writing any code.

---

## 0. Mission (one paragraph)

Upgrade the existing in-process StingTools MCP server from a **fire-and-forget intent
dispatcher** (5 tools that only return "dispatched") into a **real, synchronous, domain
model interface** for AI agents. Revit 2027 now ships Autodesk's own *read-only* Public
MCP Server; StingTools must be the **complementary "hands + standards brain"** — the
server that *acts*: queries the model with structured read-back, then performs
ISO-19650 tagging, MEP engineering sizing, compliance validation, and BOQ/cost
operations that Autodesk's server cannot. The moat is StingTools' **domain** (ISO 19650,
BS 7671 / HTM, NRM2), not tool count. Ship **~25 curated tools with typed read-back and
guardrailed writes**, not 763 raw command tags.

---

## 1. Locked decisions (do not re-litigate)

| Decision | Value | Rationale |
|---|---|---|
| **Scope** | MCP v2 server **only** | One bounded, verifiable deliverable. |
| **Build target** | **Revit 2025/26 on `net8.0-windows` first** | This machine + the GOLD deploy are 2025/26; MCP v2 does **not** need .NET 10. Port to 2027/net10 is a *separate* future brief. |
| **Write posture** | **Writes with guardrails** | Curated write/workflow tools, but every write is dry-run-capable, license-gated, transaction-rolled-back, and confirms destructive/bulk ops. |
| **Transport (now)** | **HTTP JSON-RPC** on `localhost:5199` (existing) + **shared-secret token** | Claude Code already supports the HTTP `url` form (`.mcp.json`). Keeps all Revit-API logic in-process where the API lives. |
| **Transport (stretch)** | stdio bridge exe for Claude Desktop parity | Phase 4, optional. Do **not** block core work on it. |

**Out of scope (do NOT do):** .NET 10 / Revit 2027 multi-target; rebuilding Autodesk's
generic read layer beyond what StingTools' domain needs; auto-dumping all 763 commands
via `McpToolDescriptorGenerator` (leave that `#if REVIT_2027` file untouched); External
Data API; tag-leader API; any new WPF UI. Those are other briefs.

---

## 2. Grounded architecture (verified against the real code — read these first)

| Fact | Location | Implication |
|---|---|---|
| MCP server is in-process, HTTP JSON-RPC 2.0, background `HttpListener` on `localhost:5199/mcp/`, started from `StingToolsApp.OnStartup` via `StartIfConfigured()` when `mcp_enabled=true` in `STING_LLM_CONFIG.json`. | `StingTools/Mcp/StingMcpServer.cs` | Extend this server; don't replace it. Registration/dispatch is a `switch` in `HandleToolCall`. |
| **The core gap:** tools call `StingDockPanel.DispatchCommand(tag)` which returns only `bool accepted` (ExternalEvent was queued) — **never the command's outcome.** | `StingMcpServer.cs:216`, `StingDockPanel.xaml.cs:180` | Read-back must be built. This is Phase 1. |
| A **synchronous** path already exists: `DispatchCommandSync(UIApplication app, tag, …)` runs on the API thread, bypassing `Raise()`. | `StingDockPanel.xaml.cs:194` | Model the new job bridge on this — but the MCP thread has **no** `UIApplication`, so it must marshal onto the API thread via a dedicated `ExternalEvent` + blocking wait (see §4). |
| **License hard-lock** is enforced *inside the command handler*: `if (!Core.Licensing.LicenseGate.IsLicensed && tag != "STING_Activate") return;` | `StingCommandHandler.cs:120` | Commands dispatched through the handler are already gated. **New query/write tools that touch the document directly (not via the handler) MUST re-check `LicenseGate.IsLicensed` themselves**, or they become a licensing bypass. Non-negotiable. |
| Existing 5 tools & their data sources: `run_command`, `nlp_query` → `NLPEngine.ProcessQuery`/`IntentPatterns`; `list_commands` → `NLPEngine.IntentPatterns`; `get_status` → `ComplianceScan.GetCached().StatusBarText`; `ask_bim` → `NLPEngine.SearchKnowledge` then `StingLlmService.Instance.AskBimQuestionAsync`. | `StingMcpServer.cs`, `McpToolRegistry.cs` | Keep all 5 working. Upgrade `run_command` with `dryRun` + read-back; keep the others. |
| JSON-RPC + MCP types (`JsonRpcRequest/Response`, `McpTool`, `McpContent`, `McpCallResult{content[],isError}`). Tool results return `content:[{type:"text",text}]`. | `StingTools/Mcp/McpTypes.cs` | Structured payloads ride as a fenced ```json block inside the text content (see §5). |
| Re-entrancy guard `_executeDepth`, param passing via `_extraParams` ConcurrentDictionary. | `StingCommandHandler.cs:39,48` | If a write tool needs to pass params to an existing command, set `_extraParams` **before** `SetCommand` (existing convention — respect it). |

---

## 3. House rules (StingTools conventions — enforce in every file you write)

- **`dotnet build` must stay green after every phase.** This machine can build:
  `dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`.
  A phase is not done until the build is green. **Run it; paste the result; never claim success without it.**
- Transactions: wrap all model mutation in `Transaction`/`TransactionGroup` named with a `STING ` prefix; roll back on any error. Use `[Transaction(TransactionMode.Manual)]` for state-changing commands, `ReadOnly` for queries.
- Logging: `StingLog.Info/Warn/Error` only — **no silent catch blocks**. Every `catch` either logs or returns a typed MCP error.
- User messages in commands use `TaskDialog`, never `MessageBox`. (MCP tools return text, not dialogs — see §4 "no modal" rule.)
- Reuse shared helpers (`ParameterHelpers`, `TagConfig`, `ComplianceScan`, the `Core/Validation` validators, the BOQ/MEP engines) — do not re-implement domain logic in the MCP layer. The MCP layer is a **thin adapter**.
- One class per file for the new bridge/tool classes; group closely-related simple handlers per file (existing pattern).
- **Git:** work on the current feature branch; **commit per phase** with a clear message; **do not merge to `main`**; end commit messages with the standard `Co-Authored-By` line. Note in each commit that it is **unverified in Revit** (Revit API only exercisable manually).

---

## 4. Core design — the read-back job bridge (Phase 1, highest risk)

The MCP handler runs on an HTTP `ThreadPool` thread with no Revit API access. Every tool
that touches the document must run its work on Revit's API thread and return the result
**synchronously** to the waiting HTTP thread.

**Build `StingTools/Mcp/McpJobBridge.cs`** implementing this pattern:

1. A dedicated `ExternalEvent` + `IExternalEventHandler` **separate** from `StingCommandHandler` (so MCP jobs never entangle the panel's `_executeDepth` re-entrancy state).
2. Public API callable from the MCP thread:
   ```csharp
   // Runs `job` on the Revit API thread, blocks up to timeoutMs, returns its result.
   static McpJobResult Run(Func<UIApplication, McpJobResult> job, int timeoutMs = 15000);
   ```
3. Mechanics:
   - Create a job record `{ Guid id, Func, McpJobResult result, ManualResetEventSlim done }`; enqueue on a `ConcurrentQueue`.
   - `Raise()` the MCP `ExternalEvent`. If it returns **not** `Accepted` → return a typed error `{code:"revit_busy", message:"Revit is in a modal dialog / transaction / sync — retry shortly."}` **without** blocking.
   - The handler (API thread) drains the queue, runs each `job.Func(uiApp)` inside `try/catch`, stores the result (or an `{code:"exception"}` result), signals `done`.
   - MCP thread waits `done.Wait(timeoutMs)`; on timeout return `{code:"timeout"}`; else return the stored result.
4. **Guards inside every job function** (helpers in `McpSafety.cs`):
   - `if (!LicenseGate.IsLicensed) return McpJobResult.Error("not_licensed", …);`
   - `if (uiApp.ActiveUIDocument?.Document == null) return McpJobResult.Error("no_document", …);`
   - **No modal UI.** Jobs must never open `TaskDialog`/`MessageBox` (would deadlock the API thread while the HTTP thread waits). If an underlying command shows a dialog, either call the engine directly (preferred) or document the tool as "fire-and-forget, no read-back" explicitly.

**Prove it end-to-end** before building more tools: implement `get_model_info`
(returns real `Document.Title`, path, `ProjectInformation`, active view name, phase) using
the bridge, and confirm via `curl` that a live Revit session returns the actual title
synchronously.

---

## 5. Result contract (typed read-back)

Add `StingTools/Mcp/McpResult.cs`:

- `McpJobResult { bool Ok; string Code; string Summary; object Data; }` (Data = any serialisable POCO/dict).
- Serialisation helper that turns an `McpJobResult` into an `McpCallResult`:
  - `content[0].text` = a short human summary **plus** a fenced block:
    ````
    <summary line>

    ```json
    { …Data… }
    ```
    ````
  - `isError = !Ok`.
- Rationale: agents parse the JSON block deterministically; humans reading the transcript get the summary. (If you later confirm the client supports MCP `structuredContent`, add it additively — but the fenced-JSON contract must remain.)

---

## 6. Target tool catalogue (~25 curated tools)

Keep the existing 5. Add the rest. **R** = read-only, **W** = write (guardrailed). All new
tools run via the job bridge and re-check the license gate. The agent MAY merge/rename
for coherence, but must cover every capability below.

### Existing (keep; upgrade where noted)
| Tool | R/W | Change |
|---|---|---|
| `run_command` | W | Add `dryRun`; route through bridge; return typed outcome instead of "dispatched". Curated allowlist only (reject tags not in an allowlist config). |
| `nlp_query` | W | Keep; add read-back of the executed command's outcome. |
| `list_commands` | R | Keep as-is. |
| `get_status` | R | Keep; becomes a thin alias of `get_compliance`. |
| `ask_bim` | R | Keep as-is. |

### New — read / query
| Tool | Args | Returns | Backed by |
|---|---|---|---|
| `get_model_info` | — | title, path, project info, active view, phase, discipline | `Document`/`ProjectInformation` |
| `query_elements` | `category, filter?, limit?` | element ids + key params + bbox | `FilteredElementCollector` |
| `get_element` | `id` | all params, category, family/type, location, bbox | `Element`/`ParameterHelpers` |
| `get_parameter` | `id, name` | value + storage type + shared/builtin | `ParameterHelpers` |
| `get_selection` | — | selected ids + category summary | `UIDocument.Selection` |
| `set_selection` | `ids` | count set | `Selection.SetElementIds` (non-destructive) |
| `list_views` | `filter?` | views by type/level | collector |
| `list_sheets` | `filter?` | sheets + numbers + names | collector |
| `get_compliance` | `byDiscipline?` | RAG %, tagged/untagged counts, top issues, per-disc breakdown | `ComplianceScan` |
| `get_tag_status` | `discipline?` | list of untagged/incomplete-tag element ids by discipline | `ComplianceScan` / `ISO19650Validator` |
| `run_validator` | `name` | structured findings (pass/warn/fail + element refs) | `Core/Validation/*`, `RunAllValidators` |

### New — domain write / workflow (all `dryRun`-capable, gated, rolled back)
| Tool | Args | Guardrail | Backed by |
|---|---|---|---|
| `set_parameter` | `ids, name, value, dryRun?, confirm?` | `confirm:true` required when `ids.Count > 25`; TransactionGroup rollback | `ParameterHelpers.SetString/…` |
| `auto_tag` | `scope(view\|selection\|project), dryRun?, confirm?` | `confirm` required for `project`; report count | `AutoTag`/`BatchTag` engines (call `TagPipelineHelper` directly, not the dialog) |
| `tag_scheme_render` | `dryRun?` | — | `TagScheme_Render` engine |
| `size_ducts` / `size_pipes` / `size_cables` | `scope, dryRun?` | report changed sizes | `MepAutoSize*`, `CableSizer` engines |
| `export_boq` | `format(xlsx\|csv)` | returns output file path | `BOQ_Export` engine |
| `generate_panel_schedules` | `dryRun?` | — | `Panel_BatchSchedules` |
| `run_workflow` | `name, dryRun?, confirm?` | per-step results; `confirm` required | `WorkflowEngine` |

> If any of the above only exists as a dialog-driven command (opens `TaskDialog`), call
> its **engine** directly inside the job. If no dialog-free path exists, expose it as a
> `run_command`-style fire-and-forget tool and **label it clearly** in the description as
> "no read-back". Do not fake a result.

---

## 7. Security & config

- Add to `StingTools/Data/STING_LLM_CONFIG.json`: `mcp_auth_token` (string), `mcp_tool_allowlist` (array of command tags `run_command` may execute), and keep `mcp_enabled`, `mcp_port`.
- Server: require header `X-Sting-Mcp-Token` to equal `mcp_auth_token` on every POST when the token is non-empty; return JSON-RPC error `-32001 unauthorized` otherwise. Keep the `localhost`-only binding. If token is empty/absent in config, log a `StingLog.Warn` that the server is unauthenticated.
- Every query/write tool: license gate (§4). `run_command`: allowlist check.

---

## 8. Phased plan (each phase independently build-verifiable)

| Phase | Deliverable | Acceptance criteria |
|---|---|---|
| **0 — Scaffolding** | `McpResult.cs`, `McpSafety.cs` (license gate + dryRun + TransactionGroup helper), auth-token check on server, config keys added. | Build green. Existing 5 tools unchanged and still answer. `GET /mcp/` lists tools. Bad token → `-32001`. |
| **1 — Read-back bridge** | `McpJobBridge.cs` + its `ExternalEvent`/handler, registered in `StingToolsApp.OnStartup` (alongside existing wireup). `get_model_info` proves it. | In a live Revit session, `curl` `tools/call get_model_info` returns the **real document title** synchronously. Revit-busy → typed `revit_busy`. No-doc → typed `no_document`. |
| **2 — Query suite** | All §6 read tools. | Each returns correct structured JSON for a test model. `query_elements` respects `limit`. License-off → `not_licensed`. Build green. |
| **3 — Write suite** | All §6 write tools with dry-run + confirm + rollback. | For each: `dryRun:true` reports intended change and mutates nothing; real run mutates and returns counts; forced error mid-op rolls back cleanly. `confirm` enforced on bulk/project ops. Build green. |
| **4 — stdio bridge (stretch)** | `StingTools.McpBridge/` console exe: MCP stdio ↔ HTTP forwarder + a `--emit-claude-config` that prints the Claude Desktop JSON. | Claude Desktop launches the exe and lists StingTools tools. *Optional — do not block 0–3 on this.* |
| **5 — Docs & samples** | `.mcp.json` sample, a `docs/` usage note, `docs/CHANGELOG.md` entry, an in-Revit smoke-test checklist for every tool. | CHANGELOG appended; sample config committed. |

Commit at the end of each phase. Do not merge.

---

## 9. File map

**New:**
- `StingTools/Mcp/McpJobBridge.cs` — API-thread job/read-back bridge + its ExternalEvent handler.
- `StingTools/Mcp/McpResult.cs` — `McpJobResult` + serialisation to `McpCallResult`.
- `StingTools/Mcp/McpSafety.cs` — license gate, dry-run, confirm, TransactionGroup rollback helpers.
- `StingTools/Mcp/McpQueryTools.cs` — read/query tool handlers.
- `StingTools/Mcp/McpWorkflowTools.cs` — domain write/workflow tool handlers.
- *(Phase 4)* `StingTools.McpBridge/` — stdio↔HTTP console project + Claude Desktop config emitter.
- *(Phase 5)* `docs/mcp/README.md`, `.mcp.json` sample.

**Modified:**
- `StingTools/Mcp/StingMcpServer.cs` — auth token; register new tools in `HandleToolCall`; upgrade `run_command`/`nlp_query` to read-back.
- `StingTools/Mcp/McpToolRegistry.cs` — add new `McpTool` definitions with precise descriptions + JSON input schemas.
- `StingTools/Data/STING_LLM_CONFIG.json` — new config keys.
- `StingTools/Core/StingToolsApp.cs` — register the `McpJobBridge` ExternalEvent at startup (only if not auto-created lazily).
- `docs/CHANGELOG.md` — new phase entry.

**Do NOT touch:** `Core/Mcp/McpToolDescriptorGenerator.cs` (the `#if REVIT_2027` file), any WPF panel XAML, `StingCommandHandler`'s existing switch (except reading `_extraParams` convention).

---

## 10. Verification

- **Build:** green `dotnet build` after every phase (mandatory, paste output).
- **Static MCP handshake test:** a PowerShell/`curl` script that POSTs `initialize` → `tools/list` → a `tools/call` for each tool, asserting shapes. Commit it under `docs/mcp/`. (Runs without Revit for the handshake/error paths; live tools need Revit open.)
- **In-Revit smoke test (manual, user-run):** a checklist in Phase 5 covering: server starts on project open; `get_model_info` returns real title; one query tool; one dry-run write; one real write with rollback proof; bad-token rejection; license-off rejection.
- Follow the repo's existing convention: commits carry the **"unverified in Revit"** caveat because the Revit API can only be exercised manually.

---

## 11. Definition of done

1. Phases 0–3 complete; Phase 5 docs written. (Phase 4 optional.)
2. `dotnet build` green; the MCP handshake script passes its no-Revit assertions.
3. All 5 original tools still function; ~20 new tools registered and individually spec-compliant.
4. Every query/write tool re-checks the license gate; every write is dry-run-capable, confirm-guarded on bulk/project, and TransactionGroup-rolled-back on error.
5. Auth token enforced; server localhost-bound.
6. `docs/CHANGELOG.md` entry added; `.mcp.json` sample + smoke-test checklist committed.
7. Work committed per-phase on the feature branch, **not merged**, with the Revit-unverified caveat.

---

## 12. When blocked / uncertainties

- **A capability only exists as a dialog command with no engine entry point:** expose it as fire-and-forget `run_command`-style, label "no read-back", and note it in CHANGELOG — do not invent a result.
- **`ExternalEvent` never returns `Accepted` in testing:** it requires an idle Revit UI thread; document the retry/timeout behaviour and move on — do not busy-loop.
- **A domain engine's public surface is unclear:** read the engine source (paths in `CLAUDE.md`), prefer the lowest-level dialog-free method, and if genuinely ambiguous, implement the tool as read-only for now and flag it in CHANGELOG for follow-up. Do not stall the whole phase on one tool.
- Keep a running `## Open questions` note in the PR/commit description for anything a human must confirm in Revit.
