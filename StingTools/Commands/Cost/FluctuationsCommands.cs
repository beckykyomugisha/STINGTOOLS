// ══════════════════════════════════════════════════════════════════════════
//  FluctuationsCommands.cs — PM-3. Compute index-linked fluctuations and feed the
//  AFC + Final Account waterfalls (both read COST_FLUCTUATIONS_UGX).
//
//  Data-driven: the basket (adjustable value, non-adjustable %, weighted indices /
//  CPI) lives at <project>/_BIM_COORD/fluctuations.json so the QS edits it without
//  a heavy modal. First run seeds a template (adjustable value pre-filled from the
//  live works subtotal). Command tag: Fluctuations_Compute.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Cost;
using StingTools.UI;

namespace StingTools.Commands.Cost
{
    internal static class FluctuationsStore
    {
        private static string PathFor(Document doc)
        {
            try
            {
                string parent = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                return Path.Combine(parent, "_BIM_COORD", "fluctuations.json");
            }
            catch { return null; }
        }

        public static FluctuationsBasket Load(Document doc)
        {
            try
            {
                string p = PathFor(doc);
                if (p == null || !File.Exists(p)) return null;
                return JsonConvert.DeserializeObject<FluctuationsBasket>(File.ReadAllText(p));
            }
            catch (Exception ex) { StingLog.Warn($"FluctuationsStore.Load: {ex.Message}"); return null; }
        }

        public static string Save(Document doc, FluctuationsBasket b)
        {
            try
            {
                string p = PathFor(doc);
                if (p == null || b == null) return null;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonConvert.SerializeObject(b, Formatting.Indented));
                return p;
            }
            catch (Exception ex) { StingLog.Warn($"FluctuationsStore.Save: {ex.Message}"); return null; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FluctuationsComputeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var basket = FluctuationsStore.Load(doc);
                if (basket == null)
                {
                    // Seed a template with the adjustable value pre-filled from the
                    // live works subtotal, and a one-line example index.
                    double works = 0;
                    try { works = BOQCostManager.BuildBOQDocument(doc).SubtotalUGX; } catch (Exception ex) { StingLog.Warn($"Fluctuations seed works: {ex.Message}"); }
                    basket = new FluctuationsBasket
                    {
                        Currency = "UGX",
                        AdjustableWorkValue = Math.Round(works, 0),
                        NonAdjustablePercent = 10.0,
                        Method = "formula"
                    };
                    basket.Lines.Add(new FluctuationLine { Label = "Example index (edit me)", Weight = 1, BaseIndex = 100, CurrentIndex = 100 });
                    string seeded = FluctuationsStore.Save(doc, basket);
                    StingResultPanel.Create("Fluctuations — template seeded")
                        .AddSection("EDIT THE BASKET")
                        .Text("No fluctuations basket existed, so a template was written to "
                            + "_BIM_COORD/fluctuations.json (adjustable value pre-filled from the live "
                            + "works subtotal). Edit the indices / weights / non-adjustable % (NEDO/BCIS "
                            + "formula or set method to \"cpi\" for a single UBOS CPI movement), then run "
                            + "Fluctuations again to compute and feed the AFC + Final Account.")
                        .Text(seeded != null ? $"Template: {seeded}" : "")
                        .Show();
                    return Result.Succeeded;
                }

                double amount = FluctuationsEngine.Compute(basket);
                double movement = FluctuationsEngine.BlendedMovement(basket);
                TagConfig.SetConfigValue("COST_FLUCTUATIONS_UGX",
                    amount.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));

                string ccy = basket.Currency ?? "UGX";
                StingResultPanel.Create("Fluctuations computed")
                    .AddSection(basket.Method?.ToUpperInvariant() == "CPI" ? "CPI METHOD" : "FORMULA METHOD")
                    .Metric("Adjustable work value", $"{ccy} {basket.AdjustableWorkValue:N0}")
                    .Metric("Non-adjustable", $"{basket.NonAdjustablePercent:F1}%")
                    .Metric("Blended index movement", $"{movement * 100:F2}%")
                    .Metric("= Fluctuation recoverable", $"{ccy} {amount:N0}")
                    .Text("Written to COST_FLUCTUATIONS_UGX — it now flows into the Anticipated Final "
                        + "Cost and the Final Account waterfalls.")
                    .Show();
                StingLog.Info($"Fluctuations computed: {ccy} {amount:N0} (movement {movement * 100:F2}%).");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Fluctuations_Compute", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }
}
