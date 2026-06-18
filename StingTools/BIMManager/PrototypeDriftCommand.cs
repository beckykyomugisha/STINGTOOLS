using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.Select;

namespace StingTools.BIMManager
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (E1) — Prototype drift report.
    //
    // The temple adapts an Owner prototype; reviewers repeatedly ask "what
    // changed vs the prototype?". This compares the current model against a
    // prototype (a loaded RVT link or a second open document) at TYPE-LEVEL
    // grain (category + type name) — element-level GUID matching across detached
    // prototypes is unreliable, so type-level is the honest grain (stated in the
    // output). Reports added/removed types, instance-count + key-dimension drift
    // on matching types, and room program deltas. XLSX grouped by discipline.
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PrototypeDriftCommand : IExternalCommand
    {
        private const double SqFtToM2 = 0.09290304;
        private static readonly string[] DimParams =
            { "Width", "Height", "Thickness", "Depth", "Diameter", "Length", "Default Thickness" };

        private class TypeInfo { public int Count; public Dictionary<string, double> Dims = new Dictionary<string, double>(); }
        private class DriftRow
        {
            public string Discipline, Category, Item, Kind, Current, Prototype, Delta;
        }

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            UIApplication uiapp = cmd.Application;

            // Candidate prototypes: loaded links + other open documents.
            var candidates = new List<(string label, Document d)>();
            foreach (var li in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                var ld = li.GetLinkDocument();
                if (ld != null && ld.IsValidObject) candidates.Add(($"[Link] {ld.Title}", ld));
            }
            foreach (Document od in uiapp.Application.Documents)
            {
                if (od == null || !od.IsValidObject || od.IsLinked) continue;
                if (od.Equals(doc)) continue;
                candidates.Add(($"[Open] {od.Title}", od));
            }
            // de-dup by title
            candidates = candidates.GroupBy(c => c.label).Select(g => g.First()).ToList();
            if (candidates.Count == 0)
            {
                TaskDialog.Show("Prototype Drift",
                    "No prototype available. Load the prototype as a Revit link, or open it as a second document, then re-run.");
                return Result.Succeeded;
            }

            string pick = StingListPicker.Show("Prototype Drift — pick the prototype",
                "Compare the current model against this prototype (type-level grain).",
                candidates.Select(c => c.label).ToList());
            if (string.IsNullOrEmpty(pick)) return Result.Cancelled;
            var proto = candidates.First(c => c.label == pick).d;

            var cur = BuildTypeIndex(doc);
            var pro = BuildTypeIndex(proto);
            var curRooms = BuildRoomIndex(doc);
            var proRooms = BuildRoomIndex(proto);

            var rows = new List<DriftRow>();
            int added = 0, removed = 0, countChanged = 0, dimChanged = 0, roomDelta = 0;

            // Type-level diff
            var allKeys = new HashSet<(string, string)>(cur.Keys);
            allKeys.UnionWith(pro.Keys);
            foreach (var key in allKeys)
            {
                cur.TryGetValue(key, out var c);
                pro.TryGetValue(key, out var p);
                string disc = Discipline(key.Item1);
                if (c != null && p == null)
                {
                    added++;
                    rows.Add(new DriftRow { Discipline = disc, Category = key.Item1, Item = key.Item2, Kind = "TYPE_ADDED", Current = c.Count.ToString(), Prototype = "0", Delta = $"+{c.Count}" });
                }
                else if (c == null && p != null)
                {
                    removed++;
                    rows.Add(new DriftRow { Discipline = disc, Category = key.Item1, Item = key.Item2, Kind = "TYPE_REMOVED", Current = "0", Prototype = p.Count.ToString(), Delta = $"-{p.Count}" });
                }
                else if (c != null && p != null)
                {
                    if (c.Count != p.Count)
                    {
                        countChanged++;
                        rows.Add(new DriftRow { Discipline = disc, Category = key.Item1, Item = key.Item2, Kind = "COUNT_CHANGED", Current = c.Count.ToString(), Prototype = p.Count.ToString(), Delta = (c.Count - p.Count).ToString("+0;-0;0") });
                    }
                    foreach (var dp in DimParams)
                    {
                        bool hc = c.Dims.TryGetValue(dp, out double cv);
                        bool hp = p.Dims.TryGetValue(dp, out double pv);
                        if (hc && hp && Math.Abs(cv - pv) > 1e-6)
                        {
                            dimChanged++;
                            rows.Add(new DriftRow { Discipline = disc, Category = key.Item1, Item = $"{key.Item2} · {dp}", Kind = "DIM_CHANGED", Current = Fmt(cv), Prototype = Fmt(pv), Delta = Fmt(cv - pv) });
                        }
                    }
                }
            }

            // Room program diff (by normalised name)
            var roomKeys = new HashSet<string>(curRooms.Keys);
            roomKeys.UnionWith(proRooms.Keys);
            foreach (var rk in roomKeys)
            {
                curRooms.TryGetValue(rk, out var c);
                proRooms.TryGetValue(rk, out var p);
                double cArea = c.area, pArea = p.area;
                int cCount = c.count, pCount = p.count;
                if (cCount != pCount || Math.Abs(cArea - pArea) > 0.5)
                {
                    roomDelta++;
                    rows.Add(new DriftRow
                    {
                        Discipline = "Architectural", Category = "Rooms", Item = rk, Kind = "ROOM_DELTA",
                        Current = $"{cCount}× {cArea:F1} m²", Prototype = $"{pCount}× {pArea:F1} m²",
                        Delta = $"{(cCount - pCount):+0;-0;0} / {(cArea - pArea):+0.0;-0.0;0} m²"
                    });
                }
            }

            string xlsx = WriteXlsx(doc, proto.Title, rows);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Prototype: {proto.Title}");
            sb.AppendLine($"Matching grain: category + type name (element-GUID matching across detached prototypes is unreliable).");
            sb.AppendLine();
            sb.AppendLine($"Types added:    {added}");
            sb.AppendLine($"Types removed:  {removed}");
            sb.AppendLine($"Count changed:  {countChanged}");
            sb.AppendLine($"Dim changed:    {dimChanged}");
            sb.AppendLine($"Room deltas:    {roomDelta}");
            if (xlsx != null) { sb.AppendLine(); sb.AppendLine($"XLSX: {xlsx}"); }

            new TaskDialog("Prototype Drift")
            {
                MainInstruction = $"{rows.Count} drift row(s) vs {proto.Title}",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"PrototypeDrift_Report: {rows.Count} rows vs {proto.Title}");
            return Result.Succeeded;
        }

        private static Dictionary<(string, string), TypeInfo> BuildTypeIndex(Document doc)
        {
            var index = new Dictionary<(string, string), TypeInfo>();
            // instance counts per type
            var counts = new Dictionary<ElementId, int>();
            foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(x => x.Category != null))
            {
                var tid = e.GetTypeId();
                if (tid == null || tid == ElementId.InvalidElementId) continue;
                counts.TryGetValue(tid, out int n); counts[tid] = n + 1;
            }
            foreach (var et in new FilteredElementCollector(doc).WhereElementIsElementType().Where(x => x.Category != null))
            {
                string cat = ParameterHelpers.GetCategoryName(et);
                if (string.IsNullOrEmpty(cat)) continue;
                string typeName = et.Name ?? "";
                if (string.IsNullOrEmpty(typeName)) continue;
                var key = (cat, typeName);
                if (!index.TryGetValue(key, out var ti)) { ti = new TypeInfo(); index[key] = ti; }
                counts.TryGetValue(et.Id, out int c); ti.Count += c;
                foreach (var dp in DimParams)
                {
                    if (ti.Dims.ContainsKey(dp)) continue;
                    var p = et.LookupParameter(dp);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                        ti.Dims[dp] = p.AsDouble() * 304.8; // ft → mm
                }
            }
            return index;
        }

        private static Dictionary<string, (int count, double area)> BuildRoomIndex(Document doc)
        {
            var d = new Dictionary<string, (int, double)>(StringComparer.Ordinal);
            foreach (var e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType())
            {
                if (!(e is Room room) || room.Area <= 1e-6) continue;
                string key = Norm(room.Name);
                if (key.Length == 0) continue;
                d.TryGetValue(key, out var v);
                d[key] = (v.Item1 + 1, v.Item2 + room.Area * SqFtToM2);
            }
            return d;
        }

        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private static string Discipline(string category)
        {
            if (TagConfig.DiscMap.TryGetValue(category, out string code))
            {
                switch (code)
                {
                    case "A": return "Architectural";
                    case "S": return "Structural";
                    case "M": return "Mechanical";
                    case "E": return "Electrical";
                    case "P": return "Plumbing";
                    case "FP": return "Fire Protection";
                    case "LV": return "Comms / LV";
                    default: return code;
                }
            }
            return "Other";
        }

        private static string Fmt(double mm) => Math.Round(mm, 1).ToString();

        private static string WriteXlsx(Document doc, string protoTitle, List<DriftRow> rows)
        {
            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("Prototype Drift");
                ws.Cell(1, 1).Value = $"Prototype drift vs {protoTitle} — type-level grain (category + type name)";
                ws.Cell(1, 1).Style.Font.Bold = true;
                string[] h = { "Discipline", "Category", "Item", "Kind", "Current", "Prototype", "Delta" };
                for (int c = 0; c < h.Length; c++) { ws.Cell(2, c + 1).Value = h[c]; ws.Cell(2, c + 1).Style.Font.Bold = true; }
                int r = 3;
                foreach (var x in rows.OrderBy(a => a.Discipline).ThenBy(a => a.Category).ThenBy(a => a.Item))
                {
                    ws.Cell(r, 1).Value = x.Discipline;
                    ws.Cell(r, 2).Value = x.Category;
                    ws.Cell(r, 3).Value = x.Item;
                    ws.Cell(r, 4).Value = x.Kind;
                    ws.Cell(r, 5).Value = x.Current;
                    ws.Cell(r, 6).Value = x.Prototype;
                    ws.Cell(r, 7).Value = x.Delta;
                    r++;
                }
                ws.Columns().AdjustToContents();
                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_PrototypeDrift_{DateTime.Now:yyyyMMdd}.xlsx");
                wb.SaveAs(path);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"PrototypeDrift XLSX: {ex.Message}"); return null; }
        }
    }
}
