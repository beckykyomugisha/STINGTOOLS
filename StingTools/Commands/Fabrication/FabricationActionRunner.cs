// StingTools v4 MVP — Fabrication action runner.
//
// Splits the four Fabrication actions (Generate Package / Cut List /
// Weld Map / Isometrics) into a pair per action:
//
//   • BuildXxxRows(doc, ids)  — builds preview POCOs the
//     FabricationWorkspaceDialog binds into a DataGrid / ListBox.
//   • RunXxx(uidoc, rows)     — runs the action over the rows the user
//     kept ticked in the dialog.
//
// The older IExternalCommands (GenerateFabPackageCommand,
// ExportCutListCommand, …) still exist for ribbon / shortcut paths,
// but they now delegate to the runner so the behaviour is identical
// whether the user launches from the workspace dialog or from the old
// direct buttons.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Fabrication;
using StingTools.UI;

namespace StingTools.Commands.Fabrication
{
    // ── Preview row POCOs ─────────────────────────────────────

    public class CutListRow
    {
        public bool Include { get; set; } = true;
        public long ElementId { get; set; }
        public string System { get; set; } = "";
        public double SizeMm { get; set; }
        public double LengthMm { get; set; }
        public string Material { get; set; } = "";
        public double MitreAngleDeg { get; set; }
    }

    public class WeldMapRow
    {
        public bool Include { get; set; } = true;
        public long ElementId { get; set; }
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public string WeldType { get; set; } = "";
        public string SizeMm { get; set; } = "";
        public string Schedule { get; set; } = "";
    }

    public class PackageGroupRow
    {
        public bool Include { get; set; } = true;
        public string Discipline { get; set; } = "";
        public string System { get; set; } = "";
        public string Level { get; set; } = "";
        public int ElementCount { get; set; }
        public string AssemblyNamePreview { get; set; } = "";
    }

    public class IsoSheetRow
    {
        public bool Include { get; set; } = true;
        public long SheetId { get; set; }
        public string SheetNumber { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class PcfSystemRow
    {
        public bool Include { get; set; } = true;
        public string System { get; set; } = "";
        public int PipeCount { get; set; }
        public int FittingCount { get; set; }
        public int AccessoryCount { get; set; }
    }

    public class MajFabRow
    {
        public bool Include { get; set; } = true;
        public long ElementId { get; set; }
        public string Category { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public string PartName { get; set; } = "";
    }

    // ── Runner ────────────────────────────────────────────────

    public static class FabricationActionRunner
    {
        // ── Cut list ──────────────────────────────────────────

        public static List<CutListRow> BuildCutListRows(Document doc, IList<ElementId> ids)
        {
            var rows = new List<CutListRow>();
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                if (!(el is Autodesk.Revit.DB.Plumbing.Pipe p)) continue;
                double diaFt; try { diaFt = p.Diameter; } catch { diaFt = 0; }
                double lenFt; try { lenFt = ((p.Location as LocationCurve)?.Curve?.Length) ?? 0; } catch { lenFt = 0; }
                rows.Add(new CutListRow
                {
                    ElementId = id.Value,
                    System    = ReadString(p, "PLM_SYS_TXT"),
                    SizeMm    = diaFt * 304.8,
                    LengthMm  = lenFt * 304.8,
                    Material  = ReadString(p, "PLM_PPE_MAT_TXT"),
                });
            }
            return rows;
        }

        public static string RunCutList(UIDocument uidoc, IEnumerable<CutListRow> rows)
        {
            var doc = uidoc.Document;
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "STING_v4_pipe_cut_list.csv");
            int n = 0;
            using (var w = new StreamWriter(path, false))
            {
                w.WriteLine("element_id,system,size_mm,length_mm,material,mitre_angle_deg");
                foreach (var r in rows.Where(r => r.Include))
                {
                    w.WriteLine($"{r.ElementId},{Csv(r.System)},{r.SizeMm:F0},{r.LengthMm:F0},{Csv(r.Material)},{r.MitreAngleDeg:F1}");
                    n++;
                }
            }
            return $"Exported {n} pipes to:\n{path}";
        }

        // ── Weld map ──────────────────────────────────────────

        public static List<WeldMapRow> BuildWeldMapRows(Document doc, IList<ElementId> ids)
        {
            var rows = new List<WeldMapRow>();
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                if (el.Category == null) continue;
                int bic = (int)el.Category.Id.Value;
                if (bic != (int)BuiltInCategory.OST_PipeCurves
                 && bic != (int)BuiltInCategory.OST_PipeFitting) continue;

                string nm = (el.Name ?? "").Replace(',', ';');
                string type = nm.ToUpperInvariant().Contains("FIELD") ? "FIELD"
                            : nm.ToUpperInvariant().Contains("SHOP")  ? "SHOP"
                                                                      : "FIELD-FIT";
                rows.Add(new WeldMapRow
                {
                    ElementId = id.Value,
                    Category  = el.Category?.Name ?? "",
                    Name      = nm,
                    WeldType  = type,
                });
            }
            return rows;
        }

        public static string RunWeldMap(UIDocument uidoc, IEnumerable<WeldMapRow> rows)
        {
            var doc = uidoc.Document;
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "STING_v4_pipe_welds.csv");
            int n = 0;
            using (var w = new StreamWriter(path, false))
            {
                try { Core.Branding.BrandTokens.StampCsvHeader(w, doc, "pipe_weld_map"); } catch { }
                w.WriteLine("element_id,category,name,weld_type,size_mm,schedule");
                foreach (var r in rows.Where(r => r.Include))
                {
                    w.WriteLine($"{r.ElementId},{Csv(r.Category)},{Csv(r.Name)},{Csv(r.WeldType)},{Csv(r.SizeMm)},{Csv(r.Schedule)}");
                    n++;
                }
            }
            return $"Weld map regenerated for {n} elements.\nSaved to:\n{path}";
        }

        // ── Package preview (read-only grouping) ──────────────

        public static List<PackageGroupRow> BuildPackageRows(Document doc, IList<ElementId> ids)
        {
            var rows = new List<PackageGroupRow>();
            var groups = ids
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .GroupBy(e => new
                {
                    Discipline = DisciplineFor(e),
                    System     = ReadString(e, "PLM_SYS_TXT")
                                   + ReadString(e, "MEC_SYS_TXT")
                                   + ReadString(e, "ELC_SYS_TXT"),
                    Level      = ReadString(e, "ASS_LVL_COD_TXT"),
                });
            int seq = 1;
            foreach (var g in groups.OrderBy(g => g.Key.Discipline).ThenBy(g => g.Key.System).ThenBy(g => g.Key.Level))
            {
                string disc = g.Key.Discipline;
                string sys  = string.IsNullOrWhiteSpace(g.Key.System) ? "GEN" : g.Key.System;
                string lvl  = string.IsNullOrWhiteSpace(g.Key.Level) ? "XX" : g.Key.Level;
                rows.Add(new PackageGroupRow
                {
                    Discipline          = disc,
                    System              = sys,
                    Level               = lvl,
                    ElementCount        = g.Count(),
                    AssemblyNamePreview = $"SP-{disc}-{sys}-{lvl}-{seq:0000}",
                });
                seq++;
            }
            return rows;
        }

        public static string RunGeneratePackage(UIDocument uidoc, IList<ElementId> ids)
        {
            var doc = uidoc.Document;
            var res = FabricationEngine.GenerateFabricationPackage(doc, ids);
            // Record for Undo last run (#6)
            try { FabricationUndoManager.Record(doc, res); } catch (Exception ex) { StingLog.Warn($"UndoRecord: {ex.Message}"); }
            // Open first generated sheet for immediate feedback
            if (res.SheetIds.Count > 0)
            {
                try
                {
                    if (doc.GetElement(res.SheetIds[0]) is ViewSheet sheet)
                        uidoc.ActiveView = sheet;
                }
                catch (Exception ex) { StingLog.Warn($"GenerateFabPackage open sheet failed: {ex.Message}"); }
            }
            return $"{res.AssemblyIds.Count} assemblies, {res.SheetIds.Count} sheets, {res.FailedCount} failed.";
        }

        /// <summary>
        /// Incremental variant (#11) — hashes each (discipline,system,
        /// level) group and skips groups whose content is unchanged
        /// since the last run. Returns a descriptive summary.
        /// </summary>
        public static string RunGeneratePackageIncremental(UIDocument uidoc, IList<ElementId> ids)
        {
            var doc = uidoc.Document;
            var groups = FabricationGrouper.Pack(doc, ids)
                .Select(g => (g.Key, g.ElementIds))
                .ToList();
            var state = FabricationIncrementalTracker.Load(doc);
            var changed = FabricationIncrementalTracker.FilterChanged(doc, groups, state);
            int skipped = groups.Count - changed.Count;
            if (changed.Count == 0)
                return $"Incremental run: nothing changed ({groups.Count} groups up-to-date).";
            var keepIds = changed.SelectMany(g => g.Ids).Distinct().ToList();
            var res = FabricationEngine.GenerateFabricationPackage(doc, keepIds);
            try { FabricationUndoManager.Record(doc, res); } catch { }
            FabricationIncrementalTracker.RecordHashes(doc, changed);
            return $"Incremental: rebuilt {changed.Count} group(s), skipped {skipped} unchanged; " +
                   $"{res.AssemblyIds.Count} assemblies / {res.SheetIds.Count} sheets.";
        }

        /// <summary>Consolidated BOM (#10) emitted alongside CSV sidecars.</summary>
        public static string RunBomRollup(UIDocument uidoc, IList<ElementId> ids)
        {
            var doc = uidoc.Document;
            var pkg  = BuildPackageRows(doc, ids);
            var cut  = BuildCutListRows(doc, ids);
            var weld = BuildWeldMapRows(doc, ids);
            string path = FabricationXlsxExporter.ExportConsolidatedBom(doc, pkg, cut, weld);
            return string.IsNullOrEmpty(path) ? "BOM roll-up failed (see log)." : $"BOM roll-up saved to:\n{path}";
        }

        // ── PCF / MAJ preview + run (#14) ─────────────────────

        public static List<PcfSystemRow> BuildPcfRows(Document doc, IList<ElementId> ids)
        {
            return ids
                .Select(id => doc.GetElement(id))
                .Where(e => e != null && e.Category != null)
                .Where(e =>
                {
                    int bic = (int)e.Category.Id.Value;
                    return bic == (int)BuiltInCategory.OST_PipeCurves
                        || bic == (int)BuiltInCategory.OST_PipeFitting
                        || bic == (int)BuiltInCategory.OST_PipeAccessory;
                })
                .GroupBy(e =>
                {
                    try { return (e as Autodesk.Revit.DB.Plumbing.Pipe)?.MEPSystem?.Name ?? "UNKNOWN"; }
                    catch { return "UNKNOWN"; }
                })
                .Select(g => new PcfSystemRow
                {
                    System         = g.Key,
                    PipeCount      = g.Count(e => (int)e.Category.Id.Value == (int)BuiltInCategory.OST_PipeCurves),
                    FittingCount   = g.Count(e => (int)e.Category.Id.Value == (int)BuiltInCategory.OST_PipeFitting),
                    AccessoryCount = g.Count(e => (int)e.Category.Id.Value == (int)BuiltInCategory.OST_PipeAccessory),
                })
                .OrderBy(r => r.System, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<MajFabRow> BuildMajRows(Document doc, IList<ElementId> ids)
        {
            var rows = new List<MajFabRow>();
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                rows.Add(new MajFabRow
                {
                    ElementId   = id.Value,
                    Category    = el.Category?.Name ?? "",
                    ServiceName = ReadString(el, "PLM_SYS_TXT") + ReadString(el, "MEC_SYS_TXT"),
                    PartName    = el.Name ?? "",
                });
            }
            return rows;
        }

        // ── Isometrics (existing SP-... sheets) ───────────────

        public static List<IsoSheetRow> BuildIsoRows(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.SheetNumber?.StartsWith("SP-", StringComparison.OrdinalIgnoreCase) == true)
                .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .Select(s => new IsoSheetRow
                {
                    SheetId     = s.Id.Value,
                    SheetNumber = s.SheetNumber ?? "",
                    Name        = s.Name ?? "",
                })
                .ToList();
        }

        public static string RunIsometrics(UIDocument uidoc, IEnumerable<IsoSheetRow> rows)
        {
            var doc = uidoc.Document;
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "STING_v4_isometric_sheet_index.csv");
            int n = 0;
            using (var w = new StreamWriter(path, false))
            {
                w.WriteLine("sheet_id,sheet_number,name");
                foreach (var r in rows.Where(r => r.Include))
                {
                    w.WriteLine($"{r.SheetId},{Csv(r.SheetNumber)},{Csv(r.Name)}");
                    n++;
                }
            }
            return $"Indexed {n} shop drawing sheets to:\n{path}";
        }

        // ── Back-compat entry points (used by older IExternalCommands) ─

        public static void GeneratePackage(UIDocument uidoc, IList<ElementId> ids)
        {
            string summary = RunGeneratePackage(uidoc, ids);
            TaskDialog.Show("STING v4 — Generate Fabrication Package", summary);
        }

        public static void ExportCutList(UIDocument uidoc, IList<ElementId> ids)
        {
            var rows = BuildCutListRows(uidoc.Document, ids);
            if (rows.Count == 0)
            {
                TaskDialog.Show("STING v4 — Export Cut List", "No pipes in scope.");
                return;
            }
            TaskDialog.Show("STING v4 — Export Cut List", RunCutList(uidoc, rows));
        }

        public static void ExportWeldMap(UIDocument uidoc, IList<ElementId> ids)
        {
            var rows = BuildWeldMapRows(uidoc.Document, ids);
            if (rows.Count == 0)
            {
                TaskDialog.Show("STING v4 — Export Weld Map", "No pipes / fittings in scope.");
                return;
            }
            TaskDialog.Show("STING v4 — Export Weld Map", RunWeldMap(uidoc, rows));
        }

        public static void ExportIsometrics(UIDocument uidoc)
        {
            var rows = BuildIsoRows(uidoc.Document);
            if (rows.Count == 0)
            {
                TaskDialog.Show("STING v4 — Export Isometrics",
                    "No SP-... shop drawing sheets found. Run Generate Fabrication Package first.");
                return;
            }
            TaskDialog.Show("STING v4 — Export Isometrics", RunIsometrics(uidoc, rows));
        }

        // ── Helpers ───────────────────────────────────────────

        private static string ReadString(Element el, string param)
        { try { return el?.LookupParameter(param)?.AsString() ?? ""; } catch { return ""; } }

        private static string Csv(string s) => (s ?? "").Replace(',', ';').Replace('\n', ' ').Replace('\r', ' ');

        private static string DisciplineFor(Element el)
        {
            if (el?.Category == null) return "GEN";
            int bic = (int)el.Category.Id.Value;
            return bic switch
            {
                (int)BuiltInCategory.OST_PipeCurves or
                (int)BuiltInCategory.OST_FlexPipeCurves or
                (int)BuiltInCategory.OST_PipeFitting or
                (int)BuiltInCategory.OST_PipeAccessory => "P",

                (int)BuiltInCategory.OST_DuctCurves or
                (int)BuiltInCategory.OST_FlexDuctCurves or
                (int)BuiltInCategory.OST_DuctFitting or
                (int)BuiltInCategory.OST_DuctAccessory => "M",

                (int)BuiltInCategory.OST_Conduit or
                (int)BuiltInCategory.OST_ConduitFitting or
                (int)BuiltInCategory.OST_CableTray or
                (int)BuiltInCategory.OST_CableTrayFitting => "E",

                _ => "GEN",
            };
        }
    }
}
