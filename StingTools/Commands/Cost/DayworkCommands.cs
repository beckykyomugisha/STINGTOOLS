// ══════════════════════════════════════════════════════════════════════════
//  DayworkCommands.cs — instructed-daywork capture, register + pricing. PM-3.
//
//  Closes the gap the tender annexure left open: the DAYWORKS SCHEDULE is a
//  rates framework whose own wording defers quantities to be "priced at final
//  account", and nothing captured the instructed sheets in between.
//
//  Command tags:
//    Daywork_Capture   — record an instructed sheet (headless "DayworkJson"
//                        ExtraParam, else the WPF capture dialog)
//    Daywork_Register  — list / audit captured sheets + CSV export
//    Daywork_Price     — value Recorded/Signed sheets at the build-up percentages
//    Daywork_Attach    — attach a priced sheet to a variation as a Daywork-rated
//                        item (fills the RateSource="Daywork" seam)
//
//  Register: <project>\STING_BIM_MANAGER\dayworks.json (see DayworkEngine).
//
//  All four are ReadOnly transactions — dayworks are commercial sidecar records,
//  nothing writes to the Revit model.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Variation;
using StingTools.UI;

namespace StingTools.Commands.Cost
{
    /// <summary>Shared helpers for the daywork commands.</summary>
    internal static class DayworkCommandHelpers
    {
        /// <summary>Project currency, falling back to UGX when the BOQ can't build.</summary>
        public static string Currency(Document doc)
        {
            try { return BOQCostManager.BuildBOQDocument(doc)?.Currency ?? "UGX"; }
            catch (Exception ex) { StingLog.Warn($"Daywork currency: {ex.Message}"); return "UGX"; }
        }

        /// <summary>The project's tendered daywork percentages. Project config
        /// wins; absent that, the Planscape server defaults (115/110/112) so both
        /// sides of the platform agree on the same numbers.</summary>
        public static DayworkBuildUp ProjectBuildUp()
        {
            return new DayworkBuildUp
            {
                LabourAdditionPct = TagConfig.GetConfigDouble("COST_DAYWORK_LABOUR_PCT", 115.0),
                MaterialsAdditionPct = TagConfig.GetConfigDouble("COST_DAYWORK_MATERIALS_PCT", 110.0),
                PlantAdditionPct = TagConfig.GetConfigDouble("COST_DAYWORK_PLANT_PCT", 112.0)
            };
        }

        public static string Esc(string s) => (s ?? "").Replace("\"", "\"\"");
    }

    // ── Daywork_Capture ──────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DayworkCaptureCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                string ccy = DayworkCommandHelpers.Currency(doc);

                // Inline-form gate, same shape as Variation_BuildStarRate: when the
                // BOQ panel supplied a serialized sheet, use it and skip the modal.
                DayworkRecord rec;
                string json = UI.StingCommandHandler.GetExtraParam("DayworkJson");
                if (!string.IsNullOrEmpty(json))
                {
                    try { rec = JsonConvert.DeserializeObject<DayworkRecord>(json); }
                    catch (Exception jx) { StingLog.Warn($"DayworkJson parse: {jx.Message}"); rec = null; }
                    if (rec == null) { message = "Invalid daywork payload."; return Result.Failed; }
                    if (string.IsNullOrEmpty(rec.Currency)) rec.Currency = ccy;
                    if (rec.BuildUp == null) rec.BuildUp = DayworkCommandHelpers.ProjectBuildUp();
                    if (string.IsNullOrEmpty(rec.RecordedBy)) rec.RecordedBy = Environment.UserName ?? "";
                }
                else
                {
                    var dlg = new DayworkCaptureDialog
                    {
                        CurrencyCode = ccy,
                        Defaults = DayworkCommandHelpers.ProjectBuildUp()
                    };
                    StingWindowHelper.ApplyOwner(dlg);
                    if (dlg.ShowDialog() != true || dlg.Result == null) return Result.Cancelled;
                    rec = dlg.Result;
                }

                if (!rec.HasResources)
                {
                    message = "Daywork sheet carries no priced resource lines.";
                    return Result.Failed;
                }

                DayworkEngine.Upsert(doc, rec);

                var panel = StingResultPanel.Create("Daywork sheet recorded")
                    .SetSubtitle($"{rec.Description} · instruction {(string.IsNullOrWhiteSpace(rec.InstructionRef) ? "(none)" : rec.InstructionRef)}")
                    .AddSection("NET PRIME COST")
                    .Metric("Labour", $"{ccy} {rec.LabourNet:N2}")
                    .Metric("Plant", $"{ccy} {rec.PlantNet:N2}")
                    .Metric("Materials", $"{ccy} {rec.MaterialsNet:N2}")
                    .Metric("Net total", $"{ccy} {rec.NetTotal:N2}")
                    .AddSection("AT BUILD-UP PERCENTAGES")
                    .Metric($"+ Labour ({rec.BuildUp.LabourAdditionPct:0.##}%)", $"{ccy} {rec.LabourAddition:N2}")
                    .Metric($"+ Plant ({rec.BuildUp.PlantAdditionPct:0.##}%)", $"{ccy} {rec.PlantAddition:N2}")
                    .Metric($"+ Materials ({rec.BuildUp.MaterialsAdditionPct:0.##}%)", $"{ccy} {rec.MaterialsAddition:N2}")
                    .MetricHighlight("INDICATIVE SHEET VALUE", $"{ccy} {rec.GrossTotal:N2}")
                    .Text("Status: Recorded. Run Daywork_Price to value it into the final account.");

                var warn = rec.BuildUp?.Warnings() ?? new List<string>();
                if (warn.Count > 0) panel.AddSection("CHECK THE PERCENTAGES").Text(string.Join("\n", warn));
                panel.Show();

                StingLog.Info($"Daywork recorded: {rec.Description} — net {rec.NetTotal:N2}, gross {rec.GrossTotal:N2} {ccy}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Daywork_Capture", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }

    // ── Daywork_Register ─────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DayworkRegisterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var reg = DayworkEngine.Load(doc);
                if (reg.Records.Count == 0)
                {
                    StingResultPanel.Create("Daywork Register")
                        .AddSection("NO DAYWORKS")
                        .Text("No instructed daywork sheets recorded. Use Daywork_Capture to record one.")
                        .Show();
                    return Result.Cancelled;
                }

                string ccy = string.IsNullOrEmpty(reg.Currency) ? DayworkCommandHelpers.Currency(doc) : reg.Currency;

                string csv = OutputLocationHelper.GetTimestampedPath(doc, "STING_DayworkRegister", ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("InstructionRef,Date,Description,Status,LabourNet,PlantNet,MaterialsNet,"
                    + "NetTotal,Additions,GrossTotal,AttachedToVO,SignedBy,PricedBy");
                foreach (var r in reg.Records.OrderBy(x => x.InstructionDate).ThenBy(x => x.InstructionRef))
                    sb.AppendLine(
                        $"\"{DayworkCommandHelpers.Esc(r.InstructionRef)}\",{r.InstructionDate:yyyy-MM-dd}," +
                        $"\"{DayworkCommandHelpers.Esc(r.Description)}\",{r.Status}," +
                        $"{r.LabourNet:F2},{r.PlantNet:F2},{r.MaterialsNet:F2}," +
                        $"{r.NetTotal:F2},{r.AdditionTotal:F2},{r.GrossTotal:F2}," +
                        $"\"{DayworkCommandHelpers.Esc(r.VariationNumber)}\"," +
                        $"\"{DayworkCommandHelpers.Esc(r.SignedBy)}\",\"{DayworkCommandHelpers.Esc(r.PricedBy)}\"");
                File.WriteAllText(csv, sb.ToString());

                int recorded = reg.WithStatus(DayworkStatus.Recorded).Count();
                int signed = reg.WithStatus(DayworkStatus.Signed).Count();
                int priced = reg.WithStatus(DayworkStatus.Priced).Count();
                int attached = reg.Records.Count(r => r.IsAttached);
                int noRef = reg.Records.Count(r => string.IsNullOrWhiteSpace(r.InstructionRef));

                var panel = StingResultPanel.Create("Daywork Register")
                    .SetSubtitle($"{reg.Records.Count} instructed sheet(s) · {ccy}")
                    .AddSection("STATUS")
                    .Metric("Recorded (awaiting signature)", recorded.ToString())
                    .Metric("Signed (awaiting pricing)", signed.ToString())
                    .Metric("Priced", priced.ToString())
                    .AddSection("VALUE")
                    .Metric("Unpriced net prime cost", $"{ccy} {reg.UnpricedNetTotal:N2}")
                    .Metric("Priced gross (all)", $"{ccy} {reg.PricedGrossTotal:N2}")
                    .Metric($"— of which attached to a VO ({attached})",
                        $"{ccy} {MoneyRound.Round(reg.PricedGrossTotal - reg.UnattachedPricedGrossTotal, 2):N2}")
                    .MetricHighlight("To final account (unattached)", $"{ccy} {reg.UnattachedPricedGrossTotal:N2}")
                    .Text("Attached sheets reach the final account through their variation, so only "
                        + "unattached priced sheets are added by the waterfall — no double-count.");

                if (noRef > 0)
                    panel.AddSection("SUBSTANTIATION")
                        .Text($"{noRef} sheet(s) carry no CA instruction reference — hard to substantiate at final account.");

                panel.SetCsvPath(csv);
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Daywork_Register", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }

    // ── Daywork_Price ────────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DayworkPriceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var reg = DayworkEngine.Load(doc);
                var toPrice = reg.Records
                    .Where(r => r != null && r.Status != DayworkStatus.Priced && r.HasResources)
                    .ToList();

                if (toPrice.Count == 0)
                {
                    StingResultPanel.Create("Price dayworks")
                        .AddSection("NOTHING TO PRICE")
                        .Text(reg.Records.Count == 0
                            ? "No daywork sheets recorded."
                            : "Every recorded sheet with resources is already priced.")
                        .Show();
                    return Result.Cancelled;
                }

                string ccy = string.IsNullOrEmpty(reg.Currency) ? DayworkCommandHelpers.Currency(doc) : reg.Currency;

                // Headless gate: "DayworkPriceIds" = comma-separated ids to price;
                // absent ⇒ price every unpriced sheet.
                var idFilter = (UI.StingCommandHandler.GetExtraParam("DayworkPriceIds") ?? "")
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (idFilter.Count > 0)
                    toPrice = toPrice.Where(r => idFilter.Contains(r.Id)).ToList();
                if (toPrice.Count == 0) { message = "No matching daywork sheets to price."; return Result.Cancelled; }

                var warnings = new List<string>();
                string who = Environment.UserName ?? "";
                double pricedTotal = 0;
                foreach (var r in toPrice)
                {
                    // Percentages are applied from the sheet's OWN build-up — the
                    // percentages tendered when it was instructed — not today's
                    // project config, so re-running the command can't silently
                    // reprice historic sheets at changed rates.
                    if (r.BuildUp == null) r.BuildUp = DayworkCommandHelpers.ProjectBuildUp();
                    foreach (var w in r.BuildUp.Warnings())
                        warnings.Add($"{(string.IsNullOrWhiteSpace(r.InstructionRef) ? r.Description : r.InstructionRef)}: {w}");
                    r.Status = DayworkStatus.Priced;
                    r.PricedBy = who;
                    r.PricedDate = DateTime.UtcNow;
                    pricedTotal = MoneyRound.Round(pricedTotal + r.GrossTotal, 2);
                }
                DayworkEngine.Save(doc, reg);

                // Priced-sheet export, annexure style (net → additions → gross).
                string csv = OutputLocationHelper.GetTimestampedPath(doc, "STING_DayworkPriced", ".csv");
                var sb = new StringBuilder();
                sb.AppendLine($"Priced dayworks — {doc.Title} — {DateTime.Now:yyyy-MM-dd HH:mm} — {ccy}");
                sb.AppendLine();
                sb.AppendLine("InstructionRef,Date,Description,Section,Resource,Basis,Rate,LineTotal");
                foreach (var r in toPrice.OrderBy(x => x.InstructionDate))
                {
                    AppendSection(sb, r, "Labour", r.LabourLines);
                    AppendSection(sb, r, "Plant", r.PlantLines);
                    AppendSection(sb, r, "Materials", r.MaterialsLines);
                    sb.AppendLine($"\"{DayworkCommandHelpers.Esc(r.InstructionRef)}\",,,,Net prime cost,,,{r.NetTotal:F2}");
                    sb.AppendLine($"\"{DayworkCommandHelpers.Esc(r.InstructionRef)}\",,,,"
                        + $"Additions (L {r.BuildUp.LabourAdditionPct:0.##}% / P {r.BuildUp.PlantAdditionPct:0.##}% "
                        + $"/ M {r.BuildUp.MaterialsAdditionPct:0.##}%),,,{r.AdditionTotal:F2}");
                    sb.AppendLine($"\"{DayworkCommandHelpers.Esc(r.InstructionRef)}\",,,,SHEET TOTAL,,,{r.GrossTotal:F2}");
                    sb.AppendLine();
                }
                sb.AppendLine($",,,,TOTAL PRICED THIS RUN,,,{pricedTotal:F2}");
                File.WriteAllText(csv, sb.ToString());

                var panel = StingResultPanel.Create("Dayworks priced")
                    .SetSubtitle($"{toPrice.Count} sheet(s) valued at the tendered percentages")
                    .AddSection("PRICED THIS RUN")
                    .Metric("Sheets", toPrice.Count.ToString())
                    .Metric("Net prime cost", $"{ccy} {MoneyRound.Round(toPrice.Sum(r => r.NetTotal), 2):N2}")
                    .Metric("Percentage additions", $"{ccy} {MoneyRound.Round(toPrice.Sum(r => r.AdditionTotal), 2):N2}")
                    .MetricHighlight("Gross", $"{ccy} {pricedTotal:N2}")
                    .AddSection("REGISTER TOTAL")
                    .Metric("Priced gross (all sheets)", $"{ccy} {reg.PricedGrossTotal:N2}")
                    .MetricHighlight("Flowing to final account", $"{ccy} {reg.UnattachedPricedGrossTotal:N2}")
                    .Text("Unattached priced sheets now feed Cost_AnticipatedFinalCost and the "
                        + "Final Account waterfall. Attach a sheet to a VO (Daywork_Attach) to value "
                        + "it through that variation instead.");

                if (warnings.Count > 0)
                    panel.AddSection("CHECK THE PERCENTAGES").Text(string.Join("\n", warnings.Distinct().Take(10)));

                panel.SetCsvPath(csv);
                panel.Show();

                StingLog.Info($"Dayworks priced: {toPrice.Count} sheet(s), {ccy} {pricedTotal:N2}; "
                    + $"register unattached total {reg.UnattachedPricedGrossTotal:N2}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Daywork_Price", ex);
                message = ex.Message; return Result.Failed;
            }
        }

        private static void AppendSection(StringBuilder sb, DayworkRecord r, string section, List<StarRateLine> lines)
        {
            foreach (var l in lines ?? new List<StarRateLine>())
            {
                if (l == null) continue;
                double basis = IsTimeBased(l.Unit) ? l.Hours : (l.Quantity > 0 ? l.Quantity : l.Hours);
                sb.AppendLine(
                    $"\"{DayworkCommandHelpers.Esc(r.InstructionRef)}\",{r.InstructionDate:yyyy-MM-dd}," +
                    $"\"{DayworkCommandHelpers.Esc(r.Description)}\",{section}," +
                    $"\"{DayworkCommandHelpers.Esc(l.Resource)}\",{basis:F2} {l.Unit},{l.UnitRate:F2},{l.LineTotal:F2}");
            }
        }

        private static bool IsTimeBased(string unit)
        {
            switch ((unit ?? "").Trim().ToLowerInvariant())
            {
                case "hr": case "hrs": case "hour": case "hours":
                case "day": case "days": case "week": case "weeks": return true;
                default: return false;
            }
        }
    }

    // ── Daywork_Attach — fill the RateSource="Daywork" valuation seam ────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DayworkAttachCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var attachable = DayworkEngine.AttachableSheets(doc);
                if (attachable.Count == 0)
                {
                    StingResultPanel.Create("Attach daywork to variation")
                        .AddSection("NOTHING TO ATTACH")
                        .Text("No priced, unattached daywork sheets. Run Daywork_Price first.")
                        .Show();
                    return Result.Cancelled;
                }

                var voPaths = VariationEngine.ListVariations(doc);
                var vos = voPaths.Select(p => (path: p, vo: VariationEngine.Load(p)))
                    .Where(t => t.vo != null).ToList();
                if (vos.Count == 0)
                {
                    StingResultPanel.Create("Attach daywork to variation")
                        .AddSection("NO VARIATIONS")
                        .Text("No variations recorded to attach a daywork sheet to.")
                        .Show();
                    return Result.Cancelled;
                }

                // Headless gate: DayworkAttachId + DayworkAttachVo.
                string sheetId = UI.StingCommandHandler.GetExtraParam("DayworkAttachId");
                string voNumber = UI.StingCommandHandler.GetExtraParam("DayworkAttachVo");

                DayworkRecord sheet;
                if (!string.IsNullOrEmpty(sheetId))
                    sheet = attachable.FirstOrDefault(r => string.Equals(r.Id, sheetId, StringComparison.OrdinalIgnoreCase));
                else
                {
                    // Tag carries the record so two sheets with identical labels
                    // can't be confused by a label-match.
                    var items = attachable.Select(r => new Select.StingListPicker.ListItem
                    {
                        Label = $"{(string.IsNullOrWhiteSpace(r.InstructionRef) ? "(no ref)" : r.InstructionRef)} — {r.Description}",
                        Detail = $"{r.Currency} {r.GrossTotal:N2} · priced {r.PricedDate:yyyy-MM-dd}",
                        Tag = r
                    }).ToList();
                    var picked = Select.StingListPicker.Show("Attach daywork sheet",
                        "Pick a priced daywork sheet:", items, false)?.FirstOrDefault();
                    if (picked == null) return Result.Cancelled;
                    sheet = picked.Tag as DayworkRecord;
                }
                if (sheet == null) { message = "Daywork sheet not found or not attachable."; return Result.Failed; }

                VariationInstruction targetVo;
                if (!string.IsNullOrEmpty(voNumber))
                    targetVo = vos.FirstOrDefault(t => string.Equals(t.vo.Number, voNumber, StringComparison.OrdinalIgnoreCase)).vo;
                else
                {
                    var items = vos.Select(t => new Select.StingListPicker.ListItem
                    {
                        Label = t.vo.Number,
                        Detail = $"{t.vo.Status} · {t.vo.Currency} {t.vo.TotalValue:N2}",
                        Tag = t.vo
                    }).ToList();
                    var picked = Select.StingListPicker.Show("Attach to variation",
                        "Pick the variation to value it into:", items, false)?.FirstOrDefault();
                    if (picked == null) return Result.Cancelled;
                    targetVo = picked.Tag as VariationInstruction;
                }
                if (targetVo == null) { message = "Variation not found."; return Result.Failed; }

                // The daywork becomes ONE variation item priced at the sheet gross:
                // quantity 1 × the valued sheet, sourced "Daywork" and linked back.
                targetVo.Items.Add(new VariationItem
                {
                    Description = $"Dayworks — {sheet.Description}"
                        + (string.IsNullOrWhiteSpace(sheet.InstructionRef) ? "" : $" (instruction {sheet.InstructionRef})"),
                    Unit = "sheet",
                    Quantity = 1,
                    UnitRate = sheet.GrossTotal,
                    RateSource = "Daywork",
                    DayworkId = sheet.Id
                });
                VariationEngine.Save(doc, targetVo);

                // Mark the sheet attached so the register stops adding it to the
                // final account separately — its value now rides the VO.
                var reg = DayworkEngine.Load(doc);
                var live = reg.ById(sheet.Id);
                if (live != null) { live.VariationNumber = targetVo.Number; DayworkEngine.Save(doc, reg); }

                StingResultPanel.Create("Daywork attached to variation")
                    .SetSubtitle($"{sheet.Description} → {targetVo.Number}")
                    .AddSection("ATTACHED")
                    .Metric("Sheet value", $"{sheet.Currency} {sheet.GrossTotal:N2}")
                    .Metric("Variation", targetVo.Number)
                    .Metric("Variation total (now)", $"{targetVo.Currency} {targetVo.TotalValue:N2}")
                    .AddSection("FINAL ACCOUNT")
                    .Metric("Still unattached (added separately)",
                        $"{reg.Currency} {reg.UnattachedPricedGrossTotal:N2}")
                    .Text("This sheet now reaches the final account through its variation and is no "
                        + "longer added standalone — the waterfall counts it once.")
                    .Show();

                StingLog.Info($"Daywork {sheet.Id} attached to VO {targetVo.Number} at {sheet.GrossTotal:N2}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Daywork_Attach", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }
}
