using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Validation;

namespace StingTools.Commands.Validation
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (B4) — Device Coordination audit command (A1 §7).
    //
    // Coordinates device locations against doors, casework, art/specialty and
    // decorative lighting using bounding-box + wall-host geometry. The pure math
    // lives in Core/Validation/DeviceCoordination.cs (unit-tested); this command
    // builds the AABBs from Revit and dispatches per rule type.
    // ─────────────────────────────────────────────────────────────────────────

    internal static class DeviceCoordRegistry
    {
        private static readonly ConcurrentDictionary<string, DeviceCoordRulePack> _cache
            = new ConcurrentDictionary<string, DeviceCoordRulePack>(StringComparer.OrdinalIgnoreCase);

        public static DeviceCoordRulePack Get(Document doc)
        {
            string key = DocKey(doc);
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload(Document doc) => _cache.TryRemove(DocKey(doc), out _);

        private static string DocKey(Document doc)
        {
            try { return Path.GetDirectoryName(doc?.PathName ?? "") ?? ""; } catch { return ""; }
        }

        private static DeviceCoordRulePack Load(Document doc)
        {
            DeviceCoordRulePack pack = null;
            try
            {
                string corp = StingToolsApp.FindDataFile("STING_DEVICE_COORD_RULES.json");
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                    pack = JsonConvert.DeserializeObject<DeviceCoordRulePack>(File.ReadAllText(corp));
            }
            catch (Exception ex) { StingLog.Warn($"DeviceCoord corporate load: {ex.Message}"); }
            pack = pack ?? new DeviceCoordRulePack();

            try
            {
                string dir = DocKey(doc);
                if (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, "_BIM_COORD", "device_coord_rules.json");
                    if (File.Exists(p))
                    {
                        var overlay = JsonConvert.DeserializeObject<DeviceCoordRulePack>(File.ReadAllText(p));
                        foreach (var r in overlay?.Rules ?? new List<DeviceCoordRule>())
                        {
                            if (string.IsNullOrWhiteSpace(r?.Id)) continue;
                            pack.Rules.RemoveAll(x => string.Equals(x.Id, r.Id, StringComparison.OrdinalIgnoreCase));
                            pack.Rules.Add(r);
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DeviceCoord overlay load: {ex.Message}"); }
            return pack;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DeviceCoordinationCommand : IExternalCommand
    {
        private const double FtToMm = 304.8;

        private class Dev
        {
            public Element El; public Aabb Box; public long WallId; public string RoomKey;
        }
        private class Finding
        {
            public string RuleId; public string Severity; public string Room; public long DeviceId; public string Detail;
        }

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            DeviceCoordRegistry.Reload(doc);
            var pack = DeviceCoordRegistry.Get(doc);
            if (pack.Rules == null || pack.Rules.Count == 0)
            {
                TaskDialog.Show("Device Coordination",
                    "No device-coordination rules found. Ship STING_DEVICE_COORD_RULES.json in data/ " +
                    "or add _BIM_COORD/device_coord_rules.json.");
                return Result.Succeeded;
            }

            var findings = new List<Finding>();
            foreach (var rule in pack.Rules)
            {
                try { EvaluateRule(doc, rule, findings); }
                catch (Exception ex) { StingLog.Warn($"DeviceCoord rule '{rule.Id}': {ex.Message}"); }
            }

            string csv = WriteCsv(doc, findings);

            int block = findings.Count(f => f.Severity == "BLOCK");
            int warn = findings.Count(f => f.Severity == "WARN");
            int info = findings.Count(f => f.Severity == "INFO");
            var byRoom = findings.GroupBy(f => f.Room).OrderByDescending(g => g.Count());

            var sb = new StringBuilder();
            sb.AppendLine($"{findings.Count} finding(s)  —  BLOCK {block}, WARN {warn}, INFO {info}");
            sb.AppendLine($"Rules evaluated: {pack.Rules.Count}");
            sb.AppendLine();
            sb.AppendLine("By room (top 10):");
            foreach (var g in byRoom.Take(10))
                sb.AppendLine($"   {g.Count(),4}  {g.Key}");
            sb.AppendLine();
            sb.AppendLine("First findings:");
            foreach (var f in findings.OrderByDescending(x => Sev(x.Severity)).Take(20))
                sb.AppendLine($"   [{f.Severity}] {f.RuleId} — {f.DeviceId} ({f.Room}): {f.Detail}");
            if (csv != null) { sb.AppendLine(); sb.AppendLine($"CSV: {csv}"); }

            new TaskDialog("Device Coordination Audit")
            {
                MainInstruction = findings.Count == 0
                    ? "No device-coordination issues found"
                    : $"{findings.Count} issue(s): {warn} WARN, {info} INFO",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"DeviceCoord_Audit: {findings.Count} findings ({block}/{warn}/{info})");
            return Result.Succeeded;
        }

        private static int Sev(string s) => s == "BLOCK" ? 3 : s == "WARN" ? 2 : 1;

        private void EvaluateRule(Document doc, DeviceCoordRule rule, List<Finding> findings)
        {
            var devices = CollectDevices(doc, rule.DeviceCategories);
            if (devices.Count == 0) return;
            string type = (rule.Type ?? "clearance").Trim();

            if (type == "mountingHeight")
            {
                foreach (var grp in devices.GroupBy(d => d.RoomKey))
                {
                    var list = grp.ToList();
                    var zs = list.Select(d => d.Box.CenterZ).ToList();
                    foreach (int idx in DeviceCoordination.MountingHeightOutliers(zs, rule.AlignmentToleranceMm))
                    {
                        double median = DeviceCoordination.Median(zs);
                        findings.Add(new Finding
                        {
                            RuleId = rule.Id, Severity = rule.Severity.ToUpperInvariant(), Room = grp.Key,
                            DeviceId = list[idx].El.Id.Value,
                            Detail = $"Z {list[idx].Box.CenterZ:F0} mm vs room median {median:F0} mm (±{rule.AlignmentToleranceMm:F0})"
                        });
                    }
                }
                return;
            }

            var obstacles = CollectDevices(doc, rule.AgainstCategories);

            if (type == "doorSwing")
            {
                var doors = obstacles; // againstCategories = Doors
                foreach (var dev in devices)
                {
                    foreach (var door in doors)
                    {
                        if (!(door.El is FamilyInstance fi)) continue;
                        XYZ facing = SafeOrient(() => fi.FacingOrientation);
                        XYZ hand = SafeOrient(() => fi.HandOrientation);
                        if (facing == null || hand == null) continue;
                        double gap = dev.Box.PlanarGapMm(door.Box);
                        if (gap >= rule.MinClearanceMm) continue;
                        bool swing = DeviceCoordination.OnSwingSide(door.Box.CenterX, door.Box.CenterY, facing.X, facing.Y, dev.Box.CenterX, dev.Box.CenterY);
                        bool handSide = DeviceCoordination.OnSwingSide(door.Box.CenterX, door.Box.CenterY, hand.X, hand.Y, dev.Box.CenterX, dev.Box.CenterY);
                        if (swing && handSide)
                        {
                            findings.Add(new Finding
                            {
                                RuleId = rule.Id, Severity = rule.Severity.ToUpperInvariant(), Room = dev.RoomKey,
                                DeviceId = dev.El.Id.Value,
                                Detail = $"on swing side of door {door.El.Id.Value}, gap {gap:F0} mm < {rule.MinClearanceMm:F0}"
                            });
                            break;
                        }
                    }
                }
                return;
            }

            // clearance / overlap
            foreach (var dev in devices)
            {
                foreach (var ob in obstacles)
                {
                    if (rule.SameWallOnly && (dev.WallId == 0 || dev.WallId != ob.WallId)) continue;
                    bool violation;
                    string detail;
                    if (type == "overlap")
                    {
                        violation = dev.Box.OverlapsXY(ob.Box);
                        detail = $"overlaps {ob.El.Id.Value}" + (rule.SameWallOnly ? " (same wall)" : "");
                    }
                    else // clearance
                    {
                        double gap = dev.Box.PlanarGapMm(ob.Box);
                        violation = gap < rule.MinClearanceMm;
                        detail = $"gap {gap:F0} mm to {ob.El.Id.Value} < {rule.MinClearanceMm:F0}";
                    }
                    if (violation)
                    {
                        findings.Add(new Finding
                        {
                            RuleId = rule.Id, Severity = rule.Severity.ToUpperInvariant(), Room = dev.RoomKey,
                            DeviceId = dev.El.Id.Value, Detail = detail
                        });
                        break; // one finding per device per rule
                    }
                }
            }
        }

        private List<Dev> CollectDevices(Document doc, List<string> categories)
        {
            var result = new List<Dev>();
            if (categories == null || categories.Count == 0) return result;
            var wanted = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
            foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(e => e.Category != null))
            {
                if (!wanted.Contains(ParameterHelpers.GetCategoryName(el))) continue;
                var bb = el.get_BoundingBox(null);
                if (bb == null) continue;
                var box = new Aabb
                {
                    MinX = bb.Min.X * FtToMm, MinY = bb.Min.Y * FtToMm, MinZ = bb.Min.Z * FtToMm,
                    MaxX = bb.Max.X * FtToMm, MaxY = bb.Max.Y * FtToMm, MaxZ = bb.Max.Z * FtToMm,
                };
                long wallId = 0;
                if (el is FamilyInstance fi && fi.Host is Wall w) wallId = w.Id.Value;
                string roomKey;
                try
                {
                    var room = ParameterHelpers.GetRoomAtElement(doc, el);
                    roomKey = room != null ? $"{room.Number} {room.Name}".Trim() : "(no room)";
                }
                catch { roomKey = "(no room)"; }
                result.Add(new Dev { El = el, Box = box, WallId = wallId, RoomKey = roomKey });
            }
            return result;
        }

        private static XYZ SafeOrient(Func<XYZ> f)
        {
            try { return f(); } catch { return null; }
        }

        private static string WriteCsv(Document doc, List<Finding> findings)
        {
            if (findings.Count == 0) return null;
            try
            {
                var rows = new List<string> { "RuleId,Severity,Room,DeviceId,Detail" };
                foreach (var f in findings)
                    rows.Add(string.Join(",", Csv(f.RuleId), Csv(f.Severity), Csv(f.Room), f.DeviceId, Csv(f.Detail)));
                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_DeviceCoord_Audit_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"DeviceCoord CSV: {ex.Message}"); return null; }
        }

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
