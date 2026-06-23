// StingTools — MEP Level View Producer (Phase F).
//
// Per-level fan-out + auto sheet-placement. Where Phase E duplicates the active
// plan once per discipline, this creates a fresh floor plan for EVERY level that
// hosts MEP of a discipline (M = ducts, P = pipes, E = electrical devices),
// applies the resolved MEP DrawingType + per-domain system colours, and — when
// asked — drops each view onto its own sheet (title block + number/name from the
// DrawingType patterns).
//
// CALLER OWNS THE ACTIVE TRANSACTION.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core.Drawing;

namespace StingTools.Core.Mep
{
    public sealed class MepLevelViewRow
    {
        public string Level { get; set; } = "";
        public string Discipline { get; set; } = "";
        public string ViewName { get; set; } = "";
        public string SheetNumber { get; set; } = "";
        public bool ViewCreated { get; set; }
        public bool SheetCreated { get; set; }
        public string Note { get; set; } = "";
    }

    public sealed class MepLevelViewResult
    {
        public List<MepLevelViewRow> Rows { get; } = new List<MepLevelViewRow>();
        public List<string> Warnings { get; } = new List<string>();
        public int Views  => Rows.Count(r => r.ViewCreated);
        public int Sheets => Rows.Count(r => r.SheetCreated);
    }

    public static class MepLevelViewProducer
    {
        private sealed class Disc
        {
            public string Code, Label, DocType;
            public ViewDiscipline ViewDisc;
            public MepDomain Domain;
            public HashSet<ElementId> Levels; // levels that host this discipline
        }

        public static MepLevelViewResult Produce(Document doc, bool placeOnSheets)
        {
            var result = new MepLevelViewResult();
            if (doc == null) { result.Warnings.Add("No document."); return result; }

            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().FirstOrDefault(t => t.ViewFamily == ViewFamily.FloorPlan);
            if (vft == null) { result.Warnings.Add("No FloorPlan ViewFamilyType in the project."); return result; }

            var disciplines = new List<Disc>
            {
                new Disc { Code="M", Label="Mechanical", DocType="COORD",    ViewDisc=ViewDiscipline.Mechanical, Domain=MepDomain.Duct,
                           Levels = LevelsWith<Duct>(doc) },
                new Disc { Code="P", Label="Plumbing",   DocType="DRAINAGE", ViewDisc=ViewDiscipline.Plumbing,   Domain=MepDomain.Pipe,
                           Levels = LevelsWith<Pipe>(doc) },
                new Disc { Code="E", Label="Electrical", DocType="POWER",    ViewDisc=ViewDiscipline.Electrical, Domain=MepDomain.All,
                           Levels = LevelsWithElectrical(doc) },
            };

            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            var levelById = levels.ToDictionary(l => l.Id, l => l);

            var existingViewNames = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);
            var existingSheetNos = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber),
                StringComparer.OrdinalIgnoreCase);
            var seqByDisc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var lvl in levels)
            {
                string levelCode = LevelCode(lvl);
                foreach (var disc in disciplines)
                {
                    if (!disc.Levels.Contains(lvl.Id)) continue;
                    var row = new MepLevelViewRow { Level = lvl.Name, Discipline = disc.Label };
                    result.Rows.Add(row);

                    // Idempotent: if this level×discipline coordination view already
                    // exists (prior run), skip it — don't proliferate "(2)" views + sheets.
                    string canonicalName = $"{lvl.Name} - {disc.Label} Coordination";
                    if (existingViewNames.Contains(canonicalName))
                    {
                        row.ViewName = canonicalName;
                        row.Note = "exists — skipped (re-run safe)";
                        continue;
                    }

                    try
                    {
                        var v = ViewPlan.Create(doc, vft.Id, lvl.Id);
                        if (v == null) { row.Note = "ViewPlan.Create returned null"; continue; }
                        try { v.Discipline = disc.ViewDisc; } catch { }

                        v.Name = Unique(existingViewNames, canonicalName);
                        existingViewNames.Add(v.Name);
                        row.ViewName = v.Name;
                        row.ViewCreated = true;

                        var dt = ResolveDrawingType(doc, disc.Code, disc.DocType);
                        if (dt != null)
                            try { DrawingTypePresentation.Apply(doc, v, dt, runAnnotation: false); } catch (Exception ex) { result.Warnings.Add($"{row.ViewName}: presentation: {ex.Message}"); }

                        if (disc.Domain != MepDomain.All)
                        {
                            var coord = MepCoordinationEngine.ApplyToView(doc, v, disc.Domain);
                            result.Warnings.AddRange(coord.Warnings.Select(w => $"{row.ViewName}: {w}"));
                        }

                        if (placeOnSheets)
                            PlaceOnSheet(doc, v, dt, disc, levelCode, seqByDisc, existingSheetNos, row, result.Warnings);

                        row.Note = (dt != null ? dt.Id : "no DrawingType") + (row.SheetCreated ? $" · sheet {row.SheetNumber}" : "");
                    }
                    catch (Exception ex)
                    {
                        row.Note = ex.Message;
                        result.Warnings.Add($"{lvl.Name}/{disc.Code}: {ex.Message}");
                    }
                }
            }

            if (result.Rows.Count == 0)
                result.Warnings.Add("No level hosts MEP of any discipline (no ducts / pipes / electrical devices).");
            return result;
        }

        // ── sheet placement ──────────────────────────────────────────────────

        private static void PlaceOnSheet(
            Document doc, View v, DrawingType dt, Disc disc, string levelCode,
            Dictionary<string, int> seqByDisc, HashSet<string> takenNos,
            MepLevelViewRow row, List<string> warnings)
        {
            var tbId = ResolveTitleBlock(doc, dt);
            if (tbId == null || tbId == ElementId.InvalidElementId)
            { warnings.Add($"{row.ViewName}: no title block available — view left unplaced."); return; }

            ViewSheet sheet;
            try { sheet = ViewSheet.Create(doc, tbId); }
            catch (Exception ex) { warnings.Add($"{row.ViewName}: ViewSheet.Create: {ex.Message}"); return; }

            int seq = (seqByDisc.TryGetValue(disc.Code, out var n) ? n : 0) + 1;
            seqByDisc[disc.Code] = seq;

            string numPat = dt?.SheetNumberPattern ?? "{disc}-{seq:D3}";
            string namPat = dt?.SheetNamePattern   ?? "{discipline} {purpose} - {lvl}";
            string number = Unique(takenNos, Substitute(numPat, disc, levelCode, seq));
            takenNos.Add(number);
            try { sheet.SheetNumber = number; } catch (Exception ex) { warnings.Add($"{row.ViewName}: sheet number: {ex.Message}"); }
            try { sheet.Name = Substitute(namPat, disc, levelCode, seq); } catch { }
            row.SheetNumber = sheet.SheetNumber;

            try
            {
                if (Viewport.CanAddViewToSheet(doc, sheet.Id, v.Id))
                {
                    Viewport.Create(doc, sheet.Id, v.Id, SheetCentre(doc, sheet));
                    row.SheetCreated = true;
                }
                else warnings.Add($"{row.ViewName}: view cannot be added to a sheet (already placed?).");
            }
            catch (Exception ex) { warnings.Add($"{row.ViewName}: Viewport.Create: {ex.Message}"); }
        }

        private static XYZ SheetCentre(Document doc, ViewSheet sheet)
        {
            try
            {
                var tb = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElement();
                var bb = tb?.get_BoundingBox(sheet);
                if (bb != null) return new XYZ((bb.Min.X + bb.Max.X) / 2.0, (bb.Min.Y + bb.Max.Y) / 2.0, 0);
            }
            catch { }
            return XYZ.Zero;
        }

        private static ElementId ResolveTitleBlock(Document doc, DrawingType dt)
        {
            var symbols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
            if (symbols.Count == 0) return ElementId.InvalidElementId;

            FamilySymbol pick = null;
            if (!string.IsNullOrWhiteSpace(dt?.TitleBlockFamily))
                pick = symbols.FirstOrDefault(s =>
                    string.Equals(s.FamilyName, dt.TitleBlockFamily, StringComparison.OrdinalIgnoreCase));
            pick ??= symbols[0];
            try { if (!pick.IsActive) pick.Activate(); } catch { }
            return pick.Id;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static string Substitute(string pattern, Disc disc, string levelCode, int seq)
        {
            if (string.IsNullOrEmpty(pattern)) return $"{disc.Code}-{seq:D3}";
            string s = pattern
                .Replace("{disc}", disc.Code)
                .Replace("{discipline}", disc.Label)
                .Replace("{lvl}", levelCode)
                .Replace("{purpose}", "Coordination")
                .Replace("{seq:D2}", seq.ToString("D2"))
                .Replace("{seq:D3}", seq.ToString("D3"))
                .Replace("{seq:D4}", seq.ToString("D4"))
                .Replace("{seq}", seq.ToString("D4"));
            return s;
        }

        private static DrawingType ResolveDrawingType(Document doc, string disc, string docType)
        {
            try
            {
                return DrawingDispatcher.Resolve(doc, disc, "*", docType)
                    ?? DrawingDispatcher.Resolve(doc, disc, "*", "MEP")
                    ?? DrawingDispatcher.CandidatesForDiscipline(doc, disc)
                        .FirstOrDefault(d => d.Purpose == DrawingPurpose.Coordination || d.Purpose == DrawingPurpose.Plan);
            }
            catch { return null; }
        }

        private static HashSet<ElementId> LevelsWith<T>(Document doc) where T : Element
        {
            var set = new HashSet<ElementId>();
            foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(T)).WhereElementIsNotElementType())
            {
                var lid = LevelOf(el);
                if (lid != ElementId.InvalidElementId) set.Add(lid);
            }
            return set;
        }

        private static HashSet<ElementId> LevelsWithElectrical(Document doc)
        {
            var set = new HashSet<ElementId>();
            var cats = new ElementMulticategoryFilter(new[]
            {
                BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices,
                BuiltInCategory.OST_DataDevices, BuiltInCategory.OST_FireAlarmDevices
            });
            foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(cats))
            {
                var lid = LevelOf(el);
                if (lid != ElementId.InvalidElementId) set.Add(lid);
            }
            return set;
        }

        private static ElementId LevelOf(Element el)
        {
            try
            {
                if (el.LevelId != null && el.LevelId != ElementId.InvalidElementId) return el.LevelId;
                var p = el.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId) return p.AsElementId();
                var p2 = el.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                if (p2 != null && p2.StorageType == StorageType.ElementId) return p2.AsElementId();
            }
            catch { }
            return ElementId.InvalidElementId;
        }

        private static string LevelCode(Level lvl)
        {
            string n = (lvl?.Name ?? "").Trim();
            if (string.IsNullOrEmpty(n)) return "XX";
            // Compact: "Level 1" → "L1", "Ground Floor" → "GF", else first token.
            var up = n.ToUpperInvariant();
            if (up.Contains("GROUND")) return "GF";
            if (up.Contains("ROOF")) return "RF";
            if (up.Contains("BASEMENT"))
            {
                var bd = new string(n.Where(char.IsDigit).ToArray());
                return string.IsNullOrEmpty(bd) ? "B1" : "B" + bd; // Sub-Basement 2 -> B2, not B1
            }
            var digits = new string(n.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digits)) return "L" + digits;
            return n.Length <= 4 ? n.Replace(" ", "") : n.Substring(0, 4);
        }

        private static string Unique(HashSet<string> taken, string baseName)
        {
            if (!taken.Contains(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                string c = $"{baseName} ({i})";
                if (!taken.Contains(c)) return c;
            }
            return baseName + " " + Guid.NewGuid().ToString("N").Substring(0, 4);
        }
    }
}
