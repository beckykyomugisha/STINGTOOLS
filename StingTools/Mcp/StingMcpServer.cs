// ════════════════════════════════════════════════════════════════════════════
// StingMcpServer — MCP (Model Context Protocol) server embedded in the addin
//
// Exposes five tools to Claude / Claude Code over HTTP JSON-RPC on localhost:
//   run_command   — execute any STING command by tag
//   nlp_query     — natural language → NLP engine → execute best match
//   list_commands — browse the command catalogue with optional filter
//   get_status    — cached compliance scan results
//   ask_bim       — BIM/ISO 19650 knowledge Q&A (local KB first, LLM fallback)
//
// Transport: HTTP JSON-RPC 2.0  (POST http://localhost:{port}/mcp/)
// Lifecycle: Start() called from StingToolsApp.OnStartup when mcp_enabled=true
//            Stop()  called from StingToolsApp.OnShutdown
//
// Claude Code .mcp.json:
//   { "mcpServers": { "stingtools": { "url": "http://localhost:5199/mcp/" } } }
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Tags;
using StingTools.UI;

namespace StingTools.Mcp
{
    internal static class StingMcpServer
    {
        private static HttpListener _listener;
        private static Thread       _thread;
        private static int          _port    = 5199;
        private static volatile bool _running = false;

        // Shared-secret auth. When non-empty, every POST must carry a matching
        // X-Sting-Mcp-Token header. Loaded from STING_LLM_CONFIG.json at start.
        private static string        _authToken   = "";
        // Curated allowlist of command tags run_command may execute (empty = all
        // known tags). Loaded from config; consumed by the write suite (Phase 3).
        private static List<string>  _toolAllowlist = new List<string>();

        // Reason the last Start() failed to bind (null when the last start succeeded).
        // Surfaced by StartAndPersist() so the toggle dialog can show why.
        private static string        _lastStartError;

        /// <summary>True while the HTTP listener is bound and serving.</summary>
        internal static bool IsRunning => _running;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        internal static void StartIfConfigured()
        {
            try
            {
                string cfgPath = Path.Combine(StingToolsApp.DataPath, "STING_LLM_CONFIG.json");
                if (!File.Exists(cfgPath)) return;

                var cfg = JObject.Parse(File.ReadAllText(cfgPath));
                bool enabled = cfg["mcp_enabled"]?.Value<bool>() ?? false;
                if (!enabled) return;

                int port = cfg["mcp_port"]?.Value<int>() ?? 5199;

                // Load shared-secret + allowlist before binding so the very first
                // request is already governed by the configured policy.
                _authToken = cfg["mcp_auth_token"]?.Value<string>()?.Trim() ?? "";
                _toolAllowlist = (cfg["mcp_tool_allowlist"] as JArray)?
                    .Select(t => t?.Value<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .ToList() ?? new List<string>();

                Start(port);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MCP config read failed: {ex.Message}");
            }
        }

        internal static void Start(int port = 5199)
        {
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/mcp/");
            try
            {
                _listener.Start();
                _running = true;
                _lastStartError = null;
                _thread  = new Thread(Listen) { IsBackground = true, Name = "StingMcpServer" };
                _thread.Start();
                StingLog.Info($"STING MCP server started — http://localhost:{port}/mcp/");
                if (string.IsNullOrEmpty(_authToken))
                    StingLog.Warn("STING MCP server is UNAUTHENTICATED — mcp_auth_token is empty in " +
                                  "STING_LLM_CONFIG.json. The server is localhost-bound, but any local " +
                                  "process can call it. Set mcp_auth_token to require the X-Sting-Mcp-Token header.");
                else
                    StingLog.Info("STING MCP server auth: X-Sting-Mcp-Token required on POST.");
            }
            catch (HttpListenerException ex)
            {
                _lastStartError = $"Could not bind to port {port}: {ex.Message}. " +
                                  "Try a different mcp_port in STING_LLM_CONFIG.json.";
                StingLog.Warn("MCP server " + _lastStartError);
            }
        }

        internal static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            StingLog.Info("STING MCP server stopped");
        }

        // ── One-click toggle surface (used by ToggleMcpServerCommand) ─────────────

        /// <summary>
        /// Start the server live and persist the enable flag + auth token to
        /// STING_LLM_CONFIG.json (all other keys preserved). Generates a token on
        /// first enable when none is configured. Reuses <see cref="Start"/> — does
        /// NOT duplicate listener logic. Returns false (with <paramref name="error"/>
        /// populated from the bind reason) and does NOT persist mcp_enabled=true when
        /// the port cannot be bound.
        /// </summary>
        internal static bool StartAndPersist(out string error)
        {
            error = null;
            if (_running) return true;

            try
            {
                string cfgPath = Path.Combine(StingToolsApp.DataPath, "STING_LLM_CONFIG.json");
                JObject cfg = File.Exists(cfgPath)
                    ? JObject.Parse(File.ReadAllText(cfgPath))
                    : new JObject();

                // Auto-generate a shared secret on first enable.
                string token = cfg["mcp_auth_token"]?.Value<string>()?.Trim() ?? "";
                if (string.IsNullOrEmpty(token))
                    token = Guid.NewGuid().ToString("N");

                int port = cfg["mcp_port"]?.Value<int>() ?? _port;

                // Load token + allowlist into memory BEFORE Start so the first
                // request is already governed by the configured policy.
                _authToken = token;
                _toolAllowlist = (cfg["mcp_tool_allowlist"] as JArray)?
                    .Select(t => t?.Value<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .ToList() ?? new List<string>();

                Start(port);

                if (!_running)
                {
                    // Bind failed — surface the reason, persist nothing.
                    error = _lastStartError ?? $"MCP server failed to start on port {port}.";
                    return false;
                }

                // Persist only after a confirmed successful start.
                cfg["mcp_enabled"]    = true;
                cfg["mcp_auth_token"] = token;
                if (cfg["mcp_port"] == null) cfg["mcp_port"] = port;
                File.WriteAllText(cfgPath, cfg.ToString(Formatting.Indented));
                StingLog.Info("MCP server enabled + persisted via toggle.");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error("MCP StartAndPersist failed", ex);
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Stop the server and persist mcp_enabled=false. The auth token is KEPT so
        /// re-enabling reuses the same secret (no client reconfiguration needed).
        /// </summary>
        internal static void StopAndPersist()
        {
            Stop();
            try
            {
                string cfgPath = Path.Combine(StingToolsApp.DataPath, "STING_LLM_CONFIG.json");
                if (!File.Exists(cfgPath)) return;
                JObject cfg = JObject.Parse(File.ReadAllText(cfgPath));
                cfg["mcp_enabled"] = false;   // keep mcp_auth_token
                File.WriteAllText(cfgPath, cfg.ToString(Formatting.Indented));
                StingLog.Info("MCP server disabled + persisted via toggle (token retained).");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MCP StopAndPersist config write failed: {ex.Message}");
            }
        }

        /// <summary>Current URL + token + a ready-to-paste Claude Code .mcp.json snippet.</summary>
        internal static McpConnectionInfo GetConnectionInfo()
        {
            string url = $"http://localhost:{_port}/mcp/";
            var snippet = new JObject(
                new JProperty("mcpServers", new JObject(
                    new JProperty("stingtools", new JObject(
                        new JProperty("url", url),
                        new JProperty("headers", new JObject(
                            new JProperty("X-Sting-Mcp-Token", _authToken))))))));
            return new McpConnectionInfo
            {
                Url          = url,
                Token        = _authToken,
                ClaudeConfig = snippet.ToString(Formatting.Indented),
            };
        }

        // ── HTTP listener loop ───────────────────────────────────────────────────

        private static void Listen()
        {
            while (_running && _listener.IsListening)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
                }
                catch (HttpListenerException) { break; }
                catch (Exception ex) { StingLog.Warn($"MCP listener: {ex.Message}"); }
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (ctx.Request.HttpMethod == "OPTIONS")
                { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

                // GET → server info
                if (ctx.Request.HttpMethod == "GET")
                {
                    WriteJson(ctx, 200, new
                    {
                        server  = "STING Tools MCP",
                        version = "1.0",
                        port    = _port,
                        tools   = McpToolRegistry.GetTools().Select(t => t.Name).ToList(),
                        usage   = "POST /mcp/ with a JSON-RPC 2.0 body"
                    });
                    return;
                }

                if (ctx.Request.HttpMethod != "POST")
                { WriteJson(ctx, 405, new { error = "Method not allowed" }); return; }

                // Shared-secret gate — POST only (GET server-info stays open, localhost
                // bound). When a token is configured, the X-Sting-Mcp-Token header must
                // match exactly; otherwise reject with JSON-RPC -32001 before doing any
                // work. Id is unknown at this point (body not yet read) → null.
                if (!string.IsNullOrEmpty(_authToken))
                {
                    string presented = ctx.Request.Headers["X-Sting-Mcp-Token"];
                    if (!string.Equals(presented, _authToken, StringComparison.Ordinal))
                    {
                        StingLog.Warn("MCP POST rejected: missing/invalid X-Sting-Mcp-Token.");
                        WriteJson(ctx, 200, RpcError(null, -32001, "unauthorized"));
                        return;
                    }
                }

                string body;
                using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                    body = sr.ReadToEnd();

                JsonRpcRequest req;
                try { req = JsonConvert.DeserializeObject<JsonRpcRequest>(body); }
                catch { WriteJson(ctx, 400, RpcError(null, -32700, "JSON parse error")); return; }

                WriteJson(ctx, 200, Dispatch(req));
            }
            catch (Exception ex)
            {
                StingLog.Error("MCP handle error", ex);
                try { WriteJson(ctx, 500, RpcError(null, -32603, ex.Message)); } catch { }
            }
        }

        // ── JSON-RPC dispatcher ──────────────────────────────────────────────────

        private static JsonRpcResponse Dispatch(JsonRpcRequest req)
        {
            if (req == null) return RpcError(null, -32600, "Invalid request");

            switch (req.Method)
            {
                case "initialize":
                    return RpcOk(req.Id, new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities    = new { tools = new { listChanged = false } },
                        serverInfo      = new { name = "stingtools", version = "1.0" }
                    });

                case "tools/list":
                    return RpcOk(req.Id, new { tools = McpToolRegistry.GetTools() });

                case "tools/call":
                    return HandleToolCall(req);

                case "notifications/initialized":
                    return RpcOk(req.Id, new { }); // ack — no response body needed

                default:
                    return RpcError(req.Id, -32601, $"Unknown method: {req.Method}");
            }
        }

        private static JsonRpcResponse HandleToolCall(JsonRpcRequest req)
        {
            string name = req.Params?["name"]?.Value<string>();
            var    args = req.Params?["arguments"] as JObject ?? new JObject();

            McpCallResult result;
            switch (name)
            {
                case "run_command":   result = ToolRunCommand(args);   break;
                case "nlp_query":     result = ToolNlpQuery(args);     break;
                case "list_commands": result = ToolListCommands(args); break;
                case "get_status":    result = ToolGetStatus();        break;
                case "ask_bim":       result = ToolAskBim(args);       break;
                case "get_model_info": result = ToolGetModelInfo();    break;
                default:
                    result = Err($"Unknown tool: {name}. Call tools/list to see available tools.");
                    break;
            }
            return RpcOk(req.Id, result);
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

            return Ok($"STING Model Status\n{scan.StatusBarText}\n" +
                      $"MCP server: running on http://localhost:{_port}/mcp/");
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
            // modal UI, so it cannot deadlock the waiting HTTP thread.
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

        private static McpCallResult Ok(string text) => new McpCallResult
        {
            Content = new List<McpContent> { new McpContent { Type = "text", Text = text } },
            IsError = false,
        };

        private static McpCallResult Err(string text) => new McpCallResult
        {
            Content = new List<McpContent> { new McpContent { Type = "text", Text = text } },
            IsError = true,
        };

        private static JsonRpcResponse RpcOk(string id, object result) =>
            new JsonRpcResponse { Id = id, Result = result };

        private static JsonRpcResponse RpcError(string id, int code, string message) =>
            new JsonRpcResponse { Id = id, Error = new McpRpcError { Code = code, Message = message } };

        private static void WriteJson(HttpListenerContext ctx, int status, object obj)
        {
            byte[] buf = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj));
            ctx.Response.StatusCode      = status;
            ctx.Response.ContentType     = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            ctx.Response.Close();
        }
    }
}
