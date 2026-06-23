// StingTools — MEP View Producer (Phase E).
//
// Per-discipline view production: the final link of the create-systems →
// coordinated-drawing loop. For each MEP discipline present in the model
// (M = ducts, P = pipes, E = electrical circuits) it duplicates a source plan,
// sets the view discipline, applies the resolved MEP DrawingType (template +
// style pack via DrawingDispatcher), and overlays the system-classification
// colours for that domain (M/P; E inherits its colours from the elec pack).
//
// CALLER OWNS THE ACTIVE TRANSACTION.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core.Drawing;

namespace StingTools.Core.Mep
{
    public sealed class MepViewRow
    {
        public string Discipline { get; set; } = "";
        public string ViewName { get; set; } = "";
        public string DrawingType { get; set; } = "";
        public int FiltersApplied { get; set; }
        public bool Created { get; set; }
        public string Note { get; set; } = "";
    }

    public sealed class MepViewResult
    {
        public List<MepViewRow> Rows { get; } = new List<MepViewRow>();
        public List<string> Warnings { get; } = new List<string>();
        public int Created => Rows.Count(r => r.Created);
    }

    public static class MepViewProducer
    {
        private sealed class Disc
        {
            public string Code;                 // M / E / P
            public string Label;                // Mechanical / Electrical / Plumbing
            public string DocType;              // COORD / POWER / DRAINAGE
            public ViewDiscipline ViewDisc;
            public MepDomain Domain;            // Duct / Pipe / All(E)
            public Func<Document, bool> Present;
        }

        private static readonly List<Disc> Disciplines = new List<Disc>
        {
            new Disc { Code="M", Label="Mechanical", DocType="COORD",    ViewDisc=ViewDiscipline.Mechanical, Domain=MepDomain.Duct,
                       Present = d => Any<Duct>(d) },
            new Disc { Code="P", Label="Plumbing",   DocType="DRAINAGE", ViewDisc=ViewDiscipline.Plumbing,   Domain=MepDomain.Pipe,
                       Present = d => Any<Pipe>(d) },
            new Disc { Code="E", Label="Electrical", DocType="POWER",    ViewDisc=ViewDiscipline.Electrical, Domain=MepDomain.All,
                       Present = d => Any<ElectricalSystem>(d) },
        };

        /// <summary>
        /// Produce one coordinated plan per present MEP discipline by duplicating
        /// <paramref name="source"/> (which must be a duplicatable plan view).
        /// </summary>
        public static MepViewResult Produce(Document doc, View source)
        {
            var result = new MepViewResult();
            if (doc == null) { result.Warnings.Add("No document."); return result; }

            if (source == null || !(source is ViewPlan) || source.IsTemplate ||
                !source.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing))
            {
                result.Warnings.Add("Active view is not a duplicatable plan. Open a floor/ceiling plan and re-run.");
                return result;
            }

            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var disc in Disciplines)
            {
                if (!disc.Present(doc)) continue;
                var row = new MepViewRow { Discipline = disc.Label };
                result.Rows.Add(row);

                try
                {
                    var dupId = source.Duplicate(ViewDuplicateOption.WithDetailing);
                    var v = doc.GetElement(dupId) as View;
                    if (v == null) { row.Note = "duplicate returned null"; continue; }

                    try { v.Discipline = disc.ViewDisc; } catch (Exception ex) { result.Warnings.Add($"{disc.Code}: set discipline: {ex.Message}"); }

                    string name = UniqueName(existingNames, $"{source.Name} - {disc.Label} Coordination");
                    try { v.Name = name; } catch (Exception ex) { result.Warnings.Add($"{disc.Code}: rename: {ex.Message}"); }
                    existingNames.Add(v.Name);
                    row.ViewName = v.Name;
                    row.Created = true;

                    var dt = ResolveDrawingType(doc, disc.Code, disc.DocType);
                    if (dt != null)
                    {
                        row.DrawingType = dt.Id;
                        try { DrawingTypePresentation.Apply(doc, v, dt, runAnnotation: false); }
                        catch (Exception ex) { result.Warnings.Add($"{disc.Code}: presentation: {ex.Message}"); }
                    }

                    // Duct/pipe systems get the classification colours; electrical
                    // inherits its colouring from the elec DrawingType's style pack.
                    if (disc.Domain != MepDomain.All)
                    {
                        var coord = MepCoordinationEngine.ApplyToView(doc, v, disc.Domain);
                        row.FiltersApplied = coord.Applied;
                        result.Warnings.AddRange(coord.Warnings.Select(w => $"{disc.Code}: {w}"));
                    }

                    row.Note = dt != null ? $"created · {dt.Id} · {row.FiltersApplied} filter(s)" : "created · no DrawingType matched";
                }
                catch (Exception ex)
                {
                    row.Note = ex.Message;
                    result.Warnings.Add($"{disc.Code}: {ex.Message}");
                }
            }

            if (result.Rows.Count == 0)
                result.Warnings.Add("No MEP disciplines present (no ducts / pipes / circuits).");
            return result;
        }

        private static DrawingType ResolveDrawingType(Document doc, string disc, string docType)
        {
            try
            {
                return DrawingDispatcher.Resolve(doc, disc, "*", docType)
                    ?? DrawingDispatcher.Resolve(doc, disc, "*", "MEP")
                    ?? DrawingDispatcher.CandidatesForDiscipline(doc, disc)
                        .FirstOrDefault(d => d.Purpose == DrawingPurpose.Coordination
                                          || d.Purpose == DrawingPurpose.Plan);
            }
            catch (Exception ex) { StingLog.Warn($"MEP view producer: DrawingType resolve {disc}/{docType}: {ex.Message}"); return null; }
        }

        private static string UniqueName(HashSet<string> taken, string baseName)
        {
            if (!taken.Contains(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                string n = $"{baseName} ({i})";
                if (!taken.Contains(n)) return n;
            }
            return $"{baseName} ({Guid.NewGuid():N})".Substring(0, Math.Min(baseName.Length + 6, 200));
        }

        private static bool Any<T>(Document doc) where T : Element
        {
            try
            {
                return new FilteredElementCollector(doc).OfClass(typeof(T))
                    .WhereElementIsNotElementType().Any();
            }
            catch { return false; }
        }
    }
}
