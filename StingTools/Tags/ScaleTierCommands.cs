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

            // Phase 165 — pull per-category multipliers from the Scale tab
            // sliders. Slider values default to 1.0 / 1.0 / 1.2 / 0.8 mirroring
            // the dock-panel UI defaults.
            var multipliers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["DUCTS"]     = ReadDouble("MultDucts",     1.0),
                ["PIPES"]     = ReadDouble("MultPipes",     1.0),
                ["EQUIPMENT"] = ReadDouble("MultEquipment", 1.2),
                ["FIXTURES"]  = ReadDouble("MultFixtures",  0.8),
            };

            string written = ScaleTiers.SaveProjectOverride(doc, tiers, cap, multipliers);
            if (string.IsNullOrEmpty(written))
            {
                TaskDialog.Show("Apply Scale Tiers",
                    "Cannot persist — document is unsaved. Save the project and retry.");
                return Result.Cancelled;
            }

            StingLog.Info($"ScaleTiers persisted to {written}: " +
                          $"[{string.Join(", ", offsets.Select(o => o.ToString("F1", CultureInfo.InvariantCulture)))}] mm, " +
                          $"cap={cap:F1} ft, mult=[D:{multipliers["DUCTS"]:F2} P:{multipliers["PIPES"]:F2} " +
                          $"E:{multipliers["EQUIPMENT"]:F2} F:{multipliers["FIXTURES"]:F2}]");

            if (string.IsNullOrEmpty(StingCommandHandler.GetExtraParam("SuppressDialog")))
            {
                TaskDialog.Show("Scale Tiers Applied",
                    $"Offsets (mm): {string.Join(" · ", offsets.Select(o => o.ToString("F1", CultureInfo.InvariantCulture)))}\n" +
                    $"Cap: {cap:F0} ft\n" +
                    $"Multipliers: Ducts {multipliers["DUCTS"]:F1}× · Pipes {multipliers["PIPES"]:F1}× · " +
                    $"Equipment {multipliers["EQUIPMENT"]:F1}× · Fixtures {multipliers["FIXTURES"]:F1}×\n\n" +
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
    /// Makes tags in the active view render at the scale-tier size (per
    /// <see cref="ScaleTiers.ForView"/>), preserving each tag's current style
    /// and colour. Two operating modes, selected via extra-param
    /// <c>ScaleSizeMode</c> ("Instance" / "Type" / "Auto" — default Auto):
    ///
    /// <list type="bullet">
    /// <item><b>Instance</b>: for every <c>IndependentTag</c> in the view,
    /// find a pre-existing family type named <c>{size}{style}_{colour}</c>
    /// and call <c>tag.ChangeTypeId</c>. Side-effect free: another view that
    /// uses the same family keeps its own tag types.</item>
    /// <item><b>Type</b>: flips the <c>TAG_{size}{style}_{colour}_BOOL</c>
    /// visibility matrix on each tag type used in the view. Simple but
    /// project-wide — other views using the same type swap too.</item>
    /// <item><b>Auto</b>: tries Instance per tag; any tag whose target type
    /// is missing falls through to Type-mode for that type.</item>
    /// </list>
    ///
    /// <para>Invokable programmatically via <see cref="ApplyToView"/>, which
    /// is what <c>StingToolsApp.OnViewActivated</c> calls when
    /// <c>TAG_SCALE_TIER_AUTO_BOOL</c> is set on Project Information.</para>
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SetScaleAwareTagSizeCommand : IExternalCommand
    {
        public sealed class Result_
        {
            public int InstanceSwitches { get; set; }
            public int TypeMatrixFlips { get; set; }
            public int ParamsChanged { get; set; }
            public int TypesMissingTarget { get; set; }
            public int Skipped { get; set; }
        }

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

            string modeExtra = StingCommandHandler.GetExtraParam("ScaleSizeMode");
            if (string.IsNullOrEmpty(modeExtra)) modeExtra = "Auto";

            Result_ r = ApplyToView(doc, view, size, modeExtra);
            int total = r.InstanceSwitches + r.TypeMatrixFlips;
            if (total == 0 && r.Skipped == 0 && r.TypesMissingTarget == 0)
            {
                TaskDialog.Show("Scale-Aware Tag Size",
                    "No tag annotations found in the active view.");
                return Result.Cancelled;
            }

            if (string.IsNullOrEmpty(StingCommandHandler.GetExtraParam("SuppressDialog")))
            {
                TaskDialog.Show("Scale-Aware Tag Size",
                    $"View: {view.Name}  (1:{view.Scale}, tier '{tier.Label}')\n" +
                    $"Target text size: {size} mm\n" +
                    $"Mode: {modeExtra}\n\n" +
                    $"Tag instances switched:  {r.InstanceSwitches}\n" +
                    $"Tag types matrix-flipped: {r.TypeMatrixFlips}\n" +
                    $"Params changed:           {r.ParamsChanged}\n" +
                    (r.TypesMissingTarget > 0 ? $"Types missing target:     {r.TypesMissingTarget}\n" : "") +
                    (r.Skipped > 0 ? $"Skipped:                  {r.Skipped}" : ""));
            }
            return Result.Succeeded;
        }

        /// <summary>
        /// Programmatic entry point — runs without UI. Caller is responsible
        /// for the open Revit transaction context; this method starts its own
        /// <see cref="Transaction"/>. Safe to call from event handlers.
        /// </summary>
        public static Result_ ApplyToView(Document doc, View view, string size, string mode)
        {
            var r = new Result_();
            if (doc == null || view == null) return r;

            var tagsInView = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .WhereElementIsNotElementType()
                .Cast<IndependentTag>()
                .ToList();
            if (tagsInView.Count == 0) return r;

            Dictionary<string, ElementId> typeIndex =
                TagStyleRuleEngine.BuildTagTypeIndex(doc);

            bool allowInstance = mode != "Type";
            bool allowType     = mode != "Instance";

            using (Transaction tx = new Transaction(doc, $"STING Scale-Aware Tag Size · 1:{view.Scale}"))
            {
                tx.Start();

                // Instance pass: one ChangeTypeId per tag. We track which
                // source types couldn't be matched so the Type pass only
                // touches those.
                var needTypePass = new HashSet<ElementId>();
                foreach (IndependentTag tag in tagsInView)
                {
                    ElementId srcTypeId = tag.GetTypeId();
                    if (srcTypeId == null || srcTypeId == ElementId.InvalidElementId) { r.Skipped++; continue; }
                    Element typeEl = doc.GetElement(srcTypeId);
                    if (typeEl == null) { r.Skipped++; continue; }

                    (string curSize, string curStyle, string curColour) = DetectActiveStyle(typeEl);
                    string style  = curStyle  ?? "NOM";
                    string colour = curColour ?? "BLACK";

                    if (allowInstance)
                    {
                        string targetTypeName = $"{size}{style}_{colour}";
                        if (typeIndex.TryGetValue(targetTypeName, out ElementId targetId)
                            && targetId != srcTypeId)
                        {
                            try
                            {
                                tag.ChangeTypeId(targetId);
                                r.InstanceSwitches++;
                                continue;
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"ChangeTypeId failed on tag {tag.Id}: {ex.Message}");
                            }
                        }
                        else if (typeIndex.ContainsKey(targetTypeName))
                        {
                            continue;
                        }
                        else
                        {
                            r.TypesMissingTarget++;
                            if (allowType) needTypePass.Add(srcTypeId);
                            else           r.Skipped++;
                        }
                    }
                    else
                    {
                        needTypePass.Add(srcTypeId);
                    }
                }

                foreach (ElementId tid in needTypePass)
                {
                    Element typeEl = doc.GetElement(tid);
                    if (typeEl == null) { r.Skipped++; continue; }
                    (string _, string curStyle, string curColour) = DetectActiveStyle(typeEl);
                    string targetBool = ParamRegistry.TagStyleParamName(
                        size, curStyle ?? "NOM", curColour ?? "BLACK");
                    int flipped = ApplySizeMatrix(typeEl, targetBool);
                    if (flipped > 0) { r.TypeMatrixFlips++; r.ParamsChanged += flipped; }
                }

                tx.Commit();
            }

            StingLog.Info(
                $"ScaleAwareTagSize: view='{view.Name}' scale=1:{view.Scale} size={size}mm " +
                $"mode={mode} instances={r.InstanceSwitches} typeFlips={r.TypeMatrixFlips} " +
                $"paramsChanged={r.ParamsChanged} missingTarget={r.TypesMissingTarget} " +
                $"skipped={r.Skipped}");
            return r;
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
