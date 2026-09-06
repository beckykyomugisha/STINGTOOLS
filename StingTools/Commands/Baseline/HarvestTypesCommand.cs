// ══════════════════════════════════════════════════════════════════════════
//  HarvestTypesCommand.cs — read the types a delivered model actually uses
//  and write them as a catalogue pack for review.
//
//  READ-ONLY on the model. It writes one JSON file and touches nothing else.
//
//  The categories are the ones layer 2 can mint into. Harvesting a category
//  the baseline cannot create would produce a pack that reads well and does
//  nothing.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Baseline;

namespace StingTools.Commands.Baseline
{
    [Transaction(TransactionMode.ReadOnly)]
    public class BaselineHarvestTypesCommand : IExternalCommand
    {
        /// <summary>Family-backed categories layer 2 can mint types into.</summary>
        private static readonly string[] Categories =
        {
            "Doors", "Windows",
            "Structural Columns", "Structural Foundations", "Structural Framing"
        };

        // The dimension list is PER CATEGORY and lives in HarvestDimensionRules.
        // A single flat list asked "Width" of a Concrete-Rectangular-Column and
        // got 4500 x 12000 — the column's extents, not its 450 x 450 section —
        // and wrote them into a pack that B1 would have minted from.

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = BaselineDoc.Resolve(data);
            if (doc == null)
            {
                TaskDialog.Show("STING Harvest Types", "No active document.");
                return Result.Cancelled;
            }

            var report = new TypeHarvestReport();
            var library = TypeHarvestBuilder.Build(Observe(doc),
                doc.ProjectInformation?.Number, report);

            if (report.TypesHarvested == 0)
            {
                TaskDialog.Show("STING Harvest Types", report.Summary());
                return Result.Succeeded;
            }

            string path;
            try
            {
                path = StingPaths.MetaFile(doc, "_BIM_COORD", "harvested_catalogue.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(library, Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Error("BaselineHarvestTypes write", ex);
                TaskDialog.Show("STING Harvest Types",
                    report.Summary() + "\n\nThe pack could not be written: " + ex.Message);
                return Result.Failed;
            }

            StingLog.Info($"BaselineHarvestTypes: {report.TypesHarvested} type(s) -> {path}");
            TaskDialog.Show("STING Harvest Types",
                report.Summary()
                + "\n\nWritten to:\n" + path
                + "\n\nNothing is adopted by writing it. Review the sizes, rename the pack, "
                + "then list its id in adoptCatalogues to use it.");
            return Result.Succeeded;
        }

        /// <summary>
        /// One pass over placed instances. Counting instances rather than
        /// listing types is the whole point: a type nobody placed is not
        /// evidence of practice.
        /// </summary>
        private static List<HarvestedType> Observe(Document doc)
        {
            var byKey = new Dictionary<string, HarvestedType>(StringComparer.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(Categories, StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var inst in new FilteredElementCollector(doc)
                             .OfClass(typeof(FamilyInstance))
                             .WhereElementIsNotElementType()
                             .Cast<FamilyInstance>())
                {
                    string cat = inst?.Category?.Name;
                    if (string.IsNullOrWhiteSpace(cat) || !wanted.Contains(cat)) continue;

                    var sym = inst.Symbol;
                    if (sym == null) continue;

                    string typeName = SafeName(() => sym.Name);
                    string famName = SafeName(() => sym.Family?.Name);
                    if (string.IsNullOrWhiteSpace(typeName)) continue;

                    string key = cat + "|" + typeName;
                    if (!byKey.TryGetValue(key, out var h))
                    {
                        h = new HarvestedType
                        {
                            Category = cat, FamilyName = famName ?? "", TypeName = typeName,
                            ParametersMm = ReadDimensions(sym, cat)
                        };
                        byKey[key] = h;
                    }
                    h.InstanceCount++;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn("BaselineHarvestTypes.Observe: " + ex.Message);
            }
            return byKey.Values.ToList();
        }

        private static Dictionary<string, double> ReadDimensions(FamilySymbol sym, string category)
        {
            var vals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in HarvestDimensionRules.For(category))
            {
                try
                {
                    var p = sym.LookupParameter(name);
                    if (p == null || p.StorageType != StorageType.Double) continue;
                    double mm = p.AsDouble() * 304.8;
                    if (mm > 0) vals[name] = Math.Round(mm, 1);
                }
                catch (Exception ex) { StingLog.Warn("ReadDimensions [" + name + "]: " + ex.Message); }
            }
            return vals;
        }

        private static string SafeName(Func<string> f)
        {
            try { return f(); }
            catch (Exception ex) { StingLog.Warn("SafeName: " + ex.Message); return null; }
        }
    }
}
