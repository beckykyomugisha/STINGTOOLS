using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

// TagIntelligence + StingPluginHooks relocated out of the oversized
// TagConfig.cs. Same namespace (StingTools.Core) — transparent to callers.

namespace StingTools.Core
{
    /// <summary>
    /// Advanced tagging intelligence engine providing multi-layered reasoning,
    /// cross-validation, workset inference, connected system traversal,
    /// sizing-based classification, and confidence scoring.
    ///
    /// Intelligence layers (applied in order of reliability):
    ///   L1. Connected MEP system name (connector traversal)
    ///   L2. Duct/Pipe system type built-in parameter
    ///   L3. Electrical circuit panel analysis
    ///   L4. Family name pattern matching (35+ equipment types)
    ///   L5. Room-type inference (Server Room → ICT, Kitchen → SAN)
    ///   L6. Workset name inference (M-Mechanical → M, E-Electrical → E)
    ///   L7. Connected element traversal (trace pipe/duct to source equipment)
    ///   L8. Size-based classification (small pipe → sanitary, large pipe → DCW)
    ///   L9. Adjacent element analysis (nearby elements suggest system context)
    ///   L10. Cross-validation (DISC vs SYS vs FUNC consistency checks)
    ///
    /// Each layer produces a confidence score (0.0-1.0). The highest-confidence
    /// result wins. Ties are broken by layer priority (lower = more reliable).
    /// </summary>
    public static class TagIntelligence
    {
        /// <summary>
        /// Result from an intelligence layer, including the derived value,
        /// the confidence level, and which layer produced it (for audit trail).
        /// </summary>
        public class InferenceResult
        {
            public string Value { get; set; }
            public double Confidence { get; set; }
            public string Source { get; set; }

            public InferenceResult(string value, double confidence, string source)
            {
                Value = value;
                Confidence = confidence;
                Source = source;
            }
        }

        // ── Layer 6: Workset Name Inference ──────────────────────────────────

        /// <summary>
        /// Infer discipline from the element's workset name.
        /// Revit worksets follow naming conventions like "M-Mechanical", "E-Electrical",
        /// "P-Plumbing", "A-Architecture", "S-Structure" per AEC UK BIM Protocol.
        /// </summary>
        public static InferenceResult InferDiscFromWorkset(Element el)
        {
            try
            {
                if (el.Document.IsWorkshared)
                {
                    WorksetId wsId = el.WorksetId;
                    if (wsId != null && wsId != WorksetId.InvalidWorksetId)
                    {
                        WorksetTable table = el.Document.GetWorksetTable();
                        Workset ws = table.GetWorkset(wsId);
                        if (ws != null)
                        {
                            string wsName = ws.Name?.ToUpperInvariant() ?? "";

                            if (wsName.StartsWith("M-") || wsName.Contains("MECHANICAL") ||
                                wsName.Contains("HVAC"))
                                return new InferenceResult("M", 0.7, "Workset: " + ws.Name);
                            if (wsName.StartsWith("E-") || wsName.Contains("ELECTRICAL") ||
                                wsName.Contains("LIGHTING"))
                                return new InferenceResult("E", 0.7, "Workset: " + ws.Name);
                            if (wsName.StartsWith("P-") || wsName.Contains("PLUMBING") ||
                                wsName.Contains("PUBLIC HEALTH"))
                                return new InferenceResult("P", 0.7, "Workset: " + ws.Name);
                            if (wsName.StartsWith("A-") || wsName.Contains("ARCHITECT"))
                                return new InferenceResult("A", 0.7, "Workset: " + ws.Name);
                            if (wsName.StartsWith("S-") || wsName.Contains("STRUCT"))
                                return new InferenceResult("S", 0.7, "Workset: " + ws.Name);
                            if (wsName.Contains("FIRE"))
                                return new InferenceResult("FP", 0.7, "Workset: " + ws.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InferDiscFromWorkset: {ex.Message}");
            }
            return null;
        }

        // ── Layer 7: Connected Element Traversal ─────────────────────────────

        /// <summary>
        /// Trace the connected system from an element back to its source equipment.
        /// For pipes/ducts, follows the system to find the connected major equipment
        /// (AHU, pump, boiler, chiller) which identifies the system type definitively.
        /// Limited to 2 hops to avoid performance issues.
        /// </summary>
        public static InferenceResult InferSysFromConnectedEquipment(Element el)
        {
            try
            {
                FamilyInstance fi = el as FamilyInstance;
                if (fi?.MEPModel?.ConnectorManager == null) return null;

                // Traverse up to 2 hops of connected elements
                var visited = new HashSet<ElementId> { el.Id };
                var queue = new Queue<(Element elem, int depth)>();

                foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                {
                    if (conn.AllRefs == null) continue;
                    foreach (Connector other in conn.AllRefs)
                    {
                        if (other.Owner != null && !visited.Contains(other.Owner.Id))
                        {
                            visited.Add(other.Owner.Id);
                            queue.Enqueue((other.Owner, 1));
                        }
                    }
                }

                while (queue.Count > 0)
                {
                    var (current, depth) = queue.Dequeue();
                    string catName = ParameterHelpers.GetCategoryName(current);

                    // If we reached major equipment, use its classification
                    if (catName == "Mechanical Equipment")
                    {
                        string famName = ParameterHelpers.GetFamilyName(current).ToUpperInvariant();
                        if (famName.Contains("AHU") || famName.Contains("AIR HANDLING"))
                            return new InferenceResult("HVAC", 0.9, "Connected to AHU: " + current.Id);
                        if (famName.Contains("BOILER") || famName.Contains("BLR"))
                            return new InferenceResult("HWS", 0.9, "Connected to boiler: " + current.Id);
                        if (famName.Contains("CHILLER") || famName.Contains("CHR"))
                            return new InferenceResult("HVAC", 0.9, "Connected to chiller: " + current.Id);
                        if (famName.Contains("PUMP"))
                            return new InferenceResult("DCW", 0.8, "Connected to pump: " + current.Id);
                    }
                    else if (catName == "Electrical Equipment")
                    {
                        return new InferenceResult("LV", 0.85, "Connected to panel: " + current.Id);
                    }

                    // Continue traversal (max 2 hops)
                    if (depth < 2 && current is FamilyInstance fi2 &&
                        fi2.MEPModel?.ConnectorManager != null)
                    {
                        foreach (Connector conn in fi2.MEPModel.ConnectorManager.Connectors)
                        {
                            if (conn.AllRefs == null) continue;
                            foreach (Connector other in conn.AllRefs)
                            {
                                if (other.Owner != null && !visited.Contains(other.Owner.Id))
                                {
                                    visited.Add(other.Owner.Id);
                                    queue.Enqueue((other.Owner, depth + 1));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InferSysFromConnectedEquipment: {ex.Message}");
            }
            return null;
        }

        // ── Layer 8: Size-Based Classification ───────────────────────────────

        /// <summary>
        /// Infer system type from element sizing.
        /// In MEP design, pipe/duct sizes correlate strongly with system type:
        ///   - Pipes ≤32mm: typically sanitary branch, DCW branch
        ///   - Pipes 40-80mm: sanitary mains, DCW mains
        ///   - Pipes 80-200mm: fire mains, DCW risers, HVAC CHW
        ///   - Pipes ≥200mm: fire protection mains, DHW risers
        ///   - Ducts ≤300mm: extract/exhaust branches
        ///   - Ducts 300-600mm: supply/return branches
        ///   - Ducts ≥600mm: supply/return mains
        /// </summary>
        public static InferenceResult InferSysFromSize(Element el)
        {
            try
            {
                // Read calculated size or diameter
                Parameter sizePar = el.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE);
                Parameter diaPar = el.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);

                double diameterMm = 0;
                if (diaPar != null && diaPar.HasValue && diaPar.StorageType == StorageType.Double)
                    diameterMm = diaPar.AsDouble() * 304.8; // feet to mm

                string catName = ParameterHelpers.GetCategoryName(el);

                // Pipe size-based inference (only applies if no system info available)
                if (catName == "Pipes" && diameterMm > 0)
                {
                    if (diameterMm >= 100)
                        return new InferenceResult("FP", 0.4,
                            $"Size inference: pipe {diameterMm:F0}mm ≥ 100mm (possible fire main)");
                    if (diameterMm <= 32)
                        return new InferenceResult("SAN", 0.3,
                            $"Size inference: pipe {diameterMm:F0}mm ≤ 32mm (possible sanitary branch)");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InferSysFromSize: {ex.Message}");
            }
            return null;
        }

        // ── Layer 9: Adjacent Element SYS Inference ──────────────────────────

        /// <summary>
        /// ENH-004: Infer SYS from adjacent elements within a 500mm radius.
        /// Uses BoundingBoxIntersectsFilter to find nearby elements with confirmed SYS values.
        /// If 80%+ of adjacent elements agree on a SYS code, returns that code with confidence 0.3.
        /// Useful for unconnected light fittings, generic models, and equipment placed near systems.
        /// </summary>
        public static InferenceResult InferSysFromAdjacentElements(Document doc, Element el)
        {
            try
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(null);
                if (bb == null) return null;

                // Expand bounding box by 500mm (≈1.64 ft) in all directions
                double expandFt = 500.0 / 304.8; // mm to feet
                XYZ min = new XYZ(bb.Min.X - expandFt, bb.Min.Y - expandFt, bb.Min.Z - expandFt);
                XYZ max = new XYZ(bb.Max.X + expandFt, bb.Max.Y + expandFt, bb.Max.Z + expandFt);
                Outline outline = new Outline(min, max);

                var bbFilter = new BoundingBoxIntersectsFilter(outline);
                var nearby = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(bbFilter)
                    .Where(e => e.Id != el.Id && e.Category != null)
                    .ToList();

                if (nearby.Count == 0) return null;

                // Count SYS values on nearby elements
                var sysCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int withSys = 0;

                foreach (Element adj in nearby)
                {
                    string adjSys = ParameterHelpers.GetString(adj, ParamRegistry.SYS);
                    if (string.IsNullOrEmpty(adjSys)) continue;

                    withSys++;
                    sysCounts.TryGetValue(adjSys, out int sc);
                    sysCounts[adjSys] = sc + 1;
                }

                // R3-FIX-01: Guard against empty sysCounts before .First()
                if (withSys < 2 || sysCounts.Count == 0) return null; // Need at least 2 neighbours with SYS

                // Find dominant SYS
                var dominant = sysCounts.OrderByDescending(x => x.Value).First();
                double agreement = (double)dominant.Value / withSys;

                if (agreement >= 0.8)
                {
                    return new InferenceResult(dominant.Key, 0.3,
                        $"Adjacent element inference: {dominant.Value}/{withSys} neighbours have SYS={dominant.Key}");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InferSysFromAdjacentElements: {ex.Message}");
            }
            return null;
        }

        // ── Layer 10: Cross-Validation ───────────────────────────────────────

        /// <summary>
        /// Cross-validate DISC, SYS, FUNC, and PROD codes against each other.
        /// Returns a list of inconsistencies found (empty list = all valid).
        ///
        /// Rules:
        ///   - DISC=M requires SYS in {HVAC, HWS, DCW, GAS, RWD, SAN, DHW}
        ///   - DISC=E requires SYS in {LV, FLS, SEC, ICT, COM, NCL}
        ///   - DISC=P requires SYS in {DCW, DHW, SAN, RWD, GAS}
        ///   - DISC=FP requires SYS in {FP, FLS}
        ///   - PROD code must be compatible with category
        ///   - FUNC code must be compatible with SYS code
        /// </summary>
        public static List<string> CrossValidate(Element el)
        {
            var issues = new List<string>();
            string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
            string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
            string func = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
            string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD);
            string catName = ParameterHelpers.GetCategoryName(el);

            if (string.IsNullOrEmpty(disc)) return issues;

            // DISC ↔ SYS consistency
            if (!string.IsNullOrEmpty(sys) && ISO19650Validator._validSysForDisc.TryGetValue(disc, out var validSys))
            {
                if (!validSys.Contains(sys))
                    issues.Add($"DISC={disc} incompatible with SYS={sys} (expected: {string.Join("/", validSys)})");
            }

            // DISC ↔ Category consistency
            string expectedDisc = TagConfig.DiscMap.TryGetValue(catName, out string ed) ? ed : null;
            if (expectedDisc != null && expectedDisc != disc)
                issues.Add($"DISC={disc} doesn't match category '{catName}' (expected: {expectedDisc})");

            // SYS ↔ FUNC consistency (HVAC should have SUP/RTN/EXH/FRA, not PWR)
            if (sys == "HVAC" && func == "PWR")
                issues.Add($"SYS=HVAC with FUNC=PWR is invalid (expected: SUP/RTN/EXH/FRA)");
            if (sys == "LV" && (func == "SUP" || func == "HTG"))
                issues.Add($"SYS=LV with FUNC={func} is invalid (expected: PWR/LTG)");

            return issues;
        }

        /// <summary>
        /// Perform full tagging intelligence analysis on an element.
        /// Runs all inference layers and returns a consolidated report
        /// with the best result per token and confidence scores.
        /// Used by AutoPopulate and pre-tagging audit.
        /// </summary>
        public static Dictionary<string, InferenceResult> AnalyzeElement(Document doc, Element el)
        {
            var results = new Dictionary<string, InferenceResult>();
            string catName = ParameterHelpers.GetCategoryName(el);

            // DISC — category is primary (confidence 1.0), workset as validation
            if (TagConfig.DiscMap.TryGetValue(catName, out string disc))
                results["DISC"] = new InferenceResult(disc, 1.0, "Category: " + catName);

            var wsResult = InferDiscFromWorkset(el);
            if (wsResult != null && results.TryGetValue("DISC", out var discResult) && discResult.Value != wsResult.Value)
                StingLog.Warn($"Element {el.Id}: category says DISC={discResult.Value} but workset says {wsResult.Value}");

            // SYS — multi-layer with confidence scoring
            string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
            if (!string.IsNullOrEmpty(sys))
                results["SYS"] = new InferenceResult(sys, 0.85, "MEP system detection");

            // Try connected equipment traversal for higher confidence
            var connResult = InferSysFromConnectedEquipment(el);
            if (connResult != null && connResult.Confidence > (results.TryGetValue("SYS", out var curSys) ? curSys.Confidence : 0))
                results["SYS"] = connResult;

            // Size-based only if nothing else worked
            if (!results.TryGetValue("SYS", out var sysEntry) || sysEntry.Confidence < 0.5)
            {
                var sizeResult = InferSysFromSize(el);
                if (sizeResult != null)
                    results["SYS"] = sizeResult;
            }

            // Layer 9 — adjacent element inference (lowest confidence, last resort)
            if (!results.TryGetValue("SYS", out sysEntry) || sysEntry.Confidence < 0.3)
            {
                var adjResult = InferSysFromAdjacentElements(doc, el);
                if (adjResult != null)
                    results["SYS"] = adjResult;
            }

            // FUNC — smart detection
            string sysVal = results.TryGetValue("SYS", out var sysForFunc) ? sysForFunc.Value : "";
            string func = TagConfig.GetSmartFuncCode(el, sysVal);
            if (!string.IsNullOrEmpty(func))
                results["FUNC"] = new InferenceResult(func, 0.8, "Smart FUNC detection");

            // PROD — family-aware
            string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
            if (!string.IsNullOrEmpty(prod))
                results["PROD"] = new InferenceResult(prod, 0.9, "Family-aware PROD");

            // LVL
            string lvl = ParameterHelpers.GetLevelCode(doc, el);
            if (lvl != "XX")
                results["LVL"] = new InferenceResult(lvl, 1.0, "Level: auto-derived");

            return results;
        }

        /// <summary>
        /// Generate a human-readable audit trail for an element's tag derivation.
        /// Shows what each intelligence layer detected and why, with confidence scores.
        /// </summary>
        public static string GenerateAuditTrail(Document doc, Element el)
        {
            var sb = new System.Text.StringBuilder();
            string catName = ParameterHelpers.GetCategoryName(el);
            sb.AppendLine($"Element {el.Id} [{catName}]: {ParameterHelpers.GetFamilyName(el)}");

            var analysis = AnalyzeElement(doc, el);
            foreach (var kvp in analysis)
            {
                sb.AppendLine($"  {kvp.Key} = {kvp.Value.Value} " +
                    $"(confidence: {kvp.Value.Confidence:P0}, source: {kvp.Value.Source})");
            }

            // Cross-validation
            var issues = CrossValidate(el);
            if (issues.Count > 0)
            {
                sb.AppendLine("  WARNINGS:");
                foreach (string issue in issues)
                    sb.AppendLine($"    ! {issue}");
            }
            else
            {
                sb.AppendLine("  Cross-validation: PASS");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Plugin hook system — extensibility framework for third-party command registration.
    /// Third-party plugins register hooks at Revit startup; STING invokes them at defined points.
    /// </summary>
    public static class StingPluginHooks
    {
        /// <summary>Hook invoked before each element is tagged in RunFullPipeline.</summary>
        public static event Action<Document, Element> BeforeTagElement;

        /// <summary>Hook invoked after each element is tagged in RunFullPipeline.</summary>
        public static event Action<Document, Element, string> AfterTagElement;

        /// <summary>Hook for custom token validation. Return null if valid, error string if invalid.</summary>
        public static event Func<string, string, string> ValidateToken;

        /// <summary>Hook invoked before a workflow preset executes.</summary>
        public static event Action<string> BeforeWorkflow;

        /// <summary>Hook invoked after a workflow preset completes.</summary>
        public static event Action<string, bool> AfterWorkflow;

        /// <summary>Registry of third-party commands keyed by tag string.</summary>
        private static readonly Dictionary<string, Func<UIApplication, string>> _customCommands
            = new Dictionary<string, Func<UIApplication, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Invoke BeforeWorkflow event (callable from other classes since events can only be raised by declaring class).</summary>
        internal static void InvokeBeforeWorkflow(string presetName) => BeforeWorkflow?.Invoke(presetName);

        /// <summary>Invoke AfterWorkflow event (callable from other classes since events can only be raised by declaring class).</summary>
        internal static void InvokeAfterWorkflow(string presetName, bool success) => AfterWorkflow?.Invoke(presetName, success);

        /// <summary>Register a custom command that can be invoked from workflows or dispatch.</summary>
        public static void RegisterCommand(string tag, Func<UIApplication, string> handler)
        {
            if (string.IsNullOrWhiteSpace(tag) || handler == null) return;
            _customCommands[tag] = handler;
            StingLog.Info($"StingPluginHooks: registered custom command '{tag}'");
        }

        /// <summary>Unregister a custom command.</summary>
        public static void UnregisterCommand(string tag)
        {
            if (_customCommands.Remove(tag))
                StingLog.Info($"StingPluginHooks: unregistered command '{tag}'");
        }

        /// <summary>Try to execute a registered custom command. Returns (found, resultMessage).</summary>
        public static (bool Found, string Result) TryExecuteCommand(string tag, UIApplication app)
        {
            if (_customCommands.TryGetValue(tag, out var handler))
            {
                try
                {
                    string result = handler(app);
                    return (true, result ?? "OK");
                }
                catch (Exception ex)
                {
                    StingLog.Error($"StingPluginHooks: command '{tag}' failed", ex);
                    return (true, $"Error: {ex.Message}");
                }
            }
            return (false, null);
        }

        /// <summary>Get list of registered custom command tags.</summary>
        public static IReadOnlyList<string> RegisteredCommands => _customCommands.Keys.ToList().AsReadOnly();

        /// <summary>Fire the BeforeTagElement hook (safe — catches exceptions).</summary>
        internal static void FireBeforeTag(Document doc, Element el)
        {
            try { BeforeTagElement?.Invoke(doc, el); }
            catch (Exception ex) { StingLog.Warn($"StingPluginHooks.BeforeTag: {ex.Message}"); }
        }

        /// <summary>Fire the AfterTagElement hook (safe — catches exceptions).</summary>
        internal static void FireAfterTag(Document doc, Element el, string tag)
        {
            try { AfterTagElement?.Invoke(doc, el, tag); }
            catch (Exception ex) { StingLog.Warn($"StingPluginHooks.AfterTag: {ex.Message}"); }
        }

        /// <summary>Run custom validators. Returns first error or null.</summary>
        internal static string RunCustomValidators(string tokenName, string value)
        {
            if (ValidateToken == null) return null;
            foreach (var handler in ValidateToken.GetInvocationList().Cast<Func<string, string, string>>())
            {
                try
                {
                    string error = handler(tokenName, value);
                    if (error != null) return error;
                }
                catch (Exception ex) { StingLog.Warn($"StingPluginHooks.ValidateToken: {ex.Message}"); }
            }
            return null;
        }

        /// <summary>Clear all hooks and registered commands (called on plugin shutdown).</summary>
        public static void ClearAll()
        {
            BeforeTagElement = null;
            AfterTagElement = null;
            ValidateToken = null;
            BeforeWorkflow = null;
            AfterWorkflow = null;
            _customCommands.Clear();
        }
    }
}
