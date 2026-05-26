// ══════════════════════════════════════════════════════════════════════════
//  VariationAndEvmCommands.cs — P5.2 + P5.3 user-facing commands.
//
//  P5.2:
//    Variation_FromDiff      — pick a saved diff, mint a draft VO.
//    Variation_BuildStarRate — wizard-style star-rate build-up.
//    Variation_ExportRegister — CSV register of all VOs for a contract.
//  P5.3:
//    Evm_Calculate           — produce an EVM period from BAC/BCWS/BCWP/ACWP.
//    Evm_ImportActuals       — sum a CSV of actuals to-date.
//    Evm_ExportReport        — CSV S-curve export of all periods.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BIMManager;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Evm;
using StingTools.Core.Variation;
using StingTools.Select;

namespace StingTools.Commands.Cost
{
    // ── Variation ────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class VariationFromDiffCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                // Need 2 snapshots to diff. Pick A (older baseline) then B (newer).
                var snapshots = BOQCostManager.ListSnapshots(doc);
                if (snapshots.Count < 2)
                {
                    TaskDialog.Show("STING Variation",
                        "Need at least two BOQ snapshots to mint a variation. Save snapshots before + after the change.");
                    return Result.Cancelled;
                }

                var items = snapshots.Select(s => new StingListPicker.ListItem
                {
                    Label = $"{s.Type,-8} {s.Label}",
                    Detail = $"{s.Date:yyyy-MM-dd HH:mm} — UGX {s.GrandTotalUGX:N0}",
                    Tag = s
                }).ToList();
                var pickedA = StingListPicker.Show("STING — Variation: baseline (A)",
                    "Pick the BASELINE snapshot (the one before the change).",
                    items, allowMultiSelect: false);
                if (pickedA == null || pickedA.Count == 0) return Result.Cancelled;
                var pickedB = StingListPicker.Show("STING — Variation: revised (B)",
                    "Pick the REVISED snapshot (after the change).",
                    items, allowMultiSelect: false);
                if (pickedB == null || pickedB.Count == 0) return Result.Cancelled;

                var snapA = (pickedA[0].Tag as BOQSnapshotMeta);
                var snapB = (pickedB[0].Tag as BOQSnapshotMeta);
                if (snapA == null || snapB == null) return Result.Cancelled;

                // Sanity-check that both snapshot files exist + parse,
                // then hand the paths to CompareSnapshots (which takes
                // string paths, not loaded docs).
                var docA = BOQCostManager.LoadSnapshot(snapA.Path);
                var docB = BOQCostManager.LoadSnapshot(snapB.Path);
                if (docA == null || docB == null)
                {
                    message = "Failed to load one of the snapshots.";
                    return Result.Failed;
                }

                var diff = BOQCostManager.CompareSnapshots(snapA.Path, snapB.Path);
                if (diff == null || diff.CategoryDiffs.Count == 0)
                {
                    TaskDialog.Show("STING Variation",
                        "Snapshots are identical — no variation to mint.");
                    return Result.Cancelled;
                }

                // Pick contract family (Phase 184q — distinct from Kind so
                // the liability map can match precisely on JCT2024 vs
                // FIDIC2017Yellow etc.).
                var formItems = new List<StingListPicker.ListItem>
                {
                    new StingListPicker.ListItem { Label = "JCT 2024",                Detail = "Standard Building Contract 2024 ed.",              Tag = VariationContractForm.JCT2024 },
                    new StingListPicker.ListItem { Label = "JCT 2016",                Detail = "Legacy SBC 2016 ed. (clauses 5.1 split)",          Tag = VariationContractForm.JCT2016 },
                    new StingListPicker.ListItem { Label = "NEC4 ECC",                Detail = "Engineering and Construction Contract — Option A-F", Tag = VariationContractForm.NEC4 },
                    new StingListPicker.ListItem { Label = "FIDIC 2017 Red",          Detail = "Conditions of Contract — employer-design",        Tag = VariationContractForm.FIDIC2017Red },
                    new StingListPicker.ListItem { Label = "FIDIC 2017 Yellow",       Detail = "Plant + Design-Build — contractor-design",        Tag = VariationContractForm.FIDIC2017Yellow },
                    new StingListPicker.ListItem { Label = "FIDIC 2017 Silver",       Detail = "EPC / Turnkey — contractor owns nearly everything", Tag = VariationContractForm.FIDIC2017Silver },
                    new StingListPicker.ListItem { Label = "GC/Works",                Detail = "Legacy UK public-sector forms",                    Tag = VariationContractForm.GCWorks },
                    new StingListPicker.ListItem { Label = "Bespoke",                 Detail = "Project-specific bespoke contract",                Tag = VariationContractForm.Bespoke },
                };
                var formPicked = StingListPicker.Show("STING — Contract form",
                    "Pick the contract family. Drives liability defaults and clause references.",
                    formItems, allowMultiSelect: false);
                VariationContractForm contractForm = (formPicked != null && formPicked.Count > 0 &&
                    formPicked[0].Tag is VariationContractForm cf) ? cf : VariationContractForm.JCT2024;

                // Pick kind (contractual route — i.e. how the change is being routed).
                var kindItems = new List<StingListPicker.ListItem>
                {
                    new StingListPicker.ListItem { Label = "Architect's / engineer's instruction", Tag = VariationKind.Instruction },
                    new StingListPicker.ListItem { Label = "NEC4 compensation event", Tag = VariationKind.CompensationEvent },
                    new StingListPicker.ListItem { Label = "FIDIC engineer instruction", Tag = VariationKind.EngineerInstruction },
                    new StingListPicker.ListItem { Label = "Contractor claim", Tag = VariationKind.ContractorClaim }
                };
                var kindPicked = StingListPicker.Show("STING — Variation kind",
                    "Pick the contractual route — how this change is being issued under the form.",
                    kindItems, allowMultiSelect: false);
                VariationKind kind = (kindPicked != null && kindPicked.Count > 0 &&
                    kindPicked[0].Tag is VariationKind k) ? k : VariationKind.Instruction;

                // Phase 184o — pick reason (why) + liability (who pays).
                // Drives EOT, insurance routing, month-end reporting.
                var reasonItems = new List<StingListPicker.ListItem>
                {
                    new StingListPicker.ListItem { Label = "Design change",        Detail = "Designer-initiated change to drawings / specs", Tag = VariationReason.DesignChange },
                    new StingListPicker.ListItem { Label = "Client request",       Detail = "Employer-initiated scope or quality change",     Tag = VariationReason.ClientRequest },
                    new StingListPicker.ListItem { Label = "Site condition",       Detail = "Unforeseen ground / existing-fabric condition",   Tag = VariationReason.SiteCondition },
                    new StingListPicker.ListItem { Label = "Statutory change",     Detail = "Change in law, permit, building control",         Tag = VariationReason.StatutoryChange },
                    new StingListPicker.ListItem { Label = "Error / omission",     Detail = "Error in tender docs — designer or contractor",   Tag = VariationReason.ErrorOmission },
                    new StingListPicker.ListItem { Label = "Contractor proposal",  Detail = "Value-engineering proposal accepted by employer", Tag = VariationReason.ContractorProposal },
                    new StingListPicker.ListItem { Label = "Scope addition",       Detail = "New scope added to contract",                     Tag = VariationReason.ScopeAddition },
                    new StingListPicker.ListItem { Label = "Scope omission",       Detail = "Scope removed from contract",                     Tag = VariationReason.ScopeOmission },
                    new StingListPicker.ListItem { Label = "Specification",        Detail = "Material / spec substitution",                    Tag = VariationReason.Specification },
                    new StingListPicker.ListItem { Label = "Quality",              Detail = "Quality-driven enhancement / rework",             Tag = VariationReason.Quality },
                    new StingListPicker.ListItem { Label = "Programme change",     Detail = "Acceleration / deceleration / re-sequencing",     Tag = VariationReason.ProgrammeChange },
                    new StingListPicker.ListItem { Label = "Other",                Detail = "Bespoke / non-standard cause",                    Tag = VariationReason.Other }
                };
                var reasonPicked = StingListPicker.Show("STING — Variation reason",
                    "Why did this variation arise? Drives EOT entitlement, insurance routing and month-end reporting.",
                    reasonItems, allowMultiSelect: false);
                VariationReason reason = (reasonPicked != null && reasonPicked.Count > 0 &&
                    reasonPicked[0].Tag is VariationReason r) ? r : VariationReason.Other;

                // Suggest liability — consult the config-driven contract
                // map (Phase 184q now passes the precise contract form,
                // not Kind.ToString(), so FIDIC Yellow / Silver carry
                // their contractor-design defaults correctly).
                VariationLiability codeDefault = SuggestLiability(reason);
                VariationLiability suggested = VariationLiabilityMap
                    .Get(doc).Resolve(contractForm.ToString(), reason, codeDefault);
                var liabilityItems = new List<StingListPicker.ListItem>
                {
                    new StingListPicker.ListItem { Label = "Employer / client",   Detail = "Employer absorbs cost",                       Tag = VariationLiability.Employer },
                    new StingListPicker.ListItem { Label = "Contractor",          Detail = "Contractor absorbs cost",                     Tag = VariationLiability.Contractor },
                    new StingListPicker.ListItem { Label = "Designer",            Detail = "Routed via designer's PI insurance",          Tag = VariationLiability.Designer },
                    new StingListPicker.ListItem { Label = "Shared",              Detail = "Proportionate split by agreement",            Tag = VariationLiability.Shared },
                    new StingListPicker.ListItem { Label = "Force majeure",       Detail = "Unforeseen — typically employer + insurance", Tag = VariationLiability.ForceMajeure },
                };
                var liabilityPicked = StingListPicker.Show("STING — Liability",
                    $"Who pays for this variation? Suggested from reason: {suggested}.",
                    liabilityItems, allowMultiSelect: false);
                VariationLiability liability = (liabilityPicked != null && liabilityPicked.Count > 0 &&
                    liabilityPicked[0].Tag is VariationLiability l) ? l : suggested;

                // Phase 184r — EOT band picker. Capturing this on
                // mint means the cash-flow's monthly_eot_adjusted curve
                // (Phase 184q) lights up automatically once the QS
                // approves the VO, rather than the user having to edit
                // the JSON sidecar manually.
                int eotDays = PickEotDays();

                // Phase 184r — short free-text rationale via the existing
                // WPF input prompt. The PaymentCert detail dialog uses
                // a similar lightweight prompt; reuse the pattern via a
                // small Window so we don't pull in a heavier dependency.
                string reasonDetail = PromptForReasonDetail();

                string contractRef = doc.ProjectInformation?.Number ?? "DEFAULT";
                var vo = VariationEngine.FromDiff(diff, contractRef, kind,
                    reason, liability, reasonDetail: reasonDetail, eotDays: eotDays,
                    contractForm: contractForm);
                string path = VariationEngine.Save(doc, vo);

                TaskDialog.Show("STING — Variation minted",
                    $"{vo.Number}  ({vo.Kind}, {vo.Status})\n\n" +
                    $"Contract:     {vo.ContractForm}\n" +
                    $"Reason:       {vo.Reason}\n" +
                    $"Liability:    {vo.Liability}\n" +
                    $"Items:        {vo.Items.Count}\n" +
                    $"Total value:  {vo.Currency} {vo.TotalValue:N2}\n\n" +
                    $"Path: {Path.GetFileName(path)}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Variation_FromDiff", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Phase 184r — EOT band picker. Pre-set bands cover the
        /// common QS values (0 / 1 / 3 / 7 / 14 / 21 / 30 / 60 / 90+).
        /// Returns 0 when the user cancels so the VO mints cleanly
        /// without an EOT impact.
        /// </summary>
        private static int PickEotDays()
        {
            var items = new List<StingListPicker.ListItem>
            {
                new() { Label = "0 days",   Detail = "No time impact",                    Tag = 0  },
                new() { Label = "1 day",    Detail = "Trivial / single-task slip",         Tag = 1  },
                new() { Label = "3 days",   Detail = "Half-week",                          Tag = 3  },
                new() { Label = "5 days",   Detail = "One working week",                   Tag = 5  },
                new() { Label = "7 days",   Detail = "Calendar week",                      Tag = 7  },
                new() { Label = "14 days",  Detail = "Two calendar weeks",                 Tag = 14 },
                new() { Label = "21 days",  Detail = "Three calendar weeks",               Tag = 21 },
                new() { Label = "30 days",  Detail = "One calendar month",                 Tag = 30 },
                new() { Label = "60 days",  Detail = "Two months — significant impact",    Tag = 60 },
                new() { Label = "90 days",  Detail = "Quarter — major delay",              Tag = 90 },
            };
            var picked = StingListPicker.Show("STING — EOT entitlement",
                "How many calendar days of programme extension does this variation justify? Drives the cash-flow EOT-adjusted curve.",
                items, allowMultiSelect: false);
            if (picked == null || picked.Count == 0) return 0;
            return picked[0].Tag is int days ? days : 0;
        }

        /// <summary>
        /// Phase 184r — short free-text rationale captured via a
        /// minimal WPF input window. The detail is stored on
        /// VariationInstruction.ReasonDetail so the mobile detail
        /// screen can surface it below the reason/liability badges.
        /// Empty / cancelled is fine — the field is optional.
        /// </summary>
        private static string PromptForReasonDetail()
        {
            try
            {
                var w = new System.Windows.Window
                {
                    Title = "STING — Variation rationale (optional)",
                    Width = 480, Height = 220,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false,
                    ResizeMode = System.Windows.ResizeMode.NoResize
                };
                var sp = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(14) };
                sp.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "Add a short rationale (optional). Appears on the cert + mobile detail.",
                    FontSize = 12, TextWrapping = System.Windows.TextWrapping.Wrap,
                    Margin = new System.Windows.Thickness(0, 0, 0, 8)
                });
                var tb = new System.Windows.Controls.TextBox
                {
                    Height = 80,
                    AcceptsReturn = true,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
                };
                sp.Children.Add(tb);
                var row = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Margin = new System.Windows.Thickness(0, 10, 0, 0)
                };
                var ok = new System.Windows.Controls.Button
                { Content = "OK", Width = 80, Height = 26,
                  Margin = new System.Windows.Thickness(0, 0, 6, 0), IsDefault = true };
                var cancel = new System.Windows.Controls.Button
                { Content = "Skip", Width = 80, Height = 26, IsCancel = true };
                ok.Click += (s, e) => { w.DialogResult = true; };
                row.Children.Add(ok);
                row.Children.Add(cancel);
                sp.Children.Add(row);
                w.Content = sp;
                tb.Focus();
                bool? result = w.ShowDialog();
                return result == true ? (tb.Text ?? "").Trim() : "";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PromptForReasonDetail: {ex.Message}");
                return "";
            }
        }

        // Default liability suggestion per reason. The picker still lets
        // the QS override — this just front-loads the common case so
        // they can hit Enter on the typical assignment.
        private static VariationLiability SuggestLiability(VariationReason reason)
        {
            switch (reason)
            {
                case VariationReason.DesignChange:
                case VariationReason.ErrorOmission:        return VariationLiability.Designer;
                case VariationReason.ClientRequest:
                case VariationReason.ScopeAddition:
                case VariationReason.ScopeOmission:
                case VariationReason.Specification:
                case VariationReason.Quality:              return VariationLiability.Employer;
                case VariationReason.SiteCondition:
                case VariationReason.StatutoryChange:      return VariationLiability.Employer;
                case VariationReason.ContractorProposal:   return VariationLiability.Shared;
                case VariationReason.ProgrammeChange:      return VariationLiability.Employer;
                default:                                    return VariationLiability.Employer;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class VariationBuildStarRateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                // Minimal build-up wizard — the WPF version comes in P5.4.
                // Today: a seeded star rate the QS can edit in the JSON file.
                var rate = new StarRate
                {
                    Description = "Star rate build-up — edit in JSON",
                    Unit = "each",
                    Author = Environment.UserName ?? "",
                    LabourLines = new List<StarRateLine>
                    {
                        new StarRateLine { Resource = "Skilled labourer", Hours = 8, UnitRate = 28, Unit = "hr" },
                        new StarRateLine { Resource = "General labourer", Hours = 8, UnitRate = 18, Unit = "hr" }
                    },
                    PlantLines = new List<StarRateLine>
                    {
                        new StarRateLine { Resource = "Excavator 8t", Hours = 4, UnitRate = 65, Unit = "hr" }
                    },
                    MaterialsLines = new List<StarRateLine>
                    {
                        new StarRateLine { Resource = "Concrete C30/37", Quantity = 1, UnitRate = 135, Unit = "m³" }
                    },
                    OverheadPercent = 8.0,
                    ProfitPercent = 5.0
                };
                string path = VariationEngine.SaveStarRate(doc, rate);

                TaskDialog.Show("STING — Star rate created",
                    $"Star-rate template saved.\n\n" +
                    $"Labour:    GBP {rate.LabourTotal:N2}\n" +
                    $"Plant:     GBP {rate.PlantTotal:N2}\n" +
                    $"Materials: GBP {rate.MaterialsTotal:N2}\n" +
                    $"Subtotal:  GBP {rate.Subtotal:N2}\n" +
                    $"OH ({rate.OverheadPercent}%): GBP {rate.OverheadAmount:N2}\n" +
                    $"Profit ({rate.ProfitPercent}%): GBP {rate.ProfitAmount:N2}\n" +
                    $"FINAL:     GBP {rate.FinalRate:N2}\n\n" +
                    $"Edit at: {Path.GetFileName(path)}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Variation_BuildStarRate", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class VariationExportRegisterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }
                var paths = VariationEngine.ListVariations(doc);
                if (paths.Count == 0)
                {
                    TaskDialog.Show("STING Variation", "No variations recorded.");
                    return Result.Cancelled;
                }
                var vos = paths.Select(VariationEngine.Load).Where(v => v != null)
                    .OrderBy(v => v.ContractRef).ThenBy(v => v.Number).ToList();

                string outDir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "variations");
                string outPath = Path.Combine(outDir,
                    $"variation_register_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                using (var sw = new StreamWriter(outPath))
                {
                    // Phase 184o — Reason / Liability / EotDays / ReasonDetail columns
                    // added so month-end pattern analysis can pivot by reason ("60% of
                    // VOs are design errors → review the design") and the QS can
                    // reconcile EOT entitlement against the programme.
                    sw.WriteLine("Contract,ContractForm,Number,Kind,Reason,Liability,EotDays,Status,InstructionDate,ApprovalDate,Items,Currency,TotalValue,IssuedBy,ApprovedBy,ReasonDetail");
                    foreach (var v in vos)
                    {
                        sw.WriteLine(string.Join(",", new[]
                        {
                            Q(v.ContractRef),
                            v.ContractForm.ToString(),
                            v.Number,
                            v.Kind.ToString(),
                            v.Reason.ToString(),
                            v.Liability.ToString(),
                            v.EotDays.ToString(CultureInfo.InvariantCulture),
                            v.Status.ToString(),
                            v.InstructionDate.ToString("yyyy-MM-dd"),
                            v.ApprovalDate?.ToString("yyyy-MM-dd") ?? "",
                            v.Items.Count.ToString(CultureInfo.InvariantCulture),
                            v.Currency,
                            v.TotalValue.ToString("F2", CultureInfo.InvariantCulture),
                            Q(v.IssuedBy),
                            Q(v.ApprovedBy),
                            Q(v.ReasonDetail)
                        }));
                    }
                }
                TaskDialog.Show("STING — Variation register",
                    $"{vos.Count} variation(s) exported to:\n{outPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Variation_ExportRegister", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string Q(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"")) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }

    // ── EVM ──────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class EvmCalculateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                // Build a single period from live BOQ + actuals CSV.
                var boq = BOQCostManager.BuildBOQDocument(doc);
                double bac = boq.GrandTotalUGX;

                // BCWS — use the current BOQ value × (planned % at this date).
                // No 4D wiring yet in this commit; QS sets BCWS via Cost_ReloadRules
                // workflow override later. For now, BCWS == BAC × PercentComplete
                // estimate based on weighted ASS_PMT_PCT_COMPLETE_NR.
                double pctEarned = WeightedPctComplete(doc);
                double bcwp = bac * pctEarned / 100.0;
                double bcws = bcwp; // optimistic placeholder until 4D wired

                // ACWP — sum the most recent actuals CSV under _bim_manager/actuals/.
                string actualsDir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "actuals");
                double acwp = 0;
                if (Directory.Exists(actualsDir))
                {
                    var latest = Directory.EnumerateFiles(actualsDir, "actuals_*.csv")
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(latest))
                        acwp = EvmCalculator.ImportActualsToDate(latest, DateTime.UtcNow);
                }

                var period = EvmCalculator.Compute(bac, bcws, bcwp, acwp, DateTime.UtcNow);

                // Append to existing report or create new.
                var existing = EvmCalculator.ListReports(doc).FirstOrDefault();
                var report = existing != null ? EvmCalculator.Load(existing) : null;
                if (report == null) report = new EvmReport
                {
                    ProjectName = doc.ProjectInformation?.Name ?? "",
                    ContractRef = doc.ProjectInformation?.Number ?? "",
                    Currency = boq.Currency
                };
                report.Periods.Add(period);
                string path = EvmCalculator.Save(doc, report);

                TaskDialog.Show("STING — EVM period",
                    $"Period {period.PeriodLabel}\n\n" +
                    $"BAC  {report.Currency} {period.Bac:N0}\n" +
                    $"BCWS {report.Currency} {period.Bcws:N0}\n" +
                    $"BCWP {report.Currency} {period.Bcwp:N0}\n" +
                    $"ACWP {report.Currency} {period.Acwp:N0}\n\n" +
                    $"CV   {period.Cv:N0}\n" +
                    $"SV   {period.Sv:N0}\n" +
                    $"CPI  {period.Cpi:F2}  ({period.CostHealth})\n" +
                    $"SPI  {period.Spi:F2}  ({period.ScheduleHealth})\n" +
                    $"EAC  {report.Currency} {period.Eac:N0}\n" +
                    $"ETC  {report.Currency} {period.Etc:N0}\n" +
                    $"VAC  {report.Currency} {period.Vac:N0}\n\n" +
                    $"Saved: {Path.GetFileName(path)}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Evm_Calculate", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static double WeightedPctComplete(Document doc)
        {
            try
            {
                double weightSum = 0, valueSum = 0;
                var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (Element el in col)
                {
                    Parameter p = el.LookupParameter(ParamRegistry.PMT_PCT_COMPLETE_NR);
                    if (p == null || !p.HasValue) continue;
                    Parameter tot = el.LookupParameter("CST_MODELED_TOTAL_UGX");
                    if (tot == null || !tot.HasValue) continue;
                    double pct = p.AsDouble();
                    double val = tot.AsDouble();
                    weightSum += pct * val;
                    valueSum += val;
                }
                return valueSum > 0 ? weightSum / valueSum : 0;
            }
            catch (Exception ex) { StingLog.Warn($"WeightedPctComplete: {ex.Message}"); return 0; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class EvmImportActualsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                string dir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "actuals");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    TaskDialog.Show("STING EVM",
                        $"Created actuals directory:\n{dir}\n\n" +
                        "Drop CSV files named actuals_YYYYMMDD.csv with columns " +
                        "Date,Section,Amount and re-run.");
                    return Result.Succeeded;
                }

                var files = Directory.EnumerateFiles(dir, "actuals_*.csv")
                    .OrderByDescending(File.GetLastWriteTimeUtc).ToList();
                if (files.Count == 0)
                {
                    TaskDialog.Show("STING EVM",
                        $"No actuals CSV files found under {dir}.");
                    return Result.Cancelled;
                }
                double total = EvmCalculator.ImportActualsToDate(files[0], DateTime.UtcNow);
                TaskDialog.Show("STING — Actuals imported",
                    $"File: {Path.GetFileName(files[0])}\n\n" +
                    $"Cumulative ACWP to {DateTime.UtcNow:yyyy-MM-dd}: GBP {total:N2}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Evm_ImportActuals", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class EvmExportReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }
                var reports = EvmCalculator.ListReports(doc);
                if (reports.Count == 0)
                {
                    TaskDialog.Show("STING EVM", "No EVM reports saved. Run Evm_Calculate first.");
                    return Result.Cancelled;
                }
                var rpt = EvmCalculator.Load(reports[0]);
                if (rpt == null || rpt.Periods.Count == 0)
                {
                    TaskDialog.Show("STING EVM", "Latest report has no periods.");
                    return Result.Cancelled;
                }
                string outDir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "evm");
                string outPath = Path.Combine(outDir,
                    $"evm_scurve_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                using (var sw = new StreamWriter(outPath))
                {
                    sw.WriteLine("Period,BAC,BCWS,BCWP,ACWP,CV,SV,CPI,SPI,EAC,ETC,VAC,TCPI,CostHealth,ScheduleHealth");
                    foreach (var p in rpt.Periods.OrderBy(x => x.PeriodEnd))
                    {
                        sw.WriteLine(string.Join(",", new[]
                        {
                            p.PeriodLabel,
                            p.Bac.ToString("F2", CultureInfo.InvariantCulture),
                            p.Bcws.ToString("F2", CultureInfo.InvariantCulture),
                            p.Bcwp.ToString("F2", CultureInfo.InvariantCulture),
                            p.Acwp.ToString("F2", CultureInfo.InvariantCulture),
                            p.Cv.ToString("F2", CultureInfo.InvariantCulture),
                            p.Sv.ToString("F2", CultureInfo.InvariantCulture),
                            p.Cpi.ToString("F4", CultureInfo.InvariantCulture),
                            p.Spi.ToString("F4", CultureInfo.InvariantCulture),
                            p.Eac.ToString("F2", CultureInfo.InvariantCulture),
                            p.Etc.ToString("F2", CultureInfo.InvariantCulture),
                            p.Vac.ToString("F2", CultureInfo.InvariantCulture),
                            p.Tcpi.ToString("F4", CultureInfo.InvariantCulture),
                            p.CostHealth,
                            p.ScheduleHealth
                        }));
                    }
                }
                TaskDialog.Show("STING — EVM exported", $"S-curve written to:\n{outPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Evm_ExportReport", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Variation_ReclassifyLegacy — Phase 184p / caveat #2 closure.
    //
    //  Walks every saved variation with Reason == Other AND
    //  Liability == Employer (the migration defaults) and lets the QS
    //  pick the correct reason + liability for each one via the
    //  existing StingListPicker flow. The Variation_FromDiff picker is
    //  reused so the QS sees the same affordances either way.
    // ──────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class VariationReclassifyLegacyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var paths = VariationEngine.ListVariations(doc);
                var legacy = paths
                    .Select(VariationEngine.Load)
                    .Where(v => v != null
                        && v.Reason == VariationReason.Other
                        && v.Liability == VariationLiability.Employer)
                    .OrderBy(v => v.ContractRef).ThenBy(v => v.Number)
                    .ToList();

                if (legacy.Count == 0)
                {
                    TaskDialog.Show("STING — Reclassify legacy variations",
                        "Nothing to reclassify. Every variation has a non-default reason / liability assigned.");
                    return Result.Succeeded;
                }

                // Multi-select picker — QS chooses which legacy VOs to walk.
                var items = legacy.Select(v => new StingListPicker.ListItem
                {
                    Label = $"{v.Number}  ({v.Kind}, {v.Currency} {v.TotalValue:N0})",
                    Detail = v.Title,
                    Tag = v
                }).ToList();

                var picked = StingListPicker.Show("STING — Reclassify legacy variations",
                    $"{legacy.Count} variation(s) still on the default Other / Employer. " +
                    "Pick which to reclassify (multi-select).",
                    items, allowMultiSelect: true);
                if (picked == null || picked.Count == 0) return Result.Cancelled;

                int reclassified = 0;
                int skipped = 0;
                foreach (var li in picked)
                {
                    if (!(li.Tag is VariationInstruction vo)) { skipped++; continue; }

                    // Reason picker per-variation.
                    var reasonItems = BuildReasonItems();
                    var reasonPicked = StingListPicker.Show(
                        $"STING — Reason for {vo.Number}",
                        $"{vo.Title}\n\nWhy did this variation arise?",
                        reasonItems, allowMultiSelect: false);
                    if (reasonPicked == null || reasonPicked.Count == 0)
                    {
                        skipped++;
                        continue;
                    }
                    var reason = (reasonPicked[0].Tag is VariationReason r) ? r : VariationReason.Other;

                    // Liability picker, pre-suggested from the map. Use
                    // the precise contract form (Phase 184q); legacy VOs
                    // that haven't been touched default to JCT2024 which
                    // is the safest UK QS assumption.
                    var codeDefault = SuggestLiabilityShared(reason);
                    var suggested = VariationLiabilityMap.Get(doc)
                        .Resolve(vo.ContractForm.ToString(), reason, codeDefault);
                    var liabilityItems = BuildLiabilityItems();
                    var liabilityPicked = StingListPicker.Show(
                        $"STING — Liability for {vo.Number}",
                        $"Who pays? Suggested: {suggested}.",
                        liabilityItems, allowMultiSelect: false);
                    var liability = (liabilityPicked != null && liabilityPicked.Count > 0
                        && liabilityPicked[0].Tag is VariationLiability lib)
                            ? lib : suggested;

                    vo.Reason = reason;
                    vo.Liability = liability;
                    VariationEngine.Save(doc, vo);
                    reclassified++;
                }

                TaskDialog.Show("STING — Reclassification complete",
                    $"Reclassified: {reclassified}\nSkipped (no reason picked): {skipped}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Variation_ReclassifyLegacy", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // Mirrors VariationFromDiffCommand's reason picker so the two
        // entry points present identical options to the QS.
        private static List<StingListPicker.ListItem> BuildReasonItems() => new List<StingListPicker.ListItem>
        {
            new() { Label = "Design change",        Detail = "Designer-initiated change to drawings / specs",   Tag = VariationReason.DesignChange },
            new() { Label = "Client request",       Detail = "Employer-initiated scope or quality change",       Tag = VariationReason.ClientRequest },
            new() { Label = "Site condition",       Detail = "Unforeseen ground / existing-fabric condition",    Tag = VariationReason.SiteCondition },
            new() { Label = "Statutory change",     Detail = "Change in law, permit, building control",          Tag = VariationReason.StatutoryChange },
            new() { Label = "Error / omission",     Detail = "Error in tender docs — designer or contractor",    Tag = VariationReason.ErrorOmission },
            new() { Label = "Contractor proposal",  Detail = "Value-engineering proposal accepted by employer",  Tag = VariationReason.ContractorProposal },
            new() { Label = "Scope addition",       Detail = "New scope added to contract",                      Tag = VariationReason.ScopeAddition },
            new() { Label = "Scope omission",       Detail = "Scope removed from contract",                      Tag = VariationReason.ScopeOmission },
            new() { Label = "Specification",        Detail = "Material / spec substitution",                     Tag = VariationReason.Specification },
            new() { Label = "Quality",              Detail = "Quality-driven enhancement / rework",              Tag = VariationReason.Quality },
            new() { Label = "Programme change",     Detail = "Acceleration / deceleration / re-sequencing",      Tag = VariationReason.ProgrammeChange },
            new() { Label = "Other",                Detail = "Bespoke / non-standard cause",                     Tag = VariationReason.Other },
        };

        private static List<StingListPicker.ListItem> BuildLiabilityItems() => new List<StingListPicker.ListItem>
        {
            new() { Label = "Employer / client",   Detail = "Employer absorbs cost",                       Tag = VariationLiability.Employer },
            new() { Label = "Contractor",          Detail = "Contractor absorbs cost",                     Tag = VariationLiability.Contractor },
            new() { Label = "Designer",            Detail = "Routed via designer's PI insurance",          Tag = VariationLiability.Designer },
            new() { Label = "Shared",              Detail = "Proportionate split by agreement",            Tag = VariationLiability.Shared },
            new() { Label = "Force majeure",       Detail = "Unforeseen — typically employer + insurance", Tag = VariationLiability.ForceMajeure },
        };

        // Duplicates VariationFromDiffCommand.SuggestLiability so this
        // command stays self-contained when the other one isn't used.
        private static VariationLiability SuggestLiabilityShared(VariationReason reason)
        {
            switch (reason)
            {
                case VariationReason.DesignChange:
                case VariationReason.ErrorOmission:        return VariationLiability.Designer;
                case VariationReason.ClientRequest:
                case VariationReason.ScopeAddition:
                case VariationReason.ScopeOmission:
                case VariationReason.Specification:
                case VariationReason.Quality:              return VariationLiability.Employer;
                case VariationReason.SiteCondition:
                case VariationReason.StatutoryChange:      return VariationLiability.Employer;
                case VariationReason.ContractorProposal:   return VariationLiability.Shared;
                case VariationReason.ProgrammeChange:      return VariationLiability.Employer;
                default:                                    return VariationLiability.Employer;
            }
        }
    }
}
