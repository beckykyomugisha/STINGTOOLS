// PlumbingBOQBuilder — Phase 179f.
//
// Aggregates plumbing pipe / fitting / accessory length-by-DN-by-system
// counts into a flat list of BOQ rows. Cost lookup is delegated to the
// cost rates JSON elsewhere; this engine reports the quantities only.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class BoqRow
    {
        public string Code        { get; set; } = "";
        public string Description { get; set; } = "";
        public string System      { get; set; } = "";
        public int    DnMm        { get; set; }
        public string Material    { get; set; } = "";
        public string Unit        { get; set; } = "m";
        public double Qty         { get; set; }
    }

    public class BoqResult
    {
        public List<BoqRow> Rows { get; } = new List<BoqRow>();
        public int PipesCounted     { get; set; }
        public int FittingsCounted  { get; set; }
        public int AccessoriesCounted { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class PlumbingBOQBuilder
    {
        private const double FtToM  = 0.3048;
        private const double FtToMm = 304.8;

        public static BoqResult Build(Document doc)
        {
            var r = new BoqResult();
            if (doc == null) return r;

            // ── Pipes (group by system + DN + material) ──
            var pipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
            r.PipesCounted = pipes.Count;
            var pipeBuckets = new Dictionary<string, BoqRow>();
            foreach (var p in pipes)
            {
                try
                {
                    var sys = p.MEPSystem?.Name ?? "Unclassified";
                    int dn  = (int)Math.Round(p.Diameter * FtToMm);
                    string mat = p.PipeType?.Name ?? "";
                    var key = $"P|{sys}|{dn}|{mat}";
                    if (!pipeBuckets.TryGetValue(key, out var row))
                    {
                        row = new BoqRow
                        {
                            Code = "P" + dn.ToString("D3"),
                            Description = $"{mat} pipe DN{dn} ({sys})",
                            System = sys,
                            DnMm = dn,
                            Material = mat,
                            Unit = "m"
                        };
                        pipeBuckets[key] = row;
                    }
                    var lc = p.Location as LocationCurve;
                    if (lc?.Curve != null) row.Qty += lc.Curve.Length * FtToM;
                }
                catch (Exception ex) { r.Warnings.Add($"pipe {p.Id}: {ex.Message}"); }
            }
            r.Rows.AddRange(pipeBuckets.Values.OrderBy(x => x.System).ThenBy(x => x.DnMm));

            // ── Pipe fittings (count, group by family) ──
            var fittings = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeFitting)
                .WhereElementIsNotElementType().ToElements().OfType<FamilyInstance>().ToList();
            r.FittingsCounted = fittings.Count;
            var fitBuckets = new Dictionary<string, BoqRow>();
            foreach (var f in fittings)
            {
                try
                {
                    string fam = f.Symbol?.Family?.Name ?? "Unnamed";
                    string typ = f.Symbol?.Name ?? "";
                    var key = $"F|{fam}|{typ}";
                    if (!fitBuckets.TryGetValue(key, out var row))
                    {
                        row = new BoqRow
                        {
                            Code = "F" + (fitBuckets.Count + 1).ToString("D3"),
                            Description = $"{fam} ({typ})",
                            Unit = "nr",
                            System = "Fitting"
                        };
                        fitBuckets[key] = row;
                    }
                    row.Qty += 1;
                }
                catch (Exception ex) { r.Warnings.Add($"fitting {f.Id}: {ex.Message}"); }
            }
            r.Rows.AddRange(fitBuckets.Values.OrderBy(x => x.Description));

            // ── Pipe accessories (valves, traps, sleeves, BFP) ──
            var accs = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeAccessory)
                .WhereElementIsNotElementType().ToElements().OfType<FamilyInstance>().ToList();
            r.AccessoriesCounted = accs.Count;
            var accBuckets = new Dictionary<string, BoqRow>();
            foreach (var a in accs)
            {
                try
                {
                    string fam = a.Symbol?.Family?.Name ?? "Unnamed";
                    string typ = a.Symbol?.Name ?? "";
                    var key = $"A|{fam}|{typ}";
                    if (!accBuckets.TryGetValue(key, out var row))
                    {
                        row = new BoqRow
                        {
                            Code = "A" + (accBuckets.Count + 1).ToString("D3"),
                            Description = $"{fam} ({typ})",
                            Unit = "nr",
                            System = "Accessory"
                        };
                        accBuckets[key] = row;
                    }
                    row.Qty += 1;
                }
                catch (Exception ex) { r.Warnings.Add($"acc {a.Id}: {ex.Message}"); }
            }
            r.Rows.AddRange(accBuckets.Values.OrderBy(x => x.Description));

            return r;
        }
    }
}
