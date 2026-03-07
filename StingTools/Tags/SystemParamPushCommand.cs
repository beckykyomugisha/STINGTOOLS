using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;

namespace StingTools.Tags
{
    // ═══════════════════════════════════════════════════════════════════════
    // System Parameter Push Engine
    //
    // When modelling pipework, conduit, ductwork, or any MEP system, new
    // components often lack STING parameters (DISC/LOC/ZONE/LVL/SYS/FUNC/
    // PROD/STATUS). This engine provides a highly automated workflow for
    // pushing parameters from tagged parent elements to all connected
    // components in the same MEP system.
    //
    // 3-Layer Propagation Strategy:
    //
    //   Layer 1 — MEP System Enumeration: Use Revit's MEPSystem API to
    //             get all elements in a connected system at once
    //   Layer 2 — Connector Graph Traversal: BFS walk through connector
    //             network for elements not in formal MEP systems
    //   Layer 3 — Spatial Proximity: Fall back to spatial room context
    //             for elements with no connector data
    //
    // 4-Mode Parameter Resolution:
    //
    //   Mode A — Inherit from tagged parent: Copy exact token values from
    //            the most-complete tagged element in the system
    //   Mode B — Derive from system context: Auto-detect using existing
    //            6-layer GetMepSystemAwareSysCode + SpatialAutoDetect
    //   Mode C — Hybrid: Inherit DISC/SYS/FUNC from parent, derive
    //            LOC/ZONE/LVL from spatial context per-element
    //   Mode D — Full auto-tag: Run complete tagging pipeline on
    //            connected untagged elements (SEQ assigned fresh)
    //
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// System parameter push engine: traverses MEP system connections and
    /// propagates STING parameters to untagged/incomplete elements.
    /// </summary>
    internal static class SystemParamPush
    {
        /// <summary>Propagation mode for parameter push.</summary>
        public enum PushMode
        {
            /// <summary>Copy exact tokens from the most-complete tagged parent.</summary>
            InheritFromParent,
            /// <summary>Auto-derive all tokens using system/spatial context per-element.</summary>
            DeriveFromContext,
            /// <summary>Inherit DISC/SYS/FUNC from parent; derive LOC/ZONE/LVL spatially.</summary>
            Hybrid,
            /// <summary>Run full tagging pipeline (populate + tag + combine).</summary>
            FullAutoTag,
        }

        /// <summary>Result of a system parameter push operation.</summary>
        public class PushResult
        {
            public int TotalInSystem { get; set; }
            public int AlreadyTagged { get; set; }
            public int Pushed { get; set; }
            public int Skipped { get; set; }
            public string SystemName { get; set; } = "";
            public string SystemType { get; set; } = "";
            public Dictionary<string, int> PushedByCategory { get; set; } =
                new Dictionary<string, int>();

            public string BuildReport()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"System: {SystemName}");
                if (!string.IsNullOrEmpty(SystemType))
                    sb.AppendLine($"Type: {SystemType}");
                sb.AppendLine($"Total elements in system: {TotalInSystem}");
                sb.AppendLine($"Already tagged: {AlreadyTagged}");
                sb.AppendLine($"Parameters pushed: {Pushed}");
                if (Skipped > 0)
                    sb.AppendLine($"Skipped: {Skipped}");
                sb.AppendLine();
                if (PushedByCategory.Count > 0)
                {
                    sb.AppendLine("Pushed by category:");
                    foreach (var kvp in PushedByCategory.OrderByDescending(x => x.Value))
                        sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                return sb.ToString();
            }
        }

        // ── Layer 1: MEP System Enumeration ─────────────────────────────

        /// <summary>
        /// Get all elements connected to a given element via MEP systems.
        /// Uses Revit's MEPSystem API: MechanicalSystem, PipingSystem, ElectricalSystem.
        /// This is the fastest and most reliable method.
        /// </summary>
        public static List<Element> GetSystemElements(Document doc, Element seedElement)
        {
            var result = new HashSet<ElementId>();
            result.Add(seedElement.Id);

            // Method 1: Direct MEPSystem membership
            FamilyInstance fi = seedElement as FamilyInstance;
            if (fi?.MEPModel?.ConnectorManager != null)
            {
                foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                {
                    if (conn.MEPSystem != null)
                    {
                        MEPSystem system = conn.MEPSystem;
                        // Get all elements in this system
                        foreach (Element sysEl in system.Elements)
                        {
                            result.Add(sysEl.Id);
                        }
                    }
                }
            }

            // Method 2: For ducts/pipes/conduits/cable trays (MEPCurve)
            if (seedElement is MEPCurve mepCurve)
            {
                var connMgr = mepCurve.ConnectorManager;
                if (connMgr != null)
                {
                    foreach (Connector conn in connMgr.Connectors)
                    {
                        if (conn.MEPSystem != null)
                        {
                            foreach (Element sysEl in conn.MEPSystem.Elements)
                            {
                                result.Add(sysEl.Id);
                            }
                        }
                    }
                }
            }

            // Method 3: Connector graph BFS for elements not in formal systems
            TraverseConnectorGraph(doc, seedElement, result, maxDepth: 10);

            return result.Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .ToList();
        }

        // ── Layer 2: Connector Graph Traversal ──────────────────────────

        /// <summary>
        /// BFS traverse the connector graph starting from a seed element.
        /// Follows physical connections through connector.AllRefs.
        /// Supports MEPCurve (ducts/pipes) and FamilyInstance (equipment).
        /// </summary>
        private static void TraverseConnectorGraph(
            Document doc, Element seed, HashSet<ElementId> visited, int maxDepth)
        {
            var queue = new Queue<(Element elem, int depth)>();
            queue.Enqueue((seed, 0));

            while (queue.Count > 0)
            {
                var (current, depth) = queue.Dequeue();
                if (depth >= maxDepth) continue;

                ConnectorManager connMgr = null;

                if (current is FamilyInstance fInst && fInst.MEPModel?.ConnectorManager != null)
                    connMgr = fInst.MEPModel.ConnectorManager;
                else if (current is MEPCurve mCurve)
                    connMgr = mCurve.ConnectorManager;

                if (connMgr == null) continue;

                foreach (Connector conn in connMgr.Connectors)
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

        // ── Token Reading / Writing ─────────────────────────────────────

        /// <summary>
        /// Read all STING tokens from an element as a dictionary.
        /// Returns: DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, STATUS, TAG1.
        /// </summary>
        public static Dictionary<string, string> ReadTokens(Element el)
        {
            return new Dictionary<string, string>
            {
                { "DISC",   ParameterHelpers.GetString(el, ParamRegistry.DISC) },
                { "LOC",    ParameterHelpers.GetString(el, ParamRegistry.LOC) },
                { "ZONE",   ParameterHelpers.GetString(el, ParamRegistry.ZONE) },
                { "LVL",    ParameterHelpers.GetString(el, ParamRegistry.LVL) },
                { "SYS",    ParameterHelpers.GetString(el, ParamRegistry.SYS) },
                { "FUNC",   ParameterHelpers.GetString(el, ParamRegistry.FUNC) },
                { "PROD",   ParameterHelpers.GetString(el, ParamRegistry.PROD) },
                { "STATUS", ParameterHelpers.GetString(el, ParamRegistry.STATUS) },
                { "TAG1",   ParameterHelpers.GetString(el, ParamRegistry.TAG1) },
            };
        }

        /// <summary>
        /// Count non-empty tokens in a token set (excluding TAG1).
        /// Used to find the "most complete" parent element.
        /// </summary>
        public static int CountPopulatedTokens(Dictionary<string, string> tokens)
        {
            return tokens.Count(kvp =>
                kvp.Key != "TAG1" && !string.IsNullOrEmpty(kvp.Value));
        }

        /// <summary>
        /// Find the best "parent" element in a system — the one with
        /// the most populated STING tokens.
        /// </summary>
        public static (Element Parent, Dictionary<string, string> Tokens)
            FindBestParent(List<Element> systemElements)
        {
            Element best = null;
            Dictionary<string, string> bestTokens = null;
            int bestCount = -1;

            foreach (var el in systemElements)
            {
                var tokens = ReadTokens(el);
                int count = CountPopulatedTokens(tokens);
                if (count > bestCount)
                {
                    bestCount = count;
                    best = el;
                    bestTokens = tokens;
                }
            }

            return (best, bestTokens ?? new Dictionary<string, string>());
        }

        /// <summary>
        /// Get the MEP system name for a seed element.
        /// </summary>
        public static string GetSystemName(Element el)
        {
            FamilyInstance fi = el as FamilyInstance;
            if (fi?.MEPModel?.ConnectorManager != null)
            {
                foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                {
                    if (conn.MEPSystem != null)
                        return conn.MEPSystem.Name ?? "Unknown System";
                }
            }

            if (el is MEPCurve mepCurve)
            {
                var connMgr = mepCurve.ConnectorManager;
                if (connMgr != null)
                {
                    foreach (Connector conn in connMgr.Connectors)
                    {
                        if (conn.MEPSystem != null)
                            return conn.MEPSystem.Name ?? "Unknown System";
                    }
                }
            }

            return "Unassigned";
        }

        // ── Push Operations ─────────────────────────────────────────────

        /// <summary>
        /// Execute a system parameter push operation.
        /// Must be called within an active Transaction.
        /// </summary>
        public static PushResult ExecutePush(
            Document doc, List<Element> systemElements, PushMode mode,
            Dictionary<string, string> parentTokens = null)
        {
            var result = new PushResult
            {
                TotalInSystem = systemElements.Count,
            };

            // Build spatial index for LOC/ZONE auto-detect
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            foreach (var el in systemElements)
            {
                string catName = ParameterHelpers.GetCategoryName(el);
                var existingTokens = ReadTokens(el);

                // Skip if already fully tagged
                if (!string.IsNullOrEmpty(existingTokens["TAG1"]) &&
                    TagConfig.TagIsComplete(existingTokens["TAG1"]))
                {
                    result.AlreadyTagged++;
                    continue;
                }

                try
                {
                    switch (mode)
                    {
                        case PushMode.InheritFromParent:
                            PushInherit(el, existingTokens, parentTokens);
                            break;

                        case PushMode.DeriveFromContext:
                            PushDerive(doc, el, existingTokens, catName, roomIndex, projectLoc);
                            break;

                        case PushMode.Hybrid:
                            PushHybrid(doc, el, existingTokens, parentTokens,
                                catName, roomIndex, projectLoc);
                            break;

                        case PushMode.FullAutoTag:
                            PushFullAuto(doc, el, existingTokens, catName,
                                roomIndex, projectLoc);
                            break;
                    }

                    result.Pushed++;
                    if (!result.PushedByCategory.ContainsKey(catName))
                        result.PushedByCategory[catName] = 0;
                    result.PushedByCategory[catName]++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SystemParamPush: skip {el.Id} — {ex.Message}");
                    result.Skipped++;
                }
            }

            return result;
        }

        /// <summary>Mode A: Copy exact tokens from parent element.</summary>
        private static void PushInherit(
            Element el, Dictionary<string, string> existing,
            Dictionary<string, string> parent)
        {
            if (parent == null) return;

            SetIfMissing(el, ParamRegistry.DISC, existing["DISC"], parent.GetValueOrDefault("DISC", ""));
            SetIfMissing(el, ParamRegistry.LOC, existing["LOC"], parent.GetValueOrDefault("LOC", ""));
            SetIfMissing(el, ParamRegistry.ZONE, existing["ZONE"], parent.GetValueOrDefault("ZONE", ""));
            SetIfMissing(el, ParamRegistry.LVL, existing["LVL"], parent.GetValueOrDefault("LVL", ""));
            SetIfMissing(el, ParamRegistry.SYS, existing["SYS"], parent.GetValueOrDefault("SYS", ""));
            SetIfMissing(el, ParamRegistry.FUNC, existing["FUNC"], parent.GetValueOrDefault("FUNC", ""));
            SetIfMissing(el, ParamRegistry.PROD, existing["PROD"], parent.GetValueOrDefault("PROD", ""));
            SetIfMissing(el, ParamRegistry.STATUS, existing["STATUS"], parent.GetValueOrDefault("STATUS", ""));
        }

        /// <summary>Mode B: Auto-derive all tokens from system/spatial context.</summary>
        private static void PushDerive(
            Document doc, Element el, Dictionary<string, string> existing,
            string catName, Dictionary<ElementId, Room> roomIndex, string projectLoc)
        {
            // DISC
            if (string.IsNullOrEmpty(existing["DISC"]))
            {
                string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "G";
                string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                disc = TagConfig.GetSystemAwareDisc(disc, sys, catName);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.DISC, disc);
            }

            // SYS — use the full 6-layer detection
            if (string.IsNullOrEmpty(existing["SYS"]))
            {
                string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.SYS, sys);
            }

            // FUNC
            if (string.IsNullOrEmpty(existing["FUNC"]))
            {
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                if (string.IsNullOrEmpty(sys))
                    sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                string func = TagConfig.GetSmartFuncCode(el, sys);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.FUNC, func);
            }

            // PROD
            if (string.IsNullOrEmpty(existing["PROD"]))
            {
                string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.PROD, prod);
            }

            // LVL — from element level
            if (string.IsNullOrEmpty(existing["LVL"]))
            {
                string lvl = ParameterHelpers.GetLevelCode(doc, el);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.LVL, lvl);
            }

            // LOC — spatial auto-detect
            if (string.IsNullOrEmpty(existing["LOC"]))
            {
                string loc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.LOC, loc);
            }

            // ZONE — spatial auto-detect
            if (string.IsNullOrEmpty(existing["ZONE"]))
            {
                string zone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.ZONE, zone);
            }
        }

        /// <summary>Mode C: Inherit system tokens from parent, derive spatial per-element.</summary>
        private static void PushHybrid(
            Document doc, Element el, Dictionary<string, string> existing,
            Dictionary<string, string> parent, string catName,
            Dictionary<ElementId, Room> roomIndex, string projectLoc)
        {
            // System-identity tokens: inherit from parent
            if (parent != null)
            {
                SetIfMissing(el, ParamRegistry.DISC, existing["DISC"], parent.GetValueOrDefault("DISC", ""));
                SetIfMissing(el, ParamRegistry.SYS, existing["SYS"], parent.GetValueOrDefault("SYS", ""));
                SetIfMissing(el, ParamRegistry.FUNC, existing["FUNC"], parent.GetValueOrDefault("FUNC", ""));
                SetIfMissing(el, ParamRegistry.STATUS, existing["STATUS"], parent.GetValueOrDefault("STATUS", ""));
            }

            // Spatial tokens: derive per-element (different elements can be in different zones)
            if (string.IsNullOrEmpty(existing["LVL"]))
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.LVL,
                    ParameterHelpers.GetLevelCode(doc, el));

            if (string.IsNullOrEmpty(existing["LOC"]))
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.LOC,
                    SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc));

            if (string.IsNullOrEmpty(existing["ZONE"]))
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.ZONE,
                    SpatialAutoDetect.DetectZone(doc, el, roomIndex));

            // PROD: derive per-element (different fittings have different codes)
            if (string.IsNullOrEmpty(existing["PROD"]))
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.PROD,
                    TagConfig.GetFamilyAwareProdCode(el, catName));
        }

        /// <summary>Mode D: Full auto-tag pipeline (populate + build tag).</summary>
        private static void PushFullAuto(
            Document doc, Element el, Dictionary<string, string> existing,
            string catName, Dictionary<ElementId, Room> roomIndex, string projectLoc)
        {
            // First derive all tokens
            PushDerive(doc, el, existing, catName, roomIndex, projectLoc);

            // Then map native MEP parameters
            NativeParamMapper.MapAll(doc, el);
        }

        /// <summary>Set a parameter only if the existing value is empty.</summary>
        private static void SetIfMissing(
            Element el, string paramName, string existingValue, string newValue)
        {
            if (string.IsNullOrEmpty(existingValue) && !string.IsNullOrEmpty(newValue))
                ParameterHelpers.SetIfEmpty(el, paramName, newValue);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // System Parameter Push Command — Interactive
    //
    // User selects element(s), command finds their system, discovers all
    // connected elements, and pushes parameters.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Push STING parameters to all elements in a selected element's MEP system.
    /// Discovers connected elements via MEPSystem API + connector graph traversal.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SystemParamPushCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            Document doc = uidoc.Document;

            // Get selected elements or prompt to select
            var selection = uidoc.Selection.GetElementIds();
            if (selection.Count == 0)
            {
                TaskDialog.Show("System Param Push",
                    "Select one or more elements in an MEP system, then run this command.\n\n" +
                    "The tool will discover all connected elements in the same system\n" +
                    "and push STING parameters to untagged components.");
                return Result.Cancelled;
            }

            // Discover all connected system elements
            var allSystemElements = new HashSet<ElementId>();
            var systemNames = new HashSet<string>();

            foreach (ElementId id in selection)
            {
                Element seed = doc.GetElement(id);
                if (seed == null) continue;

                var connected = SystemParamPush.GetSystemElements(doc, seed);
                foreach (var el in connected)
                    allSystemElements.Add(el.Id);

                string sysName = SystemParamPush.GetSystemName(seed);
                if (sysName != "Unassigned") systemNames.Add(sysName);
            }

            var systemElementList = allSystemElements
                .Select(id => doc.GetElement(id))
                .Where(e => e != null && e.Category != null)
                .ToList();

            if (systemElementList.Count == 0)
            {
                TaskDialog.Show("System Param Push",
                    "No connected MEP system elements found.\n\n" +
                    "Select an element that is part of an MEP system\n" +
                    "(duct, pipe, conduit, cable tray, or connected equipment).");
                return Result.Failed;
            }

            // Find best parent element (most tokens populated)
            var (parent, parentTokens) = SystemParamPush.FindBestParent(systemElementList);
            int parentTokenCount = SystemParamPush.CountPopulatedTokens(parentTokens);

            // Count untagged elements
            int untagged = systemElementList.Count(el =>
            {
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                return string.IsNullOrEmpty(tag) || !TagConfig.TagIsComplete(tag);
            });

            // Prompt for push mode
            var dlg = new TaskDialog("System Parameter Push");
            dlg.MainInstruction = $"{systemElementList.Count} elements in system" +
                (systemNames.Count > 0 ? $" ({string.Join(", ", systemNames)})" : "");
            dlg.MainContent =
                $"Already tagged: {systemElementList.Count - untagged}\n" +
                $"Need parameters: {untagged}\n\n" +
                (parent != null ? $"Best parent: {ParameterHelpers.GetCategoryName(parent)} " +
                    $"({parentTokenCount}/7 tokens)\n" +
                    $"  DISC={parentTokens.GetValueOrDefault("DISC", "—")} " +
                    $"SYS={parentTokens.GetValueOrDefault("SYS", "—")} " +
                    $"LOC={parentTokens.GetValueOrDefault("LOC", "—")}\n\n" : "") +
                "Choose parameter propagation mode:";

            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Hybrid (Recommended)",
                "Inherit DISC/SYS/FUNC/STATUS from parent; derive LOC/ZONE/LVL per-element spatially");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Inherit All from Parent",
                "Copy all token values from the most-complete element in the system");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Derive All from Context",
                "Auto-detect all tokens using 6-layer system detection + spatial context");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Full Auto-Tag",
                "Derive + map native params + build tags (complete pipeline)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            SystemParamPush.PushMode mode;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: mode = SystemParamPush.PushMode.Hybrid; break;
                case TaskDialogResult.CommandLink2: mode = SystemParamPush.PushMode.InheritFromParent; break;
                case TaskDialogResult.CommandLink3: mode = SystemParamPush.PushMode.DeriveFromContext; break;
                case TaskDialogResult.CommandLink4: mode = SystemParamPush.PushMode.FullAutoTag; break;
                default: return Result.Cancelled;
            }

            // Execute push
            SystemParamPush.PushResult result;
            using (Transaction tx = new Transaction(doc, "STING System Parameter Push"))
            {
                tx.Start();
                result = SystemParamPush.ExecutePush(doc, systemElementList, mode, parentTokens);

                // If FullAutoTag mode, also build tags
                if (mode == SystemParamPush.PushMode.FullAutoTag)
                {
                    var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
                    var existingTags = TagConfig.BuildExistingTagIndex(doc);
                    var stats = new TaggingStats();

                    foreach (var el in systemElementList)
                    {
                        string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        if (!string.IsNullOrEmpty(tag) && TagConfig.TagIsComplete(tag))
                            continue;

                        TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                            skipComplete: true, existingTags,
                            TagCollisionMode.AutoIncrement, stats);
                    }
                }

                tx.Commit();
            }

            result.SystemName = string.Join(", ", systemNames);

            // Report
            var report = new StringBuilder();
            report.AppendLine($"System Parameter Push — {mode}");
            report.AppendLine(new string('═', 45));
            report.AppendLine(result.BuildReport());

            // Select the pushed elements for visual feedback
            var pushedIds = systemElementList
                .Where(e => !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.DISC)))
                .Select(e => e.Id)
                .ToList();
            if (pushedIds.Count > 0)
            {
                uidoc.Selection.SetElementIds(pushedIds);
                report.AppendLine($"\nSelected {pushedIds.Count} elements for review.");
            }

            TaskDialog.Show("System Parameter Push", report.ToString());
            StingLog.Info($"SystemParamPush: {result.Pushed} pushed, mode={mode}, " +
                $"system={result.SystemName}");

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Batch System Scan — Find all systems and push to ALL at once
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scan the entire project for MEP systems with untagged elements
    /// and batch-push parameters across all systems at once.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchSystemPushCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            Document doc = uidoc.Document;

            // Discover all MEP systems in the project
            var allSystems = new FilteredElementCollector(doc)
                .OfClass(typeof(MEPSystem))
                .Cast<MEPSystem>()
                .ToList();

            if (allSystems.Count == 0)
            {
                TaskDialog.Show("Batch System Push",
                    "No MEP systems found in the project.\n\n" +
                    "Connect elements to duct/pipe/electrical systems first.");
                return Result.Succeeded;
            }

            // Audit each system for untagged elements
            int totalUntagged = 0;
            int totalSystems = 0;
            var systemSummary = new Dictionary<string, (int total, int untagged, string type)>();

            foreach (var sys in allSystems)
            {
                int total = 0;
                int untagged = 0;

                foreach (Element el in sys.Elements)
                {
                    total++;
                    string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag) || !TagConfig.TagIsComplete(tag))
                        untagged++;
                }

                if (untagged > 0)
                {
                    string key = sys.Name ?? $"System-{sys.Id}";
                    string sysType = sys is MechanicalSystem ? "HVAC" :
                        sys is PipingSystem ? "Piping" : "Electrical";
                    systemSummary[key] = (total, untagged, sysType);
                    totalUntagged += untagged;
                    totalSystems++;
                }
            }

            if (totalUntagged == 0)
            {
                TaskDialog.Show("Batch System Push",
                    $"All {allSystems.Count} MEP systems are fully tagged.\n" +
                    "No action needed.");
                return Result.Succeeded;
            }

            // Show summary and confirm
            var report = new StringBuilder();
            report.AppendLine($"Found {totalSystems} systems with {totalUntagged} untagged elements:\n");
            foreach (var kvp in systemSummary.OrderByDescending(x => x.Value.untagged).Take(10))
                report.AppendLine($"  {kvp.Key} ({kvp.Value.type}): " +
                    $"{kvp.Value.untagged}/{kvp.Value.total} untagged");
            if (systemSummary.Count > 10)
                report.AppendLine($"  ... and {systemSummary.Count - 10} more systems");

            var dlg = new TaskDialog("Batch System Push");
            dlg.MainInstruction = $"{totalUntagged} untagged elements across {totalSystems} systems";
            dlg.MainContent = report.ToString();
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Hybrid Push All (Recommended)",
                "Inherit system tokens from parent, derive spatial per-element");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Full Auto-Tag All",
                "Derive + build tags + assign SEQ numbers");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            SystemParamPush.PushMode mode;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: mode = SystemParamPush.PushMode.Hybrid; break;
                case TaskDialogResult.CommandLink2: mode = SystemParamPush.PushMode.FullAutoTag; break;
                default: return Result.Cancelled;
            }

            int totalPushed = 0;
            int totalSkipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Batch System Push"))
            {
                tx.Start();

                var seqCounters = mode == SystemParamPush.PushMode.FullAutoTag
                    ? TagConfig.GetExistingSequenceCounters(doc)
                    : null;
                var existingTags = mode == SystemParamPush.PushMode.FullAutoTag
                    ? TagConfig.BuildExistingTagIndex(doc)
                    : null;

                foreach (var sys in allSystems)
                {
                    var sysElements = sys.Elements.Cast<Element>().ToList();
                    if (sysElements.Count == 0) continue;

                    var (parent, parentTokens) = SystemParamPush.FindBestParent(sysElements);
                    var result = SystemParamPush.ExecutePush(
                        doc, sysElements, mode, parentTokens);

                    // Build tags in FullAutoTag mode
                    if (mode == SystemParamPush.PushMode.FullAutoTag)
                    {
                        var stats = new TaggingStats();
                        foreach (var el in sysElements)
                        {
                            string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            if (!string.IsNullOrEmpty(tag) && TagConfig.TagIsComplete(tag))
                                continue;

                            TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                                skipComplete: true, existingTags,
                                TagCollisionMode.AutoIncrement, stats);
                        }
                    }

                    totalPushed += result.Pushed;
                    totalSkipped += result.Skipped;
                }

                tx.Commit();
            }

            TaskDialog.Show("Batch System Push",
                $"Batch System Push Complete\n\n" +
                $"Systems processed: {totalSystems}\n" +
                $"Elements pushed: {totalPushed}\n" +
                $"Skipped: {totalSkipped}\n" +
                $"Mode: {mode}");

            StingLog.Info($"BatchSystemPush: {totalPushed} pushed across {totalSystems} systems, mode={mode}");

            return Result.Succeeded;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Select System Elements — Highlight all elements in a system
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Select all elements connected to the current selection's MEP system.
    /// Visual feedback for system parameter push — shows exactly what will be affected.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SelectSystemElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            Document doc = uidoc.Document;

            var selection = uidoc.Selection.GetElementIds();
            if (selection.Count == 0)
            {
                TaskDialog.Show("Select System",
                    "Select an element that is part of an MEP system,\n" +
                    "then run this command to select all connected elements.");
                return Result.Cancelled;
            }

            var allSystemElements = new HashSet<ElementId>();
            var systemNames = new HashSet<string>();

            foreach (ElementId id in selection)
            {
                Element seed = doc.GetElement(id);
                if (seed == null) continue;

                var connected = SystemParamPush.GetSystemElements(doc, seed);
                foreach (var el in connected)
                    allSystemElements.Add(el.Id);

                string sysName = SystemParamPush.GetSystemName(seed);
                if (sysName != "Unassigned") systemNames.Add(sysName);
            }

            if (allSystemElements.Count == 0)
            {
                TaskDialog.Show("Select System", "No connected system elements found.");
                return Result.Failed;
            }

            uidoc.Selection.SetElementIds(allSystemElements.ToList());

            // Count tagged vs untagged
            int tagged = 0, untagged = 0;
            foreach (var id in allSystemElements)
            {
                Element el = doc.GetElement(id);
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(tag) && TagConfig.TagIsComplete(tag))
                    tagged++;
                else
                    untagged++;
            }

            TaskDialog.Show("Select System",
                $"Selected {allSystemElements.Count} elements in " +
                $"{(systemNames.Count > 0 ? string.Join(", ", systemNames) : "connected system")}.\n\n" +
                $"Tagged: {tagged}\n" +
                $"Untagged: {untagged}\n\n" +
                "Use 'System Param Push' to populate untagged elements.");

            return Result.Succeeded;
        }
    }
}
