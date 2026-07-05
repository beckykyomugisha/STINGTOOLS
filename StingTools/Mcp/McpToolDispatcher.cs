// ════════════════════════════════════════════════════════════════════════════
// McpToolDispatcher — the SINGLE tool-execution path for all 27 MCP tools
//
// Both entry points share this one switch:
//   • StingMcpServer.HandleToolCall  (the HTTP JSON-RPC MCP server)
//   • StingLlmService.RunCopilotTurnAsync  (the in-Revit Copilot chat panel)
//
// Dispatch(name, args) maps a tool name to its handler and returns the wire-level
// McpCallResult. Model-touching tools marshal onto the Revit API thread inside
// their own handler (via McpJobBridge), so Dispatch is safe to call from any
// background thread. Factored out of StingMcpServer so there is exactly ONE place
// a tool is executed — no duplication between the MCP server and the Copilot.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Tags;
using StingTools.UI;

namespace StingTools.Mcp
{
    internal static class McpToolDispatcher
    {
        /// <summary>
        /// Execute one MCP tool by name and return its wire-level result. This is the
        /// single dispatch surface shared by the MCP HTTP server and the in-Revit
        /// Copilot. Unknown tool names return an error McpCallResult (never throws).
        /// </summary>
        internal static McpCallResult Dispatch(string toolName, JObject args)
        {
            args = args ?? new JObject();

            switch (toolName)
            {
                case "run_command":   return ToolRunCommand(args);
                case "nlp_query":     return ToolNlpQuery(args);
                case "list_commands": return ToolListCommands(args);
                case "get_status":    return ToolGetStatus();
                case "ask_bim":       return ToolAskBim(args);
                case "get_model_info": return ToolGetModelInfo();

                // ── Phase 2 — Tier 1 generic read tools ─────────────────────────
                case "query_elements":    return McpQueryTools.QueryElements(args);
                case "get_element":       return McpQueryTools.GetElement(args);
                case "get_parameter":     return McpQueryTools.GetParameter(args);
                case "get_selection":     return McpQueryTools.GetSelection();
                case "set_selection":     return McpQueryTools.SetSelection(args);
                case "list_views":        return McpQueryTools.ListViews(args);
                case "list_sheets":       return McpQueryTools.ListSheets(args);
                case "get_schedule_data": return McpQueryTools.GetScheduleData(args);
                case "get_compliance":    return McpQueryTools.GetCompliance(args);
                case "get_tag_status":    return McpQueryTools.GetTagStatus(args);
                case "run_validator":     return McpQueryTools.RunValidator(args);

                // ── Phase 2 — Tier 3 read-only discovery meta-tools ─────────────
                case "search_capabilities":   return McpDiscoveryTools.SearchCapabilities(args);
                case "describe_capability":   return McpDiscoveryTools.DescribeCapability(args);
                case "invoke_capability":     return McpDiscoveryTools.InvokeCapability(args);

                // ── Phase 3a — guarded write verbs + async job polling ──────────
                case "set_parameter":   return McpWriteTools.SetParameter(args);
                case "auto_tag":        return McpWriteTools.AutoTag(args);
                case "get_job_status":  return McpWriteTools.GetJobStatus(args);

                // ── Phase 3b — remaining engine-backed write verbs ──────────────
                case "tag_scheme_render": return McpWriteTools.TagSchemeRender(args);
                case "export_boq":        return McpWriteTools.ExportBoq(args);

                // ── Dialog→engine: cable sizing ─────────────────────────────────
                case "size_cable_calc":   return McpQueryTools.SizeCableCalc(args);
                case "size_cables":       return McpWriteTools.SizeCables(args);

                // ── Dialog→engine: MEP sizing + panel schedules ────────────────
                case "size_ducts":            return McpWriteTools.SizeDucts(args);
                case "size_pipes":            return McpWriteTools.SizePipes(args);
                case "build_panel_schedules": return McpWriteTools.BuildPanelSchedules(args);

                // ── Create stage — read tools ──────────────────────────────────
                case "get_rooms":    return McpQueryTools.GetRooms(args);
                case "get_levels":   return McpQueryTools.GetLevels();
                case "get_grids":    return McpQueryTools.GetGrids();
                case "get_warnings": return McpQueryTools.GetWarnings(args);

                // ── Create stage — ModelEngine creation tools (WRITE) ───────────
                case "create_wall":         return McpCreateTools.CreateWall(args);
                case "create_floor":        return McpCreateTools.CreateFloor(args);
                case "create_floor_in_room": return McpCreateTools.CreateFloorInRoom(args);
                case "create_roof":         return McpCreateTools.CreateRoof(args);
                case "create_duct":         return McpCreateTools.CreateDuct(args);
                case "create_pipe":         return McpCreateTools.CreatePipe(args);
                case "create_room":         return McpCreateTools.CreateRoom(args);
                case "place_family":        return McpCreateTools.PlaceFamily(args);
                case "building_shell":      return McpCreateTools.BuildingShell(args);

                default:
                    return Err($"Unknown tool: {toolName}. Call tools/list to see available tools.");
            }
        }

        // ── Tool: run_command ────────────────────────────────────────────────────

        private static McpCallResult ToolRunCommand(JObject args)
        {
            string tag = args["tag"]?.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(tag))
                return Err("Missing required argument: tag");

            // Validate against the NLP catalogue so unknown tags are caught before dispatch
            bool known = NLPEngine.IntentPatterns
                .Any(p => string.Equals(p.CommandTag, tag, StringComparison.OrdinalIgnoreCase));
            if (!known)
                return Err($"'{tag}' is not a recognised STING command tag. " +
                           "Call list_commands (with a filter) to find the right tag.");

            bool accepted = StingDockPanel.DispatchCommand(tag);
            return accepted
                ? Ok($"Command '{tag}' dispatched to Revit. " +
                     "The command is now executing — check the Revit window for results and dialogs.")
                : Err("Revit ExternalEvent was not accepted. " +
                      "Revit may be in a modal dialog, a transaction, or mid-sync. Try again shortly.");
        }

        // ── Tool: nlp_query ──────────────────────────────────────────────────────

        private static McpCallResult ToolNlpQuery(JObject args)
        {
            string text = args["text"]?.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(text))
                return Err("Missing required argument: text");

            var results = NLPEngine.ProcessQuery(text);
            if (results.Count == 0)
                return Err($"No STING command matched \"{text}\". " +
                           "Try list_commands to browse, or rephrase using BIM/Revit terminology.");

            string tag  = results[0].CommandTag;
            double conf = results[0].Confidence;

            var alts = results.Skip(1).Take(3)
                .Select(r => $"{r.CommandTag} ({r.Confidence:P0})")
                .ToList();
            string altText = alts.Count > 0
                ? $"\nAlternatives: {string.Join(", ", alts)}"
                : string.Empty;

            bool accepted = StingDockPanel.DispatchCommand(tag);
            return accepted
                ? Ok($"Matched \"{text}\" -> {tag} ({conf:P0} confidence). " +
                     $"Command dispatched to Revit.{altText}")
                : Err($"Matched '{tag}' but Revit ExternalEvent was not accepted. " +
                      "Revit may be busy — try again shortly.");
        }

        // ── Tool: list_commands ──────────────────────────────────────────────────

        private static McpCallResult ToolListCommands(JObject args)
        {
            string filter = args["filter"]?.Value<string>()?.ToLower() ?? string.Empty;

            var commands = NLPEngine.IntentPatterns
                .GroupBy(p => p.CommandTag, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Where(p => string.IsNullOrEmpty(filter) ||
                            p.CommandTag.ToLower().Contains(filter) ||
                            p.Description.ToLower().Contains(filter))
                .OrderBy(p => p.CommandTag)
                .Take(60)
                .Select(p => $"{p.CommandTag}: {p.Description}")
                .ToList();

            return commands.Count > 0
                ? Ok($"{commands.Count} command(s) found:\n\n" + string.Join("\n", commands))
                : Ok($"No commands matched filter \"{filter}\". Try a broader keyword.");
        }

        // ── Tool: get_status ─────────────────────────────────────────────────────

        private static McpCallResult ToolGetStatus()
        {
            var scan = ComplianceScan.GetCached();
            if (scan == null)
                return Ok("No compliance scan cached yet. " +
                          "Open a Revit project and run MorningHealthCheck to populate status.");

            return Ok($"STING Model Status\n{scan.StatusBarText}");
        }

        // ── Tool: ask_bim ────────────────────────────────────────────────────────

        private static McpCallResult ToolAskBim(JObject args)
        {
            string question = args["question"]?.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(question))
                return Err("Missing required argument: question");

            // Local knowledge base first (instant, offline, no credentials)
            var matches = NLPEngine.SearchKnowledge(question);
            if (matches != null && matches.Count > 0)
            {
                string answer = string.Join("\n\n",
                    matches.Select(m => $"{m.Term}:\n{m.Definition}"));
                return Ok($"[Local BIM Knowledge Base]\n\n{answer}");
            }

            // LLM fallback (requires credentials + mcp_enabled)
            try
            {
                var task = StingLlmService.Instance.AskBimQuestionAsync(question);
                if (task.Wait(8_000) && task.Result != null)
                    return Ok(task.Result);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MCP ask_bim LLM fallback failed: {ex.Message}");
            }

            return Ok("No local match found. Try asking about: ISO 19650, COBie, IFC, BEP, " +
                      "CDE, MIDP, TIDP, RIBA stages, suitability codes, TAG7, DrawingType, " +
                      "ViewStylePack, NLPEngine, or any of the 63 entries in the BIM KB.");
        }

        // ── Tool: get_model_info (read-back via the job bridge) ───────────────────

        private static McpCallResult ToolGetModelInfo()
        {
            // Marshal onto the Revit API thread and read synchronously. The job
            // re-checks the license gate + open document (McpSafety) and opens no
            // modal UI, so it cannot deadlock the waiting caller thread.
            McpJobResult r = McpJobBridge.Run(uiApp =>
            {
                var lic = McpSafety.RequireLicense();
                if (lic != null) return lic;
                var docErr = McpSafety.RequireDocument(uiApp);
                if (docErr != null) return docErr;

                Document doc = uiApp.ActiveUIDocument.Document;
                ProjectInfo pi = doc.ProjectInformation;

                string path = string.IsNullOrEmpty(doc.PathName) ? "(unsaved)" : doc.PathName;

                var projectInfo = new Dictionary<string, string>
                {
                    ["name"]         = SafeStr(() => pi?.Name),
                    ["number"]       = SafeStr(() => pi?.Number),
                    ["client"]       = SafeStr(() => pi?.ClientName),
                    ["buildingName"] = SafeStr(() => pi?.BuildingName),
                    ["status"]       = SafeStr(() => pi?.Status),
                    ["organization"] = SafeStr(() => pi?.OrganizationName),
                };

                object activeView = null;
                string viewName = "(none)";
                View v = uiApp.ActiveUIDocument.ActiveView;
                if (v != null)
                {
                    viewName = v.Name ?? "(unnamed)";
                    activeView = new Dictionary<string, string>
                    {
                        ["name"]        = viewName,
                        ["type"]        = v.ViewType.ToString(),
                        ["discipline"]  = ResolveDiscipline(v),
                        ["scale"]       = SafeStr(() => v.Scale.ToString()),
                        ["detailLevel"] = SafeStr(() => v.DetailLevel.ToString()),
                    };
                }

                var data = new Dictionary<string, object>
                {
                    ["title"]        = doc.Title,
                    ["path"]         = path,
                    ["isWorkshared"] = doc.IsWorkshared,
                    ["projectInfo"]  = projectInfo,
                    ["activeView"]   = activeView,
                };

                string summary = $"Model '{doc.Title}' — active view '{viewName}'.";
                return McpJobResult.Success(summary, data);
            });

            return r.ToCallResult();
        }

        /// <summary>Read a possibly-throwing string getter, returning "" on any failure.</summary>
        private static string SafeStr(Func<string> getter)
        {
            try { return getter() ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Resolve the active view's discipline from the VIEW_DISCIPLINE built-in
        /// parameter (a ViewDiscipline bitmask). Returns a human label or "" when
        /// the view carries no discipline (drafting views, schedules, etc.).
        /// </summary>
        private static string ResolveDiscipline(View v)
        {
            try
            {
                Parameter p = v.get_Parameter(BuiltInParameter.VIEW_DISCIPLINE);
                if (p == null || !p.HasValue) return string.Empty;
                int d = p.AsInteger();
                switch (d)
                {
                    case 1:  return "Architectural";
                    case 2:  return "Structural";
                    case 4:  return "Mechanical";
                    case 8:  return "Electrical";
                    case 16: return "Plumbing";
                    case 4095: return "Coordination";
                    default: return d > 0 ? "Multiple" : string.Empty;
                }
            }
            catch { return string.Empty; }
        }

        // ── Response helpers ─────────────────────────────────────────────────────

        internal static McpCallResult Ok(string text) => new McpCallResult
        {
            Content = new List<McpContent> { new McpContent { Type = "text", Text = text } },
            IsError = false,
        };

        internal static McpCallResult Err(string text) => new McpCallResult
        {
            Content = new List<McpContent> { new McpContent { Type = "text", Text = text } },
            IsError = true,
        };
    }
}
