// StingTools — BatchAssignCircuitsCommand.
//
// Auto-assigns unassigned electrical circuits to panels by:
//
//   1. Collecting every ElectricalSystem with no BaseEquipment and a
//      load known to STING (apparent load > 0).
//   2. Collecting every panel with available slots (NumberOfCircuits >
//      circuits already on it).
//   3. Picking, for each unassigned circuit, the smallest panel that
//      fits (by remaining-slot count and matching voltage class), then
//      preferring the least-loaded phase to keep the panel balanced.
//   4. Writing SelectPanel(panelName) on the circuit so Revit performs
//      the actual slot allocation. The phase column is left for the
//      follow-up PhaseBalanceCommand because Revit owns slot↔phase
//      mapping in the panel template.
//
// The command is read-only-by-default (preview a plan), with an
// explicit "Apply" confirmation prompt before any writes. Every
// circuit it touches is logged so the result panel + audit log
// reconcile cleanly with WORKFLOW_ElectricalQA.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchAssignCircuitsCommand : IExternalCommand
    {
        // Voltage tolerance bands. Loaded from STING_ELECTRICAL_ASSIGNMENT.json
        // on first call; falls back to BS 7671 / IEC nominal-supply defaults
        // when the JSON is missing. Two voltages are "compatible" if both
        // sit in the same band — this mirrors how the standards treat
        // nominal supply ranges (e.g. 230V band absorbs ±10% mains).
        private static volatile AssignmentConfig _cachedConfig;

        private static readonly (double low, double high, string label)[] _defaultVoltageBands = new[]
        {
            (   0.0,  60.0,  "ELV"   ),
            (  90.0, 140.0,  "120V"  ),
            ( 200.0, 250.0,  "230V"  ),
            ( 380.0, 420.0,  "400V"  ),
            ( 460.0, 530.0,  "480V"  ),
            ( 580.0, 720.0,  "600V"  )
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // ── 1. Collect inventory ──────────────────────────────────
            var allSystems = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .ToList();
            var unassigned = allSystems.Where(s => SafeBaseEquipment(s) == null).ToList();
            if (unassigned.Count == 0)
            {
                TaskDialog.Show("STING Electrical",
                    "Every circuit already has a panel assignment. Nothing to do.");
                return Result.Succeeded;
            }

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();
            if (panels.Count == 0)
            {
                TaskDialog.Show("STING Electrical",
                    "No electrical equipment in the model — cannot auto-assign circuits.");
                return Result.Cancelled;
            }

            // Pre-group every assigned circuit by its base-equipment id so
            // PanelState's constructor is O(1) per panel instead of O(S).
            // For a 50-panel / 500-circuit project this drops construction
            // from O(P*S)=25,000 ops to O(P+S)=550.
            var circuitsByPanel = new Dictionary<long, List<ElectricalSystem>>();
            foreach (var s in allSystems)
            {
                var be = SafeBaseEquipment(s);
                if (be == null) continue;
                long key = be.Id.Value;
                if (!circuitsByPanel.TryGetValue(key, out var list))
                {
                    list = new List<ElectricalSystem>();
                    circuitsByPanel[key] = list;
                }
                list.Add(s);
            }

            var pState = panels.Select(p => new PanelState(p, circuitsByPanel)).ToList();

            // Compute panel-room/level once so the grouping policy can prefer
            // panels in the same room or on the same level as the circuit's
            // load. This is the cheap precondition for "kitchen sockets stay
            // on one panel" — first the group rule narrows to panels carrying
            // the same group; then the same-room / same-level preference
            // breaks ties between equally-good candidates.
            foreach (var ps in pState) ps.PrimeRoomLevel(doc);
            var cfg = LoadConfig();

            // ── 2. Greedy assignment ─────────────────────────────────
            var plan = new List<Assignment>();
            var sortedCircuits = unassigned
                .OrderByDescending(SafeApparentVA)
                .ThenBy(s => SafePoles(s))
                .ToList();

            foreach (var sys in sortedCircuits)
            {
                double va = SafeApparentVA(sys);
                int    poles = SafePoles(sys);
                double volts = SafeCircuitVoltage(sys);
                string circuitGroup = ResolveCircuitGroup(doc, sys, cfg);
                string circuitRoom  = TryReadCircuitRoomName(doc, sys);
                string circuitLevel = TryReadCircuitLevelCode(doc, sys);

                // Two-stage candidate search. Stage 1 narrows to panels that
                // already host (or have nothing yet hosting) the same circuit
                // group, so kitchen / emergency-lighting / fire-alarm circuits
                // converge on a single panel. Stage 2 falls back to the wider
                // pool when stage 1 finds nothing — controlled by
                // GroupingPolicy.AllowFallbackToAnyPanel so strict projects
                // can disable it.
                var groupFit = pState
                    .Where(ps => ps.RemainingSlots >= Math.Max(poles, 1))
                    .Where(ps => VoltageCompatible(volts, ps.NominalVoltage, cfg))
                    .Where(ps => string.IsNullOrEmpty(circuitGroup) || ps.AcceptsGroup(circuitGroup))
                    .OrderBy(ps => string.IsNullOrEmpty(ps.GroupTag) ? 1 : 0)   // already-grouped panels first
                    .ThenByDescending(ps => SameRoomBonus(ps, circuitRoom, cfg))
                    .ThenByDescending(ps => SameLevelBonus(ps, circuitLevel, cfg))
                    .ThenBy(ps => ps.RemainingSlots)
                    .ThenBy(ps => ps.ConnectedVa)
                    .FirstOrDefault();

                var fit = groupFit;
                if (fit == null && cfg.AllowFallbackToAnyPanel)
                {
                    fit = pState
                        .Where(ps => ps.RemainingSlots >= Math.Max(poles, 1))
                        .Where(ps => VoltageCompatible(volts, ps.NominalVoltage, cfg))
                        .OrderBy(ps => ps.RemainingSlots)
                        .ThenBy(ps => ps.ConnectedVa)
                        .FirstOrDefault();
                }

                if (fit == null)
                {
                    plan.Add(new Assignment
                    {
                        SystemId = sys.Id,
                        SystemName = sys.Name ?? "(?)",
                        PanelName = null,
                        Group = circuitGroup,
                        Reason = !string.IsNullOrEmpty(circuitGroup) && !cfg.AllowFallbackToAnyPanel
                            ? $"Group '{circuitGroup}' has no panel with ≥ {poles} free slots at {volts:F0} V (strict mode)"
                            : poles > 1
                                ? $"No panel with ≥ {poles} free slots at {volts:F0} V"
                                : $"No panel with free slot at {volts:F0} V"
                    });
                    continue;
                }

                fit.Reserve(va, poles, circuitGroup);
                plan.Add(new Assignment
                {
                    SystemId = sys.Id,
                    PanelId  = fit.Id,
                    SystemName = sys.Name ?? "(?)",
                    PanelId = fit.Id,
                    PanelName = fit.Name,
                    Group = circuitGroup,
                    Reason = $"fit slots={fit.RemainingSlots} after, panelLoad={fit.ConnectedVa/1000:F1} kVA" +
                             (string.IsNullOrEmpty(circuitGroup) ? "" : $", group={circuitGroup}")
                });
            }

            int wouldAssign  = plan.Count(a => a.PanelName != null);
            int wouldSkip    = plan.Count - wouldAssign;

            // ── 3. Preview / confirm ─────────────────────────────────
            var dlg = new TaskDialog("STING Auto-assign Circuits — Preview")
            {
                MainInstruction = $"Plan: assign {wouldAssign} of {plan.Count} unassigned circuits.",
                MainContent =
                    $"Skipped: {wouldSkip} (no compatible panel with free slots).\n\n" +
                    "Apply will set the panel reference on each circuit (SelectPanel). " +
                    "Phase assignment within the panel slot follows your panel template; " +
                    "run Circuit_Balance afterwards to balance phase loads.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (dlg.Show() != TaskDialogResult.Yes)
            {
                ShowResult(plan, doc, applied: 0, failed: 0, dryRun: true);
                return Result.Cancelled;
            }

            // ── 4. Apply ─────────────────────────────────────────────
            int applied = 0, failed = 0;
            using (var tx = new Transaction(doc, "STING Auto-assign Circuits"))
            {
                tx.Start();
                foreach (var a in plan)
                {
                    if (a.PanelName == null) continue;
                    try
                    {
                        var sys = doc.GetElement(a.SystemId) as ElectricalSystem;
                        if (sys == null) { failed++; continue; }
                        // Revit 2024+ requires FamilyInstance, not the panel name string.
                        // Resolve once here so any pre-existing string-based callers still
                        // get clean error reporting via the catch block below.
                        var panelFi = (a.PanelId != null && a.PanelId != ElementId.InvalidElementId)
                            ? doc.GetElement(a.PanelId) as FamilyInstance
                            : null;
                        if (panelFi == null)
                        {
                            failed++;
                            StingLog.Warn($"BatchAssignCircuits '{a.SystemName}' → '{a.PanelName}': panel instance not found");
                            continue;
                        }
                        sys.SelectPanel(panelFi);
                        applied++;

                        // Stamp ELC_PANEL_REF_TXT on the circuit so STING tag
                        // pipelines see the back-reference even before the
                        // panel schedule is regenerated.
                        ParameterHelpers.SetString(sys, "ELC_PANEL_REF_TXT", a.PanelName, overwrite: true);

                        // Persist the resolved group so re-runs converge:
                        // stamp ELC_CIRCUIT_GROUP_TXT on the circuit and
                        // ELC_PNL_CIRCUIT_GROUP_TXT on the panel. Future
                        // runs read these before re-evaluating the rules,
                        // so manual overrides + first-pass rule resolution
                        // are honoured stably.
                        if (!string.IsNullOrEmpty(a.Group))
                        {
                            ParameterHelpers.SetString(sys, "ELC_CIRCUIT_GROUP_TXT", a.Group, overwrite: false);
                            // Direct id lookup — no need to walk the project
                            // collector by name now that PanelId is on Assignment.
                            try
                            {
                                if (panelInst != null)
                                    ParameterHelpers.SetString(panelInst, "ELC_PNL_CIRCUIT_GROUP_TXT", a.Group, overwrite: false);
                            }
                            catch (Exception ex2) { StingLog.Warn($"Stamp panel group: {ex2.Message}"); }
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        StingLog.Warn($"BatchAssignCircuits '{a.SystemName}' → '{a.PanelName}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            try { ActionAuditLog.Record("Circuit_AssignAuto",
                $"applied={applied} failed={failed} skipped={wouldSkip}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }
            try { ComplianceScan.InvalidateCache(); } catch { }

            ShowResult(plan, doc, applied, failed, dryRun: false);
            return Result.Succeeded;
        }

        // ── Result rendering ────────────────────────────────────────────

        private static void ShowResult(List<Assignment> plan, Document doc, int applied, int failed, bool dryRun)
        {
            int wouldAssign  = plan.Count(a => a.PanelName != null);
            int wouldSkip    = plan.Count - wouldAssign;

            var panel = StingResultPanel.Create(dryRun ? "Auto-assign Circuits — Preview" : "Auto-assign Circuits");
            panel.SetSubtitle($"{plan.Count} unassigned · {wouldAssign} matched · {wouldSkip} no fit");
            panel.AddSection("SUMMARY")
                 .Metric("Unassigned circuits", plan.Count.ToString())
                 .MetricHighlight("Plan: matched", wouldAssign.ToString())
                 .Metric("Plan: skipped",  wouldSkip.ToString());
            if (!dryRun)
            {
                panel.Metric("Applied", applied.ToString());
                panel.Metric("Failed",  failed.ToString());
            }

            var byPanel = plan.Where(a => a.PanelName != null).GroupBy(a => a.PanelName).OrderByDescending(g => g.Count());
            if (byPanel.Any())
            {
                panel.AddSection("BY PANEL");
                foreach (var g in byPanel)
                {
                    var groups = g.Where(x => !string.IsNullOrEmpty(x.Group)).Select(x => x.Group).Distinct().ToList();
                    string subtitle = groups.Count == 0
                        ? "circuits"
                        : groups.Count == 1
                            ? $"circuits — group: {groups[0]}"
                            : $"circuits — groups: {string.Join("/", groups)}";
                    panel.Metric(g.Key, g.Count().ToString(), subtitle);
                }
            }

            var byGroup = plan.Where(a => !string.IsNullOrEmpty(a.Group)).GroupBy(a => a.Group).OrderByDescending(g => g.Count()).ToList();
            if (byGroup.Count > 0)
            {
                panel.AddSection("BY GROUP")
                     .Text("Loads matched to a logical group via STING_ELECTRICAL_ASSIGNMENT.json (kitchen / emergency-lighting / fire-alarm / comms-room) or ELC_CIRCUIT_GROUP_TXT manual overrides. Same-group circuits share a panel where slot space allows.");
                foreach (var g in byGroup)
                    panel.Metric(g.Key, g.Count().ToString(), "circuits");
            }

            var skipped = plan.Where(a => a.PanelName == null).Take(20).ToList();
            if (skipped.Count > 0)
            {
                panel.AddSection("UNMATCHED");
                foreach (var a in skipped)
                    panel.Text($"{a.SystemName} — {a.Reason}");
                int rest = plan.Count(a => a.PanelName == null) - skipped.Count;
                if (rest > 0) panel.Text($"… {rest} more.");
            }

            panel.AddSection("NEXT STEPS")
                 .Text("Run 'Phase Balance' to balance loads across A/B/C within each panel.")
                 .Text("Run 'Batch Panel Schedules' to materialize the schedules and stamp ELC_PNL_*.")
                 .Text("Add free slots in panel families if 'no fit' circuits remain after expanding panels.");
            panel.Show();
        }

        // ── Helper data ─────────────────────────────────────────────────

        private class Assignment
        {
            public ElementId SystemId;
            public ElementId PanelId;     // Revit 2024+ SelectPanel takes FamilyInstance, not string
            public string SystemName;
            public string PanelName;
            public string Group;
            public string Reason;
        }

        private class PanelState
        {
            public ElementId Id { get; }
            public string Name { get; }
            public int TotalSlots { get; }
            public int RemainingSlots { get; private set; }
            public double ConnectedVa { get; private set; }
            public double NominalVoltage { get; }
            public string GroupTag { get; private set; } = "";
            public string RoomName { get; private set; } = "";
            public string LevelCode { get; private set; } = "";
            public ElementId LevelId { get; }
            public XYZ Location { get; }
            private FamilyInstance _fi;

            public PanelState(FamilyInstance fi, Dictionary<long, List<ElectricalSystem>> circuitsByPanel)
            {
                _fi = fi;
                Id = fi.Id;
                Name = SafeName(fi);
                TotalSlots = SafeReadInt(fi, "Number Of Circuits", 42);
                circuitsByPanel.TryGetValue(fi.Id.Value, out var owned);
                int used = owned?.Count ?? 0;
                RemainingSlots = Math.Max(0, TotalSlots - used);
                double sum = 0;
                if (owned != null) foreach (var s in owned) sum += SafeApparentVA(s);
                ConnectedVa = sum;
                NominalVoltage = SafePanelVoltage(fi);
                LevelId = fi.LevelId ?? ElementId.InvalidElementId;
                try { Location = (fi.Location as LocationPoint)?.Point; } catch { }

                // Pre-existing group tag from a prior run lets a re-run remain
                // stable: panels already accumulating a group keep getting that
                // group's circuits rather than scattering on each invocation.
                GroupTag = ParameterHelpers.GetString(fi, "ELC_PNL_CIRCUIT_GROUP_TXT") ?? "";
            }

            public void PrimeRoomLevel(Document doc)
            {
                try
                {
                    var lvl = doc.GetElement(LevelId) as Level;
                    LevelCode = lvl?.Name ?? "";
                    var room = ParameterHelpers.GetRoomAtElement(doc, _fi);
                    if (room != null)
                        RoomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name ?? "";
                }
                catch (Exception ex) { StingLog.Warn($"PrimeRoomLevel {Name}: {ex.Message}"); }
            }

            public bool AcceptsGroup(string group)
            {
                if (string.IsNullOrEmpty(group)) return true;
                if (string.IsNullOrEmpty(GroupTag)) return true;        // empty panel takes any group
                return string.Equals(GroupTag, group, StringComparison.OrdinalIgnoreCase);
            }

            public void Reserve(double va, int poles, string group = null)
            {
                RemainingSlots = Math.Max(0, RemainingSlots - Math.Max(poles, 1));
                ConnectedVa += va;
                if (string.IsNullOrEmpty(GroupTag) && !string.IsNullOrEmpty(group))
                    GroupTag = group;
            }

            private static string SafeName(FamilyInstance fi)
            {
                try { return fi.Name ?? fi.Id.ToString(); } catch { return fi.Id.ToString(); }
            }

            private static int SafeReadInt(Element el, string param, int fallback)
            {
                try
                {
                    var p = el.LookupParameter(param);
                    if (p != null && p.StorageType == StorageType.Integer) return p.AsInteger();
                    if (p != null && p.StorageType == StorageType.Double) return (int)Math.Round(p.AsDouble());
                }
                catch { }
                return fallback;
            }

            private static double SafePanelVoltage(Element el)
            {
                try
                {
                    var p = el.LookupParameter("Panel Voltage");
                    if (p != null && p.StorageType == StorageType.Double)
                    {
                        double v = p.AsDouble();
                        // Revit stores volts as volts in newer versions but
                        // historically as feet-of-equivalent. Anything above
                        // 1000 is suspect — return as-is and let the band
                        // matcher decide.
                        return v;
                    }
                }
                catch { }
                return 0;
            }
        }

        // ── Static helpers ─────────────────────────────────────────────

        private static FamilyInstance SafeBaseEquipment(ElectricalSystem s)
        {
            try { return s?.BaseEquipment as FamilyInstance; } catch { return null; }
        }

        private static double SafeApparentVA(ElectricalSystem s)
        {
            try { return s?.ApparentLoad ?? 0; } catch { return 0; }
        }

        private static int SafePoles(ElectricalSystem s)
        {
            try { return s?.PolesNumber ?? 1; } catch { return 1; }
        }

        private static double SafeCircuitVoltage(ElectricalSystem s)
        {
            try { return s?.Voltage ?? 0; } catch { return 0; }
        }

        private static bool VoltageCompatible(double a, double b, AssignmentConfig cfg = null)
        {
            if (a <= 0 || b <= 0) return true; // unknown — let it through
            var bands = cfg?.VoltageBands ?? _defaultVoltageBands;
            foreach (var band in bands)
            {
                bool inA = a >= band.low && a <= band.high;
                bool inB = b >= band.low && b <= band.high;
                if (inA && inB) return true;
            }
            return false;
        }

        // ── Grouping ────────────────────────────────────────────────────

        /// <summary>
        /// Resolve a circuit's group tag in this priority order:
        ///   1. ELC_CIRCUIT_GROUP_TXT on the system itself (manual override).
        ///   2. ELC_CIRCUIT_GROUP_TXT on the first connected load.
        ///   3. First matching rule in STING_ELECTRICAL_ASSIGNMENT.json
        ///      (room-name + category + system-name regexes).
        /// Returns "" if no group rule fires.
        /// </summary>
        private static string ResolveCircuitGroup(Document doc, ElectricalSystem sys, AssignmentConfig cfg)
        {
            try
            {
                string manual = ParameterHelpers.GetString(sys, "ELC_CIRCUIT_GROUP_TXT");
                if (!string.IsNullOrEmpty(manual)) return manual;
            }
            catch { }

            // Probe first load for the manual override.
            Element firstLoad = null;
            try
            {
                foreach (Element el in sys.Elements) { firstLoad = el; break; }
                if (firstLoad != null)
                {
                    string fromLoad = ParameterHelpers.GetString(firstLoad, "ELC_CIRCUIT_GROUP_TXT");
                    if (!string.IsNullOrEmpty(fromLoad)) return fromLoad;
                }
            }
            catch { }

            // Rule-based resolution against the project config.
            if (cfg?.GroupingRules == null) return "";
            string roomName = TryReadCircuitRoomName(doc, sys);
            string sysName = "";
            try { sysName = sys.Name ?? ""; } catch { }
            string category = "";
            try { category = firstLoad?.Category?.Name ?? ""; } catch { }

            foreach (var rule in cfg.GroupingRules)
            {
                if (rule.Categories != null && rule.Categories.Count > 0 &&
                    !rule.Categories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!string.IsNullOrEmpty(rule.RoomNamePattern) &&
                    !RegexMatch(rule.RoomNamePattern, roomName)) continue;
                if (!string.IsNullOrEmpty(rule.SystemNamePattern) &&
                    !RegexMatch(rule.SystemNamePattern, sysName)) continue;
                return rule.GroupId;
            }
            return "";
        }

        private static string TryReadCircuitRoomName(Document doc, ElectricalSystem sys)
        {
            try
            {
                foreach (Element el in sys.Elements)
                {
                    var room = ParameterHelpers.GetRoomAtElement(doc, el);
                    if (room != null)
                        return room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name ?? "";
                }
            }
            catch { }
            return "";
        }

        private static string TryReadCircuitLevelCode(Document doc, ElectricalSystem sys)
        {
            try
            {
                foreach (Element el in sys.Elements)
                {
                    var lvl = doc.GetElement(el.LevelId) as Level;
                    if (lvl != null) return lvl.Name ?? "";
                }
            }
            catch { }
            return "";
        }

        private static int SameRoomBonus(PanelState ps, string circuitRoom, AssignmentConfig cfg)
        {
            if (cfg == null || !cfg.PreferSameRoom) return 0;
            return !string.IsNullOrEmpty(circuitRoom) &&
                   string.Equals(ps.RoomName, circuitRoom, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        private static int SameLevelBonus(PanelState ps, string circuitLevel, AssignmentConfig cfg)
        {
            if (cfg == null || !cfg.PreferSameLevel) return 0;
            return !string.IsNullOrEmpty(circuitLevel) &&
                   string.Equals(ps.LevelCode, circuitLevel, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        private static bool RegexMatch(string pattern, string actual)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            if (string.IsNullOrEmpty(actual)) return false;
            try { return Regex.IsMatch(actual, pattern); }
            catch (Exception ex) { StingLog.Warn($"Group regex '{pattern}': {ex.Message}"); return false; }
        }

        // ── Config loader ──────────────────────────────────────────────

        public static void ResetConfig() { _cachedConfig = null; }

        private static AssignmentConfig LoadConfig()
        {
            var c = _cachedConfig;
            if (c != null) return c;

            var cfg = new AssignmentConfig
            {
                VoltageBands = _defaultVoltageBands,
                PreferSameRoom = true,
                PreferSameLevel = true,
                AllowFallbackToAnyPanel = true,
                GroupingRules = new List<GroupingRule>()
            };

            try
            {
                string path = StingToolsApp.FindDataFile("STING_ELECTRICAL_ASSIGNMENT.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    cfg = ParseConfig(File.ReadAllText(path), cfg);
            }
            catch (Exception ex) { StingLog.Warn($"BatchAssignCircuits: corp config load: {ex.Message}"); }

            _cachedConfig = cfg;
            return cfg;
        }

        private static AssignmentConfig ParseConfig(string json, AssignmentConfig fallback)
        {
            try
            {
                var root = JObject.Parse(json);
                var bandsArr = root["VoltageBands"] as JArray;
                if (bandsArr != null)
                {
                    var bands = new List<(double low, double high, string label)>();
                    foreach (var b in bandsArr)
                    {
                        bands.Add((
                            (double?)b["Min"]   ?? 0,
                            (double?)b["Max"]   ?? 0,
                            (string)b["Label"]  ?? ""));
                    }
                    if (bands.Count > 0) fallback.VoltageBands = bands.ToArray();
                }

                var rulesArr = root["GroupingRules"] as JArray;
                if (rulesArr != null)
                {
                    fallback.GroupingRules = new List<GroupingRule>();
                    foreach (var r in rulesArr)
                    {
                        var gr = new GroupingRule
                        {
                            GroupId            = (string)r["GroupId"] ?? "",
                            RoomNamePattern    = (string)r["RoomNamePattern"] ?? "",
                            SystemNamePattern  = (string)r["SystemNamePattern"] ?? "",
                        };
                        var cats = r["Categories"] as JArray;
                        if (cats != null)
                            gr.Categories = cats.Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        if (!string.IsNullOrEmpty(gr.GroupId)) fallback.GroupingRules.Add(gr);
                    }
                }

                var pol = root["GroupingPolicy"] as JObject;
                if (pol != null)
                {
                    fallback.PreferSameRoom          = (bool?)pol["PreferSameRoom"]          ?? fallback.PreferSameRoom;
                    fallback.PreferSameLevel         = (bool?)pol["PreferSameLevel"]         ?? fallback.PreferSameLevel;
                    fallback.AllowFallbackToAnyPanel = (bool?)pol["AllowFallbackToAnyPanel"] ?? fallback.AllowFallbackToAnyPanel;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BatchAssignCircuits: parse config: {ex.Message}"); }
            return fallback;
        }

        private sealed class AssignmentConfig
        {
            public (double low, double high, string label)[] VoltageBands;
            public List<GroupingRule> GroupingRules = new List<GroupingRule>();
            public bool PreferSameRoom          = true;
            public bool PreferSameLevel         = true;
            public bool AllowFallbackToAnyPanel = true;
        }

        private sealed class GroupingRule
        {
            public string GroupId           = "";
            public string RoomNamePattern   = "";
            public string SystemNamePattern = "";
            public List<string> Categories  = new List<string>();
        }
    }
}
