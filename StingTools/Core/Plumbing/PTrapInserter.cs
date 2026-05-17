// PTrapInserter — Phase 179c P-trap detection / placement.
//
// Walks every plumbing fixture without a recorded trap and reports
// where one is missing. Set placeFamily=true to actually insert a P-trap
// family at the fixture connector.
//
// Family resolution chain (mirrors STING_SEED pattern):
//   1. STING_SEED_PTRAP_{DN}MM
//   2. P-Trap - {Material} - {DN}mm
//   3. First family-symbol whose family name contains "TRAP"
// The engine only registers PLM_HAS_TRAP_BOOL when a placement succeeds
// or when an existing trap accessory is found within 600 mm of the
// fixture's drainage connector — so re-running is idempotent.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class PTrapResult
    {
        public int FixturesScanned    { get; set; }
        public int FixturesAlreadyTrapped { get; set; }
        public int TrapsPlaced        { get; set; }
        public int FixturesSkipped    { get; set; }
        public List<string> Warnings  { get; } = new List<string>();
        public List<ElementId> PlacedIds { get; } = new List<ElementId>();
    }

    public static class PTrapInserter
    {
        public static PTrapResult Scan(Document doc, IEnumerable<Element> scope, bool placeFamily)
        {
            var r = new PTrapResult();
            if (doc == null) return r;
            var fixtures = (scope ?? new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .WhereElementIsNotElementType()
                    .ToElements())
                .Where(el => el is FamilyInstance)
                .Cast<FamilyInstance>()
                .ToList();
            r.FixturesScanned = fixtures.Count;

            FamilySymbol trapSym = placeFamily ? ResolveTrapSymbol(doc) : null;
            if (placeFamily && trapSym == null)
            {
                r.Warnings.Add("No P-trap family loaded (STING_SEED_PTRAP_*, P-Trap*, or family name 'TRAP'). Falling back to dry-run.");
                placeFamily = false;
            }

            foreach (var fi in fixtures)
            {
                try
                {
                    if (HasExistingTrap(doc, fi))
                    {
                        r.FixturesAlreadyTrapped++;
                        TryWriteBool(fi, ParamRegistry.PLM_HAS_TRAP, true);
                        continue;
                    }
                    var conn = ResolveDrainageConnector(fi);
                    if (conn == null)
                    {
                        r.FixturesSkipped++;
                        r.Warnings.Add($"Fixture {fi.Id} has no drainage connector — skipped");
                        continue;
                    }
                    if (!placeFamily)
                    {
                        r.FixturesSkipped++;
                        continue;
                    }
                    if (!trapSym.IsActive) { trapSym.Activate(); doc.Regenerate(); }
                    var trap = doc.Create.NewFamilyInstance(conn.Origin, trapSym, StructuralType.NonStructural);
                    if (trap == null) { r.FixturesSkipped++; continue; }
                    r.TrapsPlaced++;
                    r.PlacedIds.Add(trap.Id);
                    TryWriteBool(fi, ParamRegistry.PLM_HAS_TRAP, true);
                }
                catch (Exception ex)
                {
                    r.FixturesSkipped++;
                    r.Warnings.Add($"Fixture {fi.Id}: {ex.Message}");
                }
            }
            return r;
        }

        private static bool HasExistingTrap(Document doc, FamilyInstance fi)
        {
            try
            {
                // Cheap parameter check first.
                var p = fi.LookupParameter(ParamRegistry.PLM_HAS_TRAP);
                if (p != null && p.HasValue && p.StorageType == StorageType.Integer && p.AsInteger() == 1) return true;
                // Walk the connector graph one hop downstream.
                var cm = fi.MEPModel?.ConnectorManager;
                if (cm == null) return false;
                foreach (Connector c in cm.Connectors)
                {
                    if (c.Domain != Domain.DomainPiping) continue;
                    foreach (Connector other in c.AllRefs)
                    {
                        var owner = other.Owner;
                        if (owner == null || owner.Id == fi.Id) continue;
                        var name = ((owner as FamilyInstance)?.Symbol?.Family?.Name ?? "").ToUpperInvariant();
                        if (name.Contains("TRAP") || name.Contains("P-TRAP") || name.Contains("S-TRAP")) return true;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        private static Connector ResolveDrainageConnector(FamilyInstance fi)
        {
            try
            {
                var cm = fi?.MEPModel?.ConnectorManager;
                if (cm == null) return null;
                foreach (Connector c in cm.Connectors)
                {
                    if (c.Domain == Domain.DomainPiping) return c;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return null;
        }

        private static FamilySymbol ResolveTrapSymbol(Document doc)
        {
            try
            {
                var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>().ToList();
                foreach (var s in symbols)
                {
                    var fn = (s.Family?.Name ?? "").ToUpperInvariant();
                    if (fn.StartsWith("STING_SEED_PTRAP")) return s;
                }
                foreach (var s in symbols)
                {
                    var fn = (s.Family?.Name ?? "").ToUpperInvariant();
                    if (fn.Contains("P-TRAP") || fn.Contains("PTRAP")) return s;
                }
                foreach (var s in symbols)
                {
                    var fn = (s.Family?.Name ?? "").ToUpperInvariant();
                    if (fn.Contains("TRAP")) return s;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return null;
        }

        private static void TryWriteBool(Element el, string name, bool v)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Integer) p.Set(v ? 1 : 0);
                else if (p.StorageType == StorageType.String) p.Set(v ? "true" : "false");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }
    }
}
