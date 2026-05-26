using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Tags;

namespace StingTools.Core
{
    // ════════════════════════════════════════════════════════════════════════════
    // StingLlmService — surgical LLM integration
    //
    // Three use-cases only:
    //   1. ParseDesignBriefAsync  — budget + brief → DesignBrief struct + command tag
    //   2. AskBimQuestionAsync    — BIM knowledge Q&A via RAG over STING docs
    //   3. DraftDocumentAsync     — transmittals / reports / meeting minutes
    //
    // Architecture:
    //   • Primary:  Azure OpenAI (config: Nlp:Azure:Endpoint / Deployment / ApiKey)
    //   • Fallback: Claude API  (config: Nlp:Claude:ApiKey)
    //   • Offline:  Rule-based answer from BimKnowledge dictionary + pattern match
    //
    // Rules:
    //   • Never send project data — only user's typed text + anonymised context
    //   • LLM output ALWAYS validated through IsValidCommandTag before execution
    //   • PII redacted before every call via PiiRedactor
    //   • All calls time out at 10 seconds; offline fallback on timeout
    // ════════════════════════════════════════════════════════════════════════════

    public sealed class StingLlmService
    {
        private static readonly Lazy<StingLlmService> _instance =
            new Lazy<StingLlmService>(() => new StingLlmService());
        public static StingLlmService Instance => _instance.Value;

        private readonly HttpClient _http;
        private string _azureEndpoint;
        private string _azureDeployment;
        private string _azureKey;
        private string _claudeKey;
        private string _claudeModel;
        private bool   _enabled;

        // Valid command tags — LLM output is rejected unless it is in this whitelist
        private static readonly HashSet<string> _commandWhitelist = BuildWhitelist();

        private StingLlmService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            LoadConfig();
        }

        private void LoadConfig()
        {
            // Config lives in project_config.json or STING_LLM_CONFIG.json alongside the DLL
            try
            {
                string cfgPath = System.IO.Path.Combine(StingToolsApp.DataPath, "STING_LLM_CONFIG.json");
                if (!System.IO.File.Exists(cfgPath)) return;
                var cfg = JObject.Parse(System.IO.File.ReadAllText(cfgPath));
                _enabled        = cfg["enabled"]?.Value<bool>()               ?? false;
                _azureEndpoint  = cfg["azure_endpoint"]?.Value<string>();
                _azureDeployment = cfg["azure_deployment"]?.Value<string>()   ?? "gpt-4o-mini";
                _azureKey       = cfg["azure_api_key"]?.Value<string>();
                _claudeKey      = cfg["claude_api_key"]?.Value<string>();
                _claudeModel    = cfg["claude_model"]?.Value<string>()        ?? "claude-haiku-4-5-20251001";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LLM config load failed (offline mode): {ex.Message}");
            }
        }

        // ── 1. Design brief parser ────────────────────────────────────────────

        public async Task<DesignBriefResult> ParseDesignBriefAsync(string userText)
        {
            string clean = PiiRedactor.Redact(userText);
            string prompt = BuildBriefPrompt(clean);

            string json = await CallLlmAsync(prompt, systemPrompt: BriefSystemPrompt);
            if (json == null) return OfflineBriefFallback(userText);

            try
            {
                var obj = JObject.Parse(ExtractJson(json));
                return new DesignBriefResult
                {
                    BudgetUgx       = obj["budget_ugx"]?.Value<long>() ?? 0,
                    Bedrooms        = obj["bedrooms"]?.Value<int>()     ?? 3,
                    Bathrooms       = obj["bathrooms"]?.Value<int>()    ?? 2,
                    Style           = obj["style"]?.Value<string>()     ?? "modern",
                    Type            = obj["type"]?.Value<string>()      ?? "bungalow",
                    SpecialRooms    = obj["special_rooms"]?.ToObject<List<string>>() ?? new List<string>(),
                    SuggestedCommandTag = "DesignBrief_Residential",
                    Summary         = BuildBriefSummary(obj),
                };
            }
            catch
            {
                return OfflineBriefFallback(userText);
            }
        }

        // ── 2. BIM knowledge Q&A ─────────────────────────────────────────────

        public async Task<string> AskBimQuestionAsync(string question)
        {
            // Check local BIM knowledge base first (instant, free, offline)
            var localMatches = NLPEngine.SearchKnowledge(question);
            if (localMatches != null && localMatches.Count > 0)
            {
                string localAnswer = string.Join("\n", localMatches.Select(m => $"{m.Term}: {m.Definition}"));
                return $"[Local BIM KB]\n{localAnswer}";
            }

            string clean = PiiRedactor.Redact(question);
            string context = BuildRagContext(clean);
            string prompt = $"Context from STING documentation:\n{context}\n\nQuestion: {clean}";

            string answer = await CallLlmAsync(prompt, systemPrompt: KnowledgeSystemPrompt);
            return answer ?? FallbackKnowledgeAnswer(question);
        }

        // ── 3. Generative drafting ────────────────────────────────────────────

        public async Task<string> DraftDocumentAsync(string instruction)
        {
            string clean = PiiRedactor.Redact(instruction);
            string answer = await CallLlmAsync(clean, systemPrompt: DraftingSystemPrompt);
            return answer ?? "[AI unavailable] Please use the Document Management Center to draft transmittals and reports manually.";
        }

        // ── Validation ───────────────────────────────────────────────────────

        public static bool IsValidCommandTag(string tag)
            => !string.IsNullOrWhiteSpace(tag) && _commandWhitelist.Contains(tag);

        // ── Internal ─────────────────────────────────────────────────────────

        private async Task<string> CallLlmAsync(string userPrompt, string systemPrompt)
        {
            if (!_enabled) return null; // LLM disabled in config — rule-based fallback handles this

            // Try Azure OpenAI first
            if (!string.IsNullOrEmpty(_azureEndpoint) && !string.IsNullOrEmpty(_azureKey))
            {
                try
                {
                    return await CallAzureOpenAiAsync(userPrompt, systemPrompt);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Azure OpenAI failed, trying Claude fallback: {ex.Message}");
                }
            }

            // Try Claude API
            if (!string.IsNullOrEmpty(_claudeKey))
            {
                try
                {
                    return await CallClaudeAsync(userPrompt, systemPrompt);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Claude API failed: {ex.Message}");
                }
            }

            return null; // Both unavailable — caller uses offline fallback
        }

        private async Task<string> CallAzureOpenAiAsync(string userPrompt, string systemPrompt)
        {
            string url = $"{_azureEndpoint}/openai/deployments/{_azureDeployment}/chat/completions?api-version=2024-02-01";
            var body = new
            {
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt }
                },
                temperature = 0.2,
                max_tokens  = 800,
                response_format = new { type = "text" }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("api-key", _azureKey);
            req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            return JObject.Parse(json)["choices"]?[0]?["message"]?["content"]?.Value<string>();
        }

        private async Task<string> CallClaudeAsync(string userPrompt, string systemPrompt)
        {
            var body = new
            {
                model   = _claudeModel ?? "claude-haiku-4-5-20251001",
                max_tokens = 800,
                system  = systemPrompt,
                messages = new[] { new { role = "user", content = userPrompt } }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            req.Headers.Add("x-api-key", _claudeKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            return JObject.Parse(json)["content"]?[0]?["text"]?.Value<string>();
        }

        // ── Offline fallbacks ────────────────────────────────────────────────

        private static DesignBriefResult OfflineBriefFallback(string text)
        {
            long budget = ExtractBudget(text);
            int beds = ExtractBedrooms(text);
            return new DesignBriefResult
            {
                BudgetUgx = budget, Bedrooms = beds, Bathrooms = beds > 2 ? 2 : 1,
                Style = text.Contains("modern") ? "modern" : "standard",
                Type  = text.Contains("maisonette") ? "maisonette" : "bungalow",
                SpecialRooms = new List<string>(),
                SuggestedCommandTag = "DesignBrief_Residential",
                Summary = $"Offline parse: {beds}-bed, {budget:N0} UGX budget. Run DesignBrief_Residential to generate model.",
            };
        }

        private static string FallbackKnowledgeAnswer(string q)
        {
            string ql = q.ToLower();
            if (ql.Contains("iso 19650")) return NLPEngine.BimKnowledge.GetValueOrDefault("ISO 19650", "ISO 19650 standard not found in local KB.");
            if (ql.Contains("cobie"))     return NLPEngine.BimKnowledge.GetValueOrDefault("COBie", "COBie definition not found.");
            if (ql.Contains("ifc"))       return NLPEngine.BimKnowledge.GetValueOrDefault("IFC", "IFC definition not found.");
            if (ql.Contains("cde"))       return NLPEngine.BimKnowledge.GetValueOrDefault("CDE", "CDE definition not found.");
            return "AI service unavailable. Use the BIM Knowledge Base button (BIM tab → BIM Knowledge) for offline reference.";
        }

        // ── RAG context builder ──────────────────────────────────────────────

        private static string BuildRagContext(string question)
        {
            var sb = new StringBuilder();
            // Pull relevant entries from BIM KB
            foreach (var (k, v) in NLPEngine.BimKnowledge)
                if (question.ToLower().Contains(k.ToLower()))
                    sb.AppendLine($"{k}: {v}");

            // Pull relevant NLP descriptions
            string ql = question.ToLower();
            int added = 0;
            foreach (var (_, tag, intent, desc) in NLPEngine.IntentPatterns)
            {
                if (added >= 10) break;
                if (desc.ToLower().Contains(ql.Split(' ')[0]))
                { sb.AppendLine($"Command {tag}: {desc}"); added++; }
            }
            return sb.Length > 0 ? sb.ToString() : "No specific context found in STING documentation.";
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string ExtractJson(string s)
        {
            int start = s.IndexOf('{');
            int end   = s.LastIndexOf('}');
            return (start >= 0 && end > start) ? s.Substring(start, end - start + 1) : s;
        }

        private static long ExtractBudget(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text,
                @"(\d[\d,\.]+)\s*(million|m|M|mln)?\s*(ugx|shilling|ugs)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            double v = double.TryParse(m.Groups[1].Value.Replace(",", ""), out double d) ? d : 0;
            if (m.Groups[2].Value.ToLower().StartsWith("m")) v *= 1_000_000;
            return (long)v;
        }

        private static int ExtractBedrooms(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text,
                @"(\d)\s*bed(room)?s?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 3;
        }

        private static string BuildBriefSummary(JObject obj)
        {
            long budget = obj["budget_ugx"]?.Value<long>() ?? 0;
            int beds = obj["bedrooms"]?.Value<int>() ?? 3;
            string style = obj["style"]?.Value<string>() ?? "modern";
            string type = obj["type"]?.Value<string>() ?? "bungalow";
            return $"{beds}-bedroom {style} {type}. Budget: {budget:N0} UGX. " +
                   $"Run DesignBrief_Residential to generate the Revit model, space program, and BOQ.";
        }

        private static string BuildBriefPrompt(string userText)
            => $"Extract the design brief from this text and return valid JSON only.\n" +
               $"Required fields: budget_ugx (number), bedrooms (int), bathrooms (int), " +
               $"style (modern/traditional/colonial/contemporary), type (bungalow/maisonette/duplex/villa), " +
               $"special_rooms (array of strings).\n\nText: {userText}";

        private static readonly string BriefSystemPrompt =
            "You are a BIM design assistant for Ugandan construction projects. " +
            "Parse design briefs into structured JSON. Use UGX as currency. " +
            "Return ONLY valid JSON, no explanations.";

        private static readonly string KnowledgeSystemPrompt =
            "You are a BIM and ISO 19650 expert assistant embedded in STING Tools, " +
            "a Revit plugin for AEC professionals. Answer concisely using the context provided. " +
            "Reference standards by name. Limit answers to 200 words.";

        private static readonly string DraftingSystemPrompt =
            "You are a BIM document drafter. Create professional AEC document text " +
            "(transmittals, RFIs, meeting minutes, handover certificates). " +
            "Use formal ISO 19650 language. Return the draft text only, ready to paste.";

        private static HashSet<string> BuildWhitelist()
        {
            // All valid STING command tags — LLM output validated against this list
            // This prevents hallucinated command names from executing
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AutoTag","BatchTag","TagAndCombine","TagNewOnly","PreTagAudit","ValidateTags",
                "Validate","FixDuplicates","ResolveAllIssues","CompletenessDashboard",
                "SetDisc","SetLoc","SetZone","SetStatus","AssignNumbers","BuildTags",
                "CombineParameters","RetagStale","AnomalyAutoFix","RepairDuplicateSeq",
                "SmartPlaceTags","ArrangeTags","RemoveAnnotationTags","BatchPlaceTags",
                "SheetOrganizer","ViewOrganizer","SheetIndex","Transmittal",
                "MasterSetup","ProjectSetup","LoadParams","FullAutoPopulate",
                "COBieExport","IFCExport","BCFExport","PdfExport","TagRegisterExport",
                "ModelHealth","ClashDetection","StandardsDashboard","ComplianceDashboard",
                "AutoSchedule4D","AutoCost5D","BREEAMAssessment","LifecycleCarbon",
                "DrawingTypes_SyncStyles","DrawingTypes_FromScopeBoxes","DrawingTypes_Inspect",
                "Panel_BatchSchedules","Panel_Audit","Panel_ExportToExcel",
                "Healthcare_RunAllValidators","Healthcare_PressureAudit","Healthcare_MgasAudit",
                "Routing_AutoDrop","Routing_GenerateLayout","Fabrication_GeneratePackage",
                "Placement_PlaceFixtures","Placement_LightingGrid",
                "DesignBrief_Residential","BudgetFeasibility","GenerateSpaceProgram",
                "CreateWalls","CreateFloors","CreateRooms","BuildingShell",
                "MorningHealthCheck","DailyQA","HandoverReadiness","WeeklyDataDrop",
                "BimKnowledgeBase","CommandSuggestion","NLPCommandProcessor",
                "RaiseIssue","IssueDashboard","CreateTransmittal","CreateBEP",
                "CreateRevision","RevisionDashboard","ExportToExcel","ImportFromExcel",
            };
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public sealed class DesignBriefResult
    {
        public long BudgetUgx            { get; set; }
        public int  Bedrooms             { get; set; }
        public int  Bathrooms            { get; set; }
        public string Style              { get; set; }
        public string Type               { get; set; }
        public List<string> SpecialRooms { get; set; }
        public string SuggestedCommandTag { get; set; }
        public string Summary            { get; set; }
    }

    // ── PII redactor ─────────────────────────────────────────────────────────

    internal static class PiiRedactor
    {
        private static readonly System.Text.RegularExpressions.Regex _emailRx =
            new System.Text.RegularExpressions.Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        private static readonly System.Text.RegularExpressions.Regex _phoneRx =
            new System.Text.RegularExpressions.Regex(@"\+?\d[\d\s\-()]{7,}");

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = _emailRx.Replace(text, "[EMAIL]");
            text = _phoneRx.Replace(text, "[PHONE]");
            return text;
        }
    }
}
