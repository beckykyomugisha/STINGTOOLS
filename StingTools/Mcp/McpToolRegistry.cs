using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StingTools.Mcp
{
    // Defines the five MCP tools exposed to Claude / Claude Code.
    // Each tool maps to a handler in StingMcpServer.HandleToolCall().
    internal static class McpToolRegistry
    {
        internal static List<McpTool> GetTools() => new List<McpTool>
        {
            new McpTool
            {
                Name = "run_command",
                Description =
                    "Run any STING Tools Revit command by its exact command tag. " +
                    "Use list_commands to discover valid tags. " +
                    "Example tags: AutoTag, BatchTag, COBieExport, MorningHealthCheck, " +
                    "DrawingTypes_SyncStyles, Panel_BatchSchedules, Fabrication_GeneratePackage.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""tag"": {
                            ""type"": ""string"",
                            ""description"": ""The STING command tag to execute""
                        }
                    },
                    ""required"": [""tag""]
                }"),
            },

            new McpTool
            {
                Name = "nlp_query",
                Description =
                    "Execute a STING command using natural language. " +
                    "The built-in NLP engine (435 patterns, offline) finds the best match and runs it. " +
                    "Returns the matched command tag, confidence, and any alternatives. " +
                    "Example: 'tag all mechanical elements', 'run daily QA', " +
                    "'export COBie for handover', 'generate panel schedules'.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""text"": {
                            ""type"": ""string"",
                            ""description"": ""Natural language description of the action you want to perform in Revit""
                        }
                    },
                    ""required"": [""text""]
                }"),
            },

            new McpTool
            {
                Name = "list_commands",
                Description =
                    "List available STING Tools commands with descriptions. " +
                    "Optionally filter by keyword. Returns up to 60 matching commands. " +
                    "Use this to discover the right command tag before calling run_command.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""filter"": {
                            ""type"": ""string"",
                            ""description"": ""Optional keyword to filter by command name or description""
                        }
                    }
                }"),
            },

            new McpTool
            {
                Name = "get_status",
                Description =
                    "Get the current STING Tools model status: compliance percentage, " +
                    "tagged/untagged element counts, RAG rating, and top issues. " +
                    "Reads from the cached compliance scan — no Revit API call required.",
                InputSchema = JObject.Parse(@"{""type"": ""object"", ""properties"": {}}"),
            },

            new McpTool
            {
                Name = "ask_bim",
                Description =
                    "Ask a BIM, ISO 19650, or STING Tools knowledge question. " +
                    "Answered from the local 63-entry knowledge base first (instant, offline). " +
                    "Falls back to the configured LLM (Azure OpenAI or Claude) if no local match. " +
                    "Good for: ISO 19650 terms, COBie fields, RIBA stages, suitability codes, " +
                    "STING-specific features (TAG7, DrawingType, ViewStylePack, NLPEngine).",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""question"": {
                            ""type"": ""string"",
                            ""description"": ""Your BIM or standards question""
                        }
                    },
                    ""required"": [""question""]
                }"),
            },

            new McpTool
            {
                Name = "get_model_info",
                Description =
                    "Read structured information about the currently open Revit model, " +
                    "synchronously from the Revit API thread. Returns the document title and " +
                    "path, whether it is workshared, key Project Information fields " +
                    "(name, number, client, building, status, organization), and the active " +
                    "view (name, type, discipline, scale, detail level). " +
                    "Requires an active document and a valid STING licence; returns a typed " +
                    "error (no_document / not_licensed / revit_busy / timeout) otherwise. " +
                    "Takes no arguments.",
                InputSchema = JObject.Parse(@"{""type"": ""object"", ""properties"": {}}"),
            },

            // ── Phase 2 — Tier 1 generic read tools ─────────────────────────────
            new McpTool
            {
                Name = "query_elements",
                Description =
                    "Find and summarize model elements. Use this to answer 'how many / which' " +
                    "questions and to filter by parameter. Returns a SUMMARY (total count + " +
                    "per-category and per-level histograms) plus one paginated page of " +
                    "{id, category, family, type, keyParams} and a nextCursor — never a raw dump. " +
                    "category is a friendly name ('Ducts') or BuiltInCategory ('OST_DuctCurves'). " +
                    "viewScope 'active' restricts to the active view (default 'project'). " +
                    "paramFilters is an array of {name, op, value} where op is one of " +
                    "eq, ne, gt, lt, contains, empty, notEmpty (numeric compares use the " +
                    "parameter's displayed value in project units). " +
                    "Example: {category:'Ducts', paramFilters:[{name:'Diameter', op:'lt', value:100}], limit:50}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""category"":  { ""type"": ""string"", ""description"": ""Friendly category name or OST_ BuiltInCategory"" },
                        ""viewScope"": { ""type"": ""string"", ""description"": ""'project' (default) or 'active'"" },
                        ""paramFilters"": {
                            ""type"": ""array"",
                            ""description"": ""Array of {name, op, value}; op in eq|ne|gt|lt|contains|empty|notEmpty"",
                            ""items"": {
                                ""type"": ""object"",
                                ""properties"": {
                                    ""name"":  { ""type"": ""string"" },
                                    ""op"":    { ""type"": ""string"" },
                                    ""value"": {}
                                }
                            }
                        },
                        ""limit"":  { ""type"": ""integer"", ""description"": ""Page size (default 50, max 200)"" },
                        ""cursor"": { ""type"": ""string"", ""description"": ""nextCursor from a previous call"" }
                    }
                }"),
            },
            new McpTool
            {
                Name = "get_element",
                Description =
                    "Get the full detail of one element by id: category, family/type, name, level, " +
                    "location (point or curve endpoints, mm), bounding box (mm), and every " +
                    "parameter with its value. Use after query_elements/get_selection to inspect a " +
                    "specific element. Example: {id: 348122}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": { ""id"": { ""type"": ""integer"", ""description"": ""Element id"" } },
                    ""required"": [""id""]
                }"),
            },
            new McpTool
            {
                Name = "get_parameter",
                Description =
                    "Read one parameter of one element. Returns the value, storage type, and " +
                    "whether it is shared / built-in / read-only. Use when you need a single " +
                    "value rather than the whole element. Example: {id: 348122, name: 'Comments'}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""id"":   { ""type"": ""integer"", ""description"": ""Element id"" },
                        ""name"": { ""type"": ""string"",  ""description"": ""Parameter name"" }
                    },
                    ""required"": [""id"", ""name""]
                }"),
            },
            new McpTool
            {
                Name = "get_selection",
                Description =
                    "Get the user's current selection in Revit: element ids + a per-category count " +
                    "summary. Use to act on 'the selected elements'. Takes no arguments.",
                InputSchema = JObject.Parse(@"{""type"": ""object"", ""properties"": {}}"),
            },
            new McpTool
            {
                Name = "set_selection",
                Description =
                    "Set the Revit selection to the given element ids (non-destructive — changes " +
                    "only what is highlighted, mutates no model data). Returns the count set and any " +
                    "ids that were not found. Example: {ids: [348122, 348130]}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""ids"": { ""type"": ""array"", ""items"": { ""type"": ""integer"" }, ""description"": ""Element ids to select"" }
                    },
                    ""required"": [""ids""]
                }"),
            },
            new McpTool
            {
                Name = "list_views",
                Description =
                    "List project views (excluding view templates), grouped by view type, with " +
                    "id/name/type/scale. Optional filter (name contains) and type (e.g. 'FloorPlan', " +
                    "'Section', '3D'). Example: {type: 'FloorPlan', filter: 'Level 1'}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""filter"": { ""type"": ""string"", ""description"": ""Name-contains filter"" },
                        ""type"":   { ""type"": ""string"", ""description"": ""ViewType name filter"" }
                    }
                }"),
            },
            new McpTool
            {
                Name = "list_sheets",
                Description =
                    "List drawing sheets with id, sheet number, and name. Optional filter matches " +
                    "sheet number or name. Example: {filter: 'A-'}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": { ""filter"": { ""type"": ""string"", ""description"": ""Number/name contains filter"" } }
                }"),
            },
            new McpTool
            {
                Name = "get_schedule_data",
                Description =
                    "Read a Revit schedule's data by name: its field headers plus a paginated page " +
                    "of body rows (never the whole table at once). Returns totalRows + nextCursor. " +
                    "Example: {name: 'Door Schedule', limit: 50}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""name"":   { ""type"": ""string"",  ""description"": ""Exact schedule name"" },
                        ""limit"":  { ""type"": ""integer"", ""description"": ""Rows per page (default 50, max 200)"" },
                        ""cursor"": { ""type"": ""string"",  ""description"": ""nextCursor from a previous call"" }
                    },
                    ""required"": [""name""]
                }"),
            },
            new McpTool
            {
                Name = "get_compliance",
                Description =
                    "Get the STING ISO 19650 tagging-compliance scan: RAG status, total/tagged/" +
                    "untagged counts, compliance %, strict %, revision %, stale count, and top issue " +
                    "types. Pass byDiscipline:true for a per-discipline breakdown. Richer, structured " +
                    "form of get_status. Example: {byDiscipline: true}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": { ""byDiscipline"": { ""type"": ""boolean"", ""description"": ""Include per-discipline breakdown"" } }
                }"),
            },
            new McpTool
            {
                Name = "get_tag_status",
                Description =
                    "List untagged and incomplete-tag elements by discipline, with capped element-id " +
                    "lists per bucket (to protect context). Use to find exactly what still needs " +
                    "tagging. Optional discipline filter (e.g. 'M', 'E', 'P'). Example: {discipline: 'M'}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": { ""discipline"": { ""type"": ""string"", ""description"": ""Discipline code filter"" } }
                }"),
            },
            new McpTool
            {
                Name = "run_validator",
                Description =
                    "Run a STING engineering validator and return structured findings (verdict " +
                    "PASS/WARN/FAIL, counts by severity, top findings with element ids + codes). " +
                    "Valid names: connectivity, fill, spec, slope, termination, clearance, separation. " +
                    "Call with no name to see the list. Example: {name: 'fill'}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": { ""name"": { ""type"": ""string"", ""description"": ""Validator name"" } }
                }"),
            },

            // ── Phase 2 — Tier 3 read-only discovery meta-tools ─────────────────
            new McpTool
            {
                Name = "search_capabilities",
                Description =
                    "Search STING's full command catalogue (444 capabilities) by free text — the way " +
                    "to discover any of the ~1,580 commands without a tool per command. Returns ranked " +
                    "{tag, description, triggers, category, readOnly, opensUI}. Then use " +
                    "describe_capability for detail and invoke_capability to run read-only ones. " +
                    "Examples: 'panel schedule', 'tag mechanical', 'voltage drop'.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""query"": { ""type"": ""string"",  ""description"": ""Free-text query"" },
                        ""limit"": { ""type"": ""integer"", ""description"": ""Max results (default 15, max 50)"" }
                    },
                    ""required"": [""query""]
                }"),
            },
            new McpTool
            {
                Name = "describe_capability",
                Description =
                    "Get the full catalogue record for one command tag: description, trigger phrases, " +
                    "category, readOnly flag (null when unresolved), opensUI, engineBacked, and input " +
                    "contract. Use before invoke_capability to learn a command's inputs and whether it " +
                    "opens UI. Example: {tag: 'Panel_BatchSchedules'}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": { ""tag"": { ""type"": ""string"", ""description"": ""Command tag"" } },
                    ""required"": [""tag""]
                }"),
            },
            new McpTool
            {
                Name = "invoke_capability",
                Description =
                    "Invoke a discovered command by tag. Engine-backed tags (see describe_capability " +
                    "engineBacked:true — currently AutoTag/BatchTag) EXECUTE with full guardrails: dryRun " +
                    "returns a real plan; a real run mutates inside a rolled-back TransactionGroup with " +
                    "confirm required for bulk/project scope; project scope runs async (returns a jobId — " +
                    "poll get_job_status). Non-engine-backed write tags return 'not_allowed'; dialog/wizard " +
                    "tags return 'opens_ui' (never dispatched). Engine-backed writes also respect " +
                    "mcp_tool_allowlist. Put command args under 'args'. " +
                    "Example: {tag:'AutoTag', args:{scope:'view'}, dryRun:true}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""tag"":     { ""type"": ""string"",  ""description"": ""Command tag to invoke"" },
                        ""args"":    { ""type"": ""object"",  ""description"": ""Engine args (e.g. scope, mode) for engine-backed tags"" },
                        ""dryRun"":  { ""type"": ""boolean"", ""description"": ""Preview only; execute nothing"" },
                        ""confirm"": { ""type"": ""boolean"", ""description"": ""Required for bulk (>25) / project-scope writes"" }
                    },
                    ""required"": [""tag""]
                }"),
            },

            // ── Phase 3a — guarded WRITE verbs + async job polling ──────────────
            new McpTool
            {
                Name = "set_parameter",
                Description =
                    "WRITE. Set one parameter to one value on a set of elements. Storage-type aware " +
                    "(String/Integer/Yes-No/Double/ElementId); numeric (Double) values are interpreted in " +
                    "PROJECT DISPLAY UNITS (e.g. '100' on a mm length = 100 mm). Read-only params, missing " +
                    "params, and elements locked by other users are skipped and reported. dryRun:true returns " +
                    "a plan and mutates nothing. confirm:true is REQUIRED when ids has more than 25 entries. " +
                    "All changes run inside a rolled-back transaction; returns {changed, skipped, errors, sampleIds}. " +
                    "Example: {ids:[348122,348130], name:'Comments', value:'MCP set', dryRun:true}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""ids"":     { ""type"": ""array"", ""items"": { ""type"": ""integer"" }, ""description"": ""Element ids to write"" },
                        ""name"":    { ""type"": ""string"",  ""description"": ""Parameter name"" },
                        ""value"":   { ""description"": ""New value (string/number/bool); Double values are in project display units"" },
                        ""dryRun"":  { ""type"": ""boolean"", ""description"": ""Preview only; execute nothing"" },
                        ""confirm"": { ""type"": ""boolean"", ""description"": ""Required when ids.length > 25"" }
                    },
                    ""required"": [""ids"", ""name"", ""value""]
                }"),
            },
            new McpTool
            {
                Name = "auto_tag",
                Description =
                    "WRITE. Run the STING ISO 19650 tagging pipeline over a scope. scope: 'selection' or " +
                    "'view' run synchronously and return read-back {changed, skipped, errors, sampleIds}; " +
                    "'project' runs asynchronously and returns {jobId, status:'running'} — poll get_job_status. " +
                    "mode: 'skip' (default, tag untagged only), 'overwrite', or 'increment'. dryRun:true returns " +
                    "a plan (would-change counts + sample ids) and mutates nothing. confirm:true is REQUIRED for " +
                    "scope=project and for any scoped run affecting more than 25 elements. All mutation runs " +
                    "inside a rolled-back transaction. Example: {scope:'view', dryRun:true}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""scope"":   { ""type"": ""string"",  ""description"": ""selection | view | project"" },
                        ""mode"":    { ""type"": ""string"",  ""description"": ""skip (default) | overwrite | increment"" },
                        ""dryRun"":  { ""type"": ""boolean"", ""description"": ""Preview only; execute nothing"" },
                        ""confirm"": { ""type"": ""boolean"", ""description"": ""Required for project scope or >25 elements"" }
                    }
                }"),
            },
            new McpTool
            {
                Name = "get_job_status",
                Description =
                    "Poll an asynchronous MCP job (e.g. a project-scope auto_tag, tag_scheme_render, or " +
                    "export_boq). Returns the completed read-back once done, {status:'running'} while in " +
                    "progress, or 'not_found' for an unknown/expired jobId. Example: {jobId:'a1b2c3…'}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": { ""jobId"": { ""type"": ""string"", ""description"": ""jobId returned by an async write"" } },
                    ""required"": [""jobId""]
                }"),
            },

            // ── Phase 3b — remaining engine-backed WRITE verbs ──────────────────
            new McpTool
            {
                Name = "tag_scheme_render",
                Description =
                    "WRITE. Render the project's enabled tag schemes onto elements, writing each scheme's " +
                    "string to its target parameter (TagSchemeEngine). scope: 'selection' or 'view' run " +
                    "synchronously and return {changed, skipped, errors, sampleIds}; 'project' runs " +
                    "asynchronously and returns {jobId, status:'running'} — poll get_job_status. dryRun:true " +
                    "returns a would-change plan and mutates nothing. confirm:true is REQUIRED for scope=project " +
                    "or any scoped run affecting more than 25 elements. No enabled schemes → reports 0 changes. " +
                    "All mutation runs inside a rolled-back transaction. Example: {scope:'view', dryRun:true}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""scope"":   { ""type"": ""string"",  ""description"": ""selection | view | project"" },
                        ""dryRun"":  { ""type"": ""boolean"", ""description"": ""Preview only; execute nothing"" },
                        ""confirm"": { ""type"": ""boolean"", ""description"": ""Required for project scope or >25 elements"" }
                    }
                }"),
            },
            new McpTool
            {
                Name = "export_boq",
                Description =
                    "Produce a Bill of Quantities export FILE from the live model (does not modify the model). " +
                    "format:'csv' builds the BOQ (BOQCostManager) and writes a flat cost CSV, returning the " +
                    "output file path. This runs asynchronously (BOQ builds can be heavy) and returns " +
                    "{jobId, status:'running'} — poll get_job_status for {path, lines, format}. " +
                    "format:'xlsx' is not yet available dialog-free (returns no_engine_path). " +
                    "Requires an active document and licence. Example: {format:'csv'}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": { ""format"": { ""type"": ""string"", ""description"": ""csv (xlsx not yet supported dialog-free)"" } }
                }"),
            },

            // ── Dialog→engine: cable sizing ─────────────────────────────────────
            new McpTool
            {
                Name = "size_cable_calc",
                Description =
                    "READ-ONLY. Pure BS 7671 / NEC cable-sizing calculator — no Revit model needed. Given a " +
                    "single circuit's electrical inputs, returns the recommended conductor size, design current, " +
                    "voltage drop %, VD compliance, and the next standard breaker. loadKW and lengthM are " +
                    "required. Numeric inputs are in engineering units (kW, V, m, °C, mm²). " +
                    "Example: {loadKW:7.2, voltageV:230, lengthM:35, phases:1, standard:'BS7671'}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""loadKW"":         { ""type"": ""number"",  ""description"": ""Load in kW (required)"" },
                        ""voltageV"":       { ""type"": ""number"",  ""description"": ""System voltage (default 230)"" },
                        ""powerFactor"":    { ""type"": ""number"",  ""description"": ""Power factor (default 0.85)"" },
                        ""lengthM"":        { ""type"": ""number"",  ""description"": ""Run length in metres (required)"" },
                        ""installMethod"":  { ""type"": ""string"",  ""description"": ""BS 7671 method A1/B1/C/E/F… (default C)"" },
                        ""material"":       { ""type"": ""string"",  ""description"": ""Cu | Al (default Cu)"" },
                        ""insulation"":     { ""type"": ""string"",  ""description"": ""PVC70 | XLPE90 | LSOH90 | THWN90 (default XLPE90)"" },
                        ""vdLimitPct"":     { ""type"": ""number"",  ""description"": ""Max voltage drop % (default 3)"" },
                        ""standard"":       { ""type"": ""string"",  ""description"": ""BS7671 | NEC | IEC60364 (default BS7671)"" },
                        ""phases"":         { ""type"": ""integer"", ""description"": ""1 or 3 (default 1)"" },
                        ""ambientTempC"":   { ""type"": ""number"",  ""description"": ""Ambient °C (default 30)"" },
                        ""continuousLoad"": { ""type"": ""boolean"", ""description"": ""NEC continuous-load flag (default false)"" }
                    },
                    ""required"": [""loadKW"", ""lengthM""]
                }"),
            },
            new McpTool
            {
                Name = "size_cables",
                Description =
                    "WRITE. Size cables for the model's electrical power circuits (CableSizerApplyEngine). Inputs " +
                    "are READ from each circuit (apparent load, voltage, poles, length); results are WRITTEN to the " +
                    "circuit's connected equipment/fixtures as ELC_CBL_SZ_MM (cable size text) and ELC_VLT_DROP_PCT " +
                    "(voltage-drop % text) — the params actually bound to those categories. Numeric CSA/VD params " +
                    "(ELC_WIRE_CSA_MM2_NUM / ELC_WIRE_VD_PCT_NUM) are bound to no writable target and are reported " +
                    "as requiredBindingGaps, not written. install method / material / insulation / standard / VD " +
                    "limit are design ASSUMPTIONS you may pass (BS7671 / C / Cu / XLPE90 defaults). Circuits missing " +
                    "load or length are skipped (reported). scope: 'selection' (selected circuits or equipment→their " +
                    "circuits) or 'view' run synchronously; 'project' runs asynchronously (returns {jobId} — poll " +
                    "get_job_status). Read-back reports computed vs written + perParamWritten; if computed>0 but " +
                    "written==0 it flags noWritesPersisted (never a silent success). dryRun:true returns a per-circuit " +
                    "plan and mutates nothing. confirm:true is REQUIRED for scope=project or >25 circuits. " +
                    "Example: {scope:'view', standard:'BS7671', dryRun:true}.",
                InputSchema = JObject.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""scope"":         { ""type"": ""string"",  ""description"": ""selection | view | project"" },
                        ""installMethod"": { ""type"": ""string"",  ""description"": ""Design assumption (default C)"" },
                        ""material"":      { ""type"": ""string"",  ""description"": ""Cu | Al (default Cu)"" },
                        ""insulation"":    { ""type"": ""string"",  ""description"": ""Default XLPE90"" },
                        ""vdLimitPct"":    { ""type"": ""number"",  ""description"": ""Max VD % (default 3)"" },
                        ""standard"":      { ""type"": ""string"",  ""description"": ""BS7671 | NEC | IEC60364 (default BS7671)"" },
                        ""ambientTempC"":  { ""type"": ""number"",  ""description"": ""Ambient °C (default 30)"" },
                        ""dryRun"":        { ""type"": ""boolean"", ""description"": ""Preview only; execute nothing"" },
                        ""confirm"":       { ""type"": ""boolean"", ""description"": ""Required for project scope or >25 circuits"" }
                    }
                }"),
            },
        };
    }
}
