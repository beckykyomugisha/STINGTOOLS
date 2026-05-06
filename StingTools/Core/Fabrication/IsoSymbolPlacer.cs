// StingTools v4 MVP — IsoSymbolPlacer.
//
// Walks an assembly's elements, looks up a matching detail symbol
// from STING_ISO_SYMBOLS_INDEX.csv via name/category heuristics,
// resolves the corresponding FamilySymbol (lazy-loads the .rfa from
// the families folder when not yet in the project), and calls
// DetailComponent.Create to drop the symbol on a host detail view.
//
// First-pass heuristic mapping uses three rules:
//   1. Category match (Pipe / Duct / Conduit / etc.)
//   2. Name keyword match against the symbol_code uppercase
//   3. Element type metadata (e.g. valve type, fitting angle)
// Symbols whose family file cannot be located are silently skipped
// with a single StingLog.Warn per missing family.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Fabrication
{
    public class SymbolEntry
    {
        public string SymbolCode { get; set; } = "";
        public string FamilyFile { get; set; } = "";
        public string Category   { get; set; } = "";
        public string Description{ get; set; } = "";
    }

    public static class IsoSymbolPlacer
    {
        private static List<SymbolEntry> _index;
        private static readonly HashSet<string> _missingFamiliesLogged = new HashSet<string>();

        public static int PlaceSymbolsForAssembly(
            Document doc,
            ElementId assemblyId,
            View detailView,
            FabricationResult result)
        {
            int placed = 0;
            if (doc == null || assemblyId == null || detailView == null) return placed;
            EnsureIndexLoaded();
            if (_index == null || _index.Count == 0) return placed;

            var ai = doc.GetElement(assemblyId) as AssemblyInstance;
            if (ai == null) return placed;

            using (var tx = new Transaction(doc, "STING v4 ISO 6412 symbols"))
            {
                try { tx.Start(); }
                catch (Exception ex) { result.Warnings.Add($"IsoSymbolPlacer tx: {ex.Message}"); return placed; }

                try
                {
                    foreach (ElementId memberId in ai.GetMemberIds())
                    {
                        var member = doc.GetElement(memberId);
                        if (member == null) continue;
                        SymbolEntry entry = ResolveSymbol(member);
                        if (entry == null) continue;
                        FamilySymbol fs = ResolveFamilySymbol(doc, entry, result);
                        if (fs == null) continue;
                        if (TryPlace(doc, detailView, member, fs, result))
                            placed++;
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.Warnings.Add($"IsoSymbolPlacer fatal: {ex.Message}");
                }
            }
            return placed;
        }

        private static bool TryPlace(Document doc, View view, Element member, FamilySymbol fs, FabricationResult result)
        {
            try
            {
                if (!fs.IsActive) { fs.Activate(); doc.Regenerate(); }
                XYZ point = (member.Location as LocationPoint)?.Point
                         ?? (member.Location as LocationCurve)?.Curve?.GetEndPoint(0)
                         ?? XYZ.Zero;
                var inst = doc.Create.NewFamilyInstance(point, fs, view);

                // Phase 4 #15 — scale-aware sizing. Detail symbols are
                // drawn at paper-space size; if the family exposes a
                // "Symbol Scale" instance parameter we set it to match
                // the host view's current scale so a 1:50 spool and a
                // 1:25 detail both show symbols at the same plotted mm.
                try
                {
                    int vs = (view?.Scale ?? 50);
                    var p = inst?.LookupParameter("Symbol Scale");
                    if (p != null && !p.IsReadOnly)
                    {
                        if (p.StorageType == StorageType.Double)      p.Set((double)vs);
                        else if (p.StorageType == StorageType.Integer) p.Set(vs);
                    }
                    var pAss = inst?.LookupParameter("STING_ISO_SYMBOL_SCALE_IN");
                    if (pAss != null && !pAss.IsReadOnly && pAss.StorageType == StorageType.Double)
                        pAss.Set((double)vs);
                }
                catch (Exception sx) { StingLog.Warn($"IsoSymbolPlacer scale: {sx.Message}"); }
                return true;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"IsoSymbolPlacer.TryPlace {member.Id} -> {fs.FamilyName}: {ex.Message}");
                return false;
            }
        }

        private static FamilySymbol ResolveFamilySymbol(Document doc, SymbolEntry entry, FabricationResult result)
        {
            string famName = Path.GetFileNameWithoutExtension(entry.FamilyFile);
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)))
                {
                    if (el is FamilySymbol fs &&
                        string.Equals(fs.FamilyName, famName, StringComparison.OrdinalIgnoreCase))
                        return fs;
                }
                // Try lazy load from families folder
                string familyPath = Path.Combine(StingToolsApp.DataPath ?? "", "..", "Families", "ISO6412", entry.FamilyFile);
                if (File.Exists(familyPath))
                {
                    Family f;
                    if (doc.LoadFamily(familyPath, out f) && f != null)
                    {
                        foreach (ElementId sid in f.GetFamilySymbolIds())
                            if (doc.GetElement(sid) is FamilySymbol fs) return fs;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"IsoSymbolPlacer.ResolveFamilySymbol {famName}: {ex.Message}");
            }

            if (_missingFamiliesLogged.Add(famName))
                StingLog.Warn($"IsoSymbolPlacer: family not found -> {famName}");
            try { result?.MissingFamilies?.Add(famName); } catch { }
            return null;
        }

        private static SymbolEntry ResolveSymbol(Element member)
        {
            string nameUp = (member.Name ?? "").ToUpperInvariant();
            string catNm  = member.Category?.Name ?? "";
            // Quick keyword-based match
            foreach (var entry in _index)
            {
                if (string.IsNullOrEmpty(entry.SymbolCode)) continue;
                if (nameUp.Contains(entry.SymbolCode)) return entry;
            }
            // Category fallback
            foreach (var entry in _index)
            {
                if (string.Equals(entry.Category, catNm, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        private static void EnsureIndexLoaded()
        {
            if (_index != null) return;
            _index = new List<SymbolEntry>();
            string path = StingToolsApp.FindDataFile("STING_ISO_SYMBOLS_INDEX.csv");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                StingLog.Warn("IsoSymbolPlacer: STING_ISO_SYMBOLS_INDEX.csv not found");
                return;
            }
            try
            {
                bool first = true;
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    if (first) { first = false; continue; }
                    var cols = StingToolsApp.ParseCsvLine(line);
                    if (cols == null || cols.Length < 4) continue;
                    _index.Add(new SymbolEntry
                    {
                        SymbolCode  = cols[0],
                        FamilyFile  = cols[1],
                        Category    = cols[2],
                        Description = cols[3]
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"IsoSymbolPlacer: index load failed: {ex.Message}");
            }
        }
    }
}
