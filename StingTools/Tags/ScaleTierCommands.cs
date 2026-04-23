using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Tags
{
    /// <summary>
    /// Persists the five scale-tier offset-mm sliders + the offset-cap slider
    /// from the Tag Studio &gt; Scale tab into project_config.json under the
    /// "SCALE_TIERS" key, then reloads <see cref="ScaleTiers"/> so
    /// <see cref="SmartTagPlacementCommand.GetModelOffset"/> picks the new
    /// values up immediately. Slider values arrive via extra-params keyed
    /// Scale50Mm / Scale100Mm / Scale200Mm / Scale500Mm / Scale1000Mm /
    /// OffsetCapFt (floats). Text-size-per-tier is not user-editable in the
    /// current Scale tab UI, so we preserve whatever the loaded tiers say
    /// (falling back to the bundled SCALE_TIERS.json sizes).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ApplyScaleTiersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            double[] offsets = {
                ReadDouble("Scale50Mm",    2.0),
                ReadDouble("Scale100Mm",   5.0),
                ReadDouble("Scale200Mm",   8.0),
                ReadDouble("Scale500Mm",   12.0),
                ReadDouble("Scale1000Mm",  20.0),
            };
            double cap = ReadDouble("OffsetCapFt", 30.0);

            // Seed text-size-per-tier from the currently-loaded tiers so
            // saving the offsets doesn't wipe them. Length-aligned with
            // the 5 denominator buckets below.
            var currentTiers = ScaleTiers.Current;
            string[] textSizes = new string[5];
            int[] maxDenoms = { 50, 100, 200, 500, int.MaxValue };
            string[] labels  = { "1:1–1:50", "1:50–1:100", "1:100–1:200", "1:200–1:500", "1:500+" };
            for (int i = 0; i < textSizes.Length; i++)
            {
                var match = currentTiers.FirstOrDefault(t => t.MaxDenominator == maxDenoms[i]);
                textSizes[i] = match?.TextSizeMm ?? DefaultTextSize(maxDenoms[i]);
            }

            var tiers = new List<ScaleTiers.Tier>();
            for (int i = 0; i < maxDenoms.Length; i++)
            {
                tiers.Add(new ScaleTiers.Tier
                {
                    MaxDenominator = maxDenoms[i],
                    Label          = labels[i],
                    OffsetMm       = offsets[i],
                    TextSizeMm     = textSizes[i],
                });
            }

            string written = ScaleTiers.SaveProjectOverride(doc, tiers, cap);
            if (string.IsNullOrEmpty(written))
            {
                TaskDialog.Show("Apply Scale Tiers",
                    "Cannot persist — document is unsaved. Save the project and retry.");
                return Result.Cancelled;
            }

            StingLog.Info($"ScaleTiers persisted to {written}: " +
                          $"[{string.Join(", ", offsets.Select(o => o.ToString("F1", CultureInfo.InvariantCulture)))}] mm, " +
                          $"cap={cap:F1} ft");

            if (string.IsNullOrEmpty(StingCommandHandler.GetExtraParam("SuppressDialog")))
            {
                TaskDialog.Show("Scale Tiers Applied",
                    $"Offsets (mm): {string.Join(" · ", offsets.Select(o => o.ToString("F1", CultureInfo.InvariantCulture)))}\n" +
                    $"Cap: {cap:F0} ft\n\n" +
                    "SmartTagPlacementCommand will use the new values on its next run.");
            }
            return Result.Succeeded;
        }

        private static double ReadDouble(string key, double fallback)
        {
            string v = StingCommandHandler.GetExtraParam(key);
            if (string.IsNullOrEmpty(v)) return fallback;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
        }

        private static string DefaultTextSize(int maxDenom)
        {
            if (maxDenom <= 50)  return "3.5";
            if (maxDenom <= 100) return "3";
            if (maxDenom <= 200) return "2.5";
            return "2";
        }
    }

    /// <summary>
    /// Flips the <c>TAG_{size}{style}_{colour}_BOOL</c> visibility matrix on
    /// every tag family type used in the active view so the active size row
    /// matches the view's scale tier (per <see cref="ScaleTiers.ForView"/>).
    /// Style and colour are preserved: whichever BOOL was currently true keeps
    /// its style + colour, only the size letter swaps.
    ///
    /// When no BOOL is currently true on a type (fresh family), we default to
    /// NOM / BLACK at the scale-tier size.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SetScaleAwareTagSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;
            if (view == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }

            ScaleTiers.Tier tier = ScaleTiers.ForView(view);
            string size = tier.TextSizeMm;
            if (!ParamRegistry.TagStyleSizes.Contains(size))
            {
                TaskDialog.Show("Scale-Aware Tag Size",
                    $"Tier '{tier.Label}' maps to text_size_mm='{size}', which is not one of " +
                    $"[{string.Join(", ", ParamRegistry.TagStyleSizes)}]. Fix SCALE_TIERS.json " +
                    "and retry.");
                return Result.Failed;
            }

            var tagTypeIds = CollectTagTypeIdsInView(doc, view);
            if (tagTypeIds.Count == 0)
            {
                TaskDialog.Show("Scale-Aware Tag Size",
                    "No tag annotations found in the active view.");
                return Result.Cancelled;
            }

            int typesUpdated = 0, paramsChanged = 0, typesSkipped = 0;
            using (Transaction tx = new Transaction(doc, $"STING Scale-Aware Tag Size · {view.Scale}"))
            {
                tx.Start();
                foreach (ElementId tid in tagTypeIds)
                {
                    Element typeEl = doc.GetElement(tid);
                    if (typeEl == null) { typesSkipped++; continue; }

                    (string curSize, string curStyle, string curColour) = DetectActiveStyle(typeEl);
                    string newStyle  = curStyle  ?? "NOM";
                    string newColour = curColour ?? "BLACK";
                    string targetBool = ParamRegistry.TagStyleParamName(size, newStyle, newColour);

                    int flipped = ApplySizeMatrix(typeEl, targetBool);
                    if (flipped > 0) { typesUpdated++; paramsChanged += flipped; }
                    else             typesSkipped++;
                }
                tx.Commit();
            }

            StingLog.Info(
                $"ScaleAwareTagSize: view='{view.Name}' scale=1:{view.Scale} tier='{tier.Label}' " +
                $"size={size}mm typesUpdated={typesUpdated} paramsChanged={paramsChanged} " +
                $"typesSkipped={typesSkipped}");

            if (string.IsNullOrEmpty(StingCommandHandler.GetExtraParam("SuppressDialog")))
            {
                TaskDialog.Show("Scale-Aware Tag Size",
                    $"View: {view.Name}  (1:{view.Scale}, tier '{tier.Label}')\n" +
                    $"Target text size: {size} mm\n\n" +
                    $"Tag types updated: {typesUpdated}\n" +
                    $"Params flipped:    {paramsChanged}\n" +
                    (typesSkipped > 0 ? $"Skipped (no matrix / read-only): {typesSkipped}" : ""));
            }
            return Result.Succeeded;
        }

        private static List<ElementId> CollectTagTypeIdsInView(Document doc, View view)
        {
            var set = new HashSet<ElementId>();
            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .WhereElementIsNotElementType()
                .ToElements();
            foreach (Element e in tags)
            {
                ElementId tid = e.GetTypeId();
                if (tid != null && tid != ElementId.InvalidElementId) set.Add(tid);
            }
            return set.ToList();
        }

        private static (string size, string style, string colour) DetectActiveStyle(Element typeEl)
        {
            foreach (string pname in ParamRegistry.AllTagStyleParams)
            {
                Parameter p = typeEl.LookupParameter(pname);
                if (p == null || p.StorageType != StorageType.Integer) continue;
                if (p.AsInteger() == 0) continue;
                return ParseTagStyleParamName(pname);
            }
            return (null, null, null);
        }

        private static (string size, string style, string colour) ParseTagStyleParamName(string pname)
        {
            if (string.IsNullOrEmpty(pname) || !pname.StartsWith("TAG_") || !pname.EndsWith("_BOOL"))
                return (null, null, null);
            string body = pname.Substring(4, pname.Length - 4 - 5);
            int us = body.IndexOf('_');
            if (us <= 0) return (null, null, null);
            string sizeAndStyle = body.Substring(0, us);
            string colour = body.Substring(us + 1);

            foreach (string sz in ParamRegistry.TagStyleSizes.OrderByDescending(s => s.Length))
            {
                if (sizeAndStyle.StartsWith(sz, StringComparison.Ordinal))
                    return (sz, sizeAndStyle.Substring(sz.Length), colour);
            }
            return (null, null, colour);
        }

        private static int ApplySizeMatrix(Element typeEl, string activeBool)
        {
            int changed = 0;
            foreach (string pname in ParamRegistry.AllTagStyleParams)
            {
                Parameter p = typeEl.LookupParameter(pname);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) continue;
                int want = string.Equals(pname, activeBool, StringComparison.Ordinal) ? 1 : 0;
                if (p.AsInteger() == want) continue;
                p.Set(want);
                changed++;
            }
            return changed;
        }
    }
}
