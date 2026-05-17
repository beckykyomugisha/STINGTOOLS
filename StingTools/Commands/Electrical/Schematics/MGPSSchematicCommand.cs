// StingTools — D5: Medical Gas Pipeline System (MGPS) Schematic (Phase 179)
//
// Generates a simplified HTM 02-01 / NFPA 99 MGPS schematic in a new ViewDrafting
// showing gas mains, supply sources, zone valves, and terminal units grouped by
// gas type. Elements are identified by MGS_GAS_TYPE_TXT; supply sources by
// MGS_SUPPLY_TYPE_TXT; zone valves by MGS_ZV_ZONE_TXT.
//
// Workflow tag: MGPS_Schematic

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Schematics
{
    /// <summary>
    /// Generates a simplified HTM 02-01 / NFPA 99 MGPS schematic in a new drafting
    /// view. Gas mains are drawn as vertical risers with horizontal zone valve
    /// branches and terminal unit symbols.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MGPSSchematicCommand : IExternalCommand
    {
        public const string Tag = "MGPS_Schematic";

        // Standard medical gas type codes.
        private static readonly string[] KnownGasTypes =
            { "O2", "N2O", "CO2", "AIR", "VAC", "AGSS" };

        // Legend text per gas type.
        private static readonly Dictionary<string, string> GasLegend =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "O2",   "O2   = Oxygen" },
                { "N2O",  "N2O  = Nitrous Oxide" },
                { "CO2",  "CO2  = Carbon Dioxide" },
                { "AIR",  "AIR  = Medical Air" },
                { "VAC",  "VAC  = Vacuum" },
                { "AGSS", "AGSS = Anaesthetic Gas Scavenging" }
            };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Collect all non-type elements.
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList();

            // Group MGPS elements by gas type.
            var mgsByGas = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);

            // Supply source elements keyed by gas type.
            var sourcesByGas = new Dictionary<string, List<(Element el, string supplyType)>>(
                StringComparer.OrdinalIgnoreCase);

            // Zone valve elements keyed by gas type.
            var zvByGas = new Dictionary<string, List<(Element el, string zone)>>(
                StringComparer.OrdinalIgnoreCase);

            // Terminal unit elements keyed by gas type.
            var tuByGas = new Dictionary<string, List<(Element el, string mark)>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var el in allElements)
            {
                try
                {
                    string gasType = el.LookupParameter("MGS_GAS_TYPE_TXT")?.AsString()?.Trim();
                    if (string.IsNullOrEmpty(gasType)) continue;

                    gasType = gasType.ToUpperInvariant();

                    // Register gas type.
                    if (!mgsByGas.ContainsKey(gasType)) mgsByGas[gasType] = new List<Element>();
                    mgsByGas[gasType].Add(el);

                    // Supply source?
                    string supplyType = el.LookupParameter("MGS_SUPPLY_TYPE_TXT")?.AsString()?.Trim();
                    if (!string.IsNullOrEmpty(supplyType))
                    {
                        if (!sourcesByGas.ContainsKey(gasType))
                            sourcesByGas[gasType] = new List<(Element, string)>();
                        sourcesByGas[gasType].Add((el, supplyType));
                    }

                    // Zone valve?
                    string zvZone = el.LookupParameter("MGS_ZV_ZONE_TXT")?.AsString()?.Trim();
                    if (!string.IsNullOrEmpty(zvZone))
                    {
                        if (!zvByGas.ContainsKey(gasType))
                            zvByGas[gasType] = new List<(Element, string)>();
                        zvByGas[gasType].Add((el, zvZone));
                    }

                    // Terminal unit: has gas type but is not a supply source or zone valve.
                    if (string.IsNullOrEmpty(supplyType) && string.IsNullOrEmpty(zvZone))
                    {
                        string mark = el.LookupParameter("MARK")?.AsString()
                            ?? el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()
                            ?? "";
                        if (!tuByGas.ContainsKey(gasType))
                            tuByGas[gasType] = new List<(Element, string)>();
                        tuByGas[gasType].Add((el, mark));
                    }
                }
                catch { /* parameter not applicable — skip */ }
            }

            // Determine the ordered list of gas types present in the project.
            var gasTypesPresent = KnownGasTypes
                .Where(g => mgsByGas.ContainsKey(g))
                .ToList();

            // Include any non-standard gas types found.
            foreach (var g in mgsByGas.Keys)
                if (!gasTypesPresent.Contains(g, StringComparer.OrdinalIgnoreCase))
                    gasTypesPresent.Add(g);

            // If nothing is found, still produce a schematic with a placeholder riser.
            if (gasTypesPresent.Count == 0)
                gasTypesPresent.Add("O2");  // placeholder to show structure

            int totalTU = tuByGas.Values.SelectMany(x => x).Count();

            using (var tx = new Transaction(doc, "STING MGPS Schematic"))
            {
                tx.Start();

                var view = CreateDraftingView(doc, "STING - MGPS Schematic");
                if (view == null)
                {
                    tx.RollBack();
                    TaskDialog.Show("STING MGPS Schematic",
                        "Could not create a drafting view — no Drafting ViewFamilyType found.");
                    return Result.Failed;
                }

                DrawMGPSSchematic(doc, view, gasTypesPresent,
                    sourcesByGas, zvByGas, tuByGas);

                tx.Commit();

                string gasList = string.Join(", ", gasTypesPresent);
                TaskDialog.Show("STING MGPS Schematic",
                    $"MGPS schematic generated.\n\n" +
                    $"View:          {view.Name}\n" +
                    $"Gas types:     {gasList}\n" +
                    $"Terminal units found: {totalTU}");
            }

            return Result.Succeeded;
        }

        // ---------------------------------------------------------------- drawing

        private static void DrawMGPSSchematic(Document doc, ViewDrafting view,
            List<string> gasTypes,
            Dictionary<string, List<(Element el, string supplyType)>> sourcesByGas,
            Dictionary<string, List<(Element el, string zone)>> zvByGas,
            Dictionary<string, List<(Element el, string mark)>> tuByGas)
        {
            double riserSpacingX = Mm(50.0);  // horizontal gap between gas risers
            double riserTopY     = Mm(200.0); // top of the riser
            double riserBottomY  = 0.0;       // bottom (floor level)
            double branchLength  = Mm(30.0);  // horizontal branch from riser
            double zvSymW        = Mm(6.0);   // zone valve symbol width
            double zvSymH        = Mm(6.0);   // zone valve symbol height
            double tuSymW        = Mm(8.0);   // terminal unit symbol width
            double tuSymH        = Mm(5.0);   // terminal unit symbol height

            for (int gi = 0; gi < gasTypes.Count; gi++)
            {
                string gasType = gasTypes[gi];
                double riserX  = gi * riserSpacingX + Mm(20.0);

                // ── Supply source box at top of riser ────────────────────────────
                sourcesByGas.TryGetValue(gasType, out var sources);
                string supplyType = sources?.FirstOrDefault().supplyType ?? "Supply";

                double srcW = Mm(30.0);
                double srcH = Mm(15.0);
                double srcX = riserX - srcW / 2.0;
                double srcY = riserTopY + Mm(5.0);

                DrawBox(doc, view, srcX, srcY, srcW, srcH);
                PlaceLabel(doc, view,
                    srcX + Mm(1), srcY + srcH / 2.0 + Mm(1),
                    $"{gasType}\n{supplyType}");

                // ── Vertical riser line ──────────────────────────────────────────
                DrawLine(doc, view,
                    new XYZ(riserX, srcY, 0),
                    new XYZ(riserX, riserBottomY, 0));

                PlaceLabel(doc, view, riserX + Mm(1), riserTopY - Mm(5), $"{gasType} Main");

                // ── Zone valves: horizontal branches from riser ──────────────────
                zvByGas.TryGetValue(gasType, out var zvList);
                int zvCount = zvList?.Count ?? 0;
                // Evenly space zone valves down the riser.
                double riserLen      = riserTopY - riserBottomY;
                double zvSpacingY    = zvCount > 0
                    ? riserLen / (zvCount + 1)
                    : riserLen / 2.0;

                for (int zi = 0; zi < zvCount; zi++)
                {
                    string zone = zvList![zi].zone;
                    double zvY = riserTopY - zvSpacingY * (zi + 1);

                    // Horizontal branch line.
                    DrawLine(doc, view,
                        new XYZ(riserX,              zvY, 0),
                        new XYZ(riserX + branchLength, zvY, 0));

                    // Zone valve symbol (small box).
                    double zvBoxX = riserX + branchLength;
                    double zvBoxY = zvY - zvSymH / 2.0;
                    DrawBox(doc, view, zvBoxX, zvBoxY, zvSymW, zvSymH);

                    PlaceLabel(doc, view,
                        zvBoxX + zvSymW + Mm(1),
                        zvY - Mm(2),
                        $"{gasType}-ZV-{zone}");
                }

                // ── Terminal units ───────────────────────────────────────────────
                tuByGas.TryGetValue(gasType, out var tuList);
                int tuCount = tuList?.Count ?? 0;

                // Place terminal units below the last zone valve or in the lower
                // half of the riser, offset to the right to avoid zone valve overlap.
                double tuOffsetX = riserX + branchLength + zvSymW + Mm(30.0);
                double tuSpacingY = tuCount > 0
                    ? (riserLen * 0.6) / Math.Max(tuCount, 1)
                    : 0;

                for (int ti = 0; ti < tuCount; ti++)
                {
                    string mark = tuList![ti].mark;
                    double tuY = riserTopY * 0.3 - tuSpacingY * ti;

                    // Short horizontal branch from riser to TU.
                    double tuBranchEndX = riserX + Mm(15.0);
                    DrawLine(doc, view,
                        new XYZ(riserX,        tuY, 0),
                        new XYZ(tuBranchEndX,  tuY, 0));

                    // Terminal unit symbol: rectangle with small flag line.
                    double tuBoxX = tuBranchEndX;
                    double tuBoxY = tuY - tuSymH / 2.0;
                    DrawBox(doc, view, tuBoxX, tuBoxY, tuSymW, tuSymH);

                    // Flag line at right.
                    DrawLine(doc, view,
                        new XYZ(tuBoxX + tuSymW,           tuY,           0),
                        new XYZ(tuBoxX + tuSymW + Mm(4.0), tuY + Mm(4.0), 0));

                    string tuLabel = string.IsNullOrEmpty(mark)
                        ? $"{gasType}-TU-{ti + 1}"
                        : $"{gasType}-{mark}";
                    PlaceLabel(doc, view,
                        tuBoxX + tuSymW + Mm(5.0),
                        tuY - Mm(1),
                        tuLabel);
                }
            }

            // ── Legend box in bottom-right corner ───────────────────────────────
            int legendGasCount = Math.Min(gasTypes.Count, KnownGasTypes.Length);
            double legendX = gasTypes.Count * riserSpacingX + Mm(30.0);
            double legendY = riserBottomY;
            double legendW = Mm(65.0);
            double legendH = Mm(6.0) * (legendGasCount + 1) + Mm(4.0);

            DrawBox(doc, view, legendX, legendY, legendW, legendH);
            PlaceLabel(doc, view, legendX + Mm(2), legendY + legendH - Mm(4), "LEGEND");

            var legendLines = gasTypes
                .Select(g => GasLegend.TryGetValue(g, out var lv) ? lv : $"{g} = {g}")
                .ToList();

            for (int li = 0; li < legendLines.Count; li++)
            {
                PlaceLabel(doc, view,
                    legendX + Mm(2),
                    legendY + legendH - Mm(8) - li * Mm(6),
                    legendLines[li]);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static ViewDrafting CreateDraftingView(Document doc, string name)
        {
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
            if (vft == null) return null;
            var v = ViewDrafting.Create(doc, vft.Id);
            try { v.Name = name; } catch { }
            return v;
        }

        private static double Mm(double mm) => mm / 304.8;

        private static void DrawLine(Document doc, ViewDrafting view, XYZ start, XYZ end)
        {
            if (start.IsAlmostEqualTo(end)) return;
            try { doc.Create.NewDetailCurve(view, Line.CreateBound(start, end)); }
            catch (Exception ex) { StingLog.Warn($"DrawLine: {ex.Message}"); }
        }

        private static void DrawBox(Document doc, ViewDrafting view,
            double x, double y, double w, double h)
        {
            try
            {
                doc.Create.NewDetailCurve(view,
                    Line.CreateBound(new XYZ(x,   y,   0), new XYZ(x+w, y,   0)));
                doc.Create.NewDetailCurve(view,
                    Line.CreateBound(new XYZ(x+w, y,   0), new XYZ(x+w, y+h, 0)));
                doc.Create.NewDetailCurve(view,
                    Line.CreateBound(new XYZ(x+w, y+h, 0), new XYZ(x,   y+h, 0)));
                doc.Create.NewDetailCurve(view,
                    Line.CreateBound(new XYZ(x,   y+h, 0), new XYZ(x,   y,   0)));
            }
            catch (Exception ex) { StingLog.Warn($"DrawBox: {ex.Message}"); }
        }

        private static void PlaceLabel(Document doc, ViewDrafting view,
            double x, double y, string text)
        {
            try
            {
                var tnt = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstElementId();
                if (tnt != ElementId.InvalidElementId)
                    TextNote.Create(doc, view.Id, new XYZ(x, y, 0), text, tnt);
            }
            catch (Exception ex) { StingLog.Warn($"PlaceLabel: {ex.Message}"); }
        }
    }
}
