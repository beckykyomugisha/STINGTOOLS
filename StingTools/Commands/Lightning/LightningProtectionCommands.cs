// StingTools — LightningProtectionCommands.cs
//
// Ten Lightning Protection System commands wired into the BIM tab of the
// dock panel. Each command is a thin orchestration layer over LpsEngine;
// all calculation logic lives there. Commands write the
// ELC_LPS_COMPLIANCE_STATUS_TXT sentinel parameter so downstream
// schedules can surface plugin-evaluated verdicts.
//
// Per CLAUDE.md: every write goes through a "STING …" Transaction; every
// command implements both IExternalCommand and IPanelCommand; user
// errors surface via TaskDialog, never MessageBox.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using MiniSoftware;
using Newtonsoft.Json.Linq;
using StingTools.Core;
// Disambiguate WPF controls from Autodesk.Revit.UI.TextBox/ComboBox.
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using StingTools.Core.Lightning;
using StingTools.Core.Fabrication;
using StingTools.UI;

namespace StingTools.Commands.Lightning
{
    // ════════════════════════════════════════════════════════════════
    //  CMD-1 — LPS Class Setup wizard
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsClassSetupCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — LPS Class Setup", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var wizard = new LpsClassSetupWizard(doc);
            wizard.ShowDialog();
            if (!wizard.IsCompleted) return Result.Cancelled;

            var risk = wizard.RiskResult;
            string classId = wizard.SelectedClass;
            var def = LpsEngine.LoadClass(classId);
            if (def == null)
            {
                TaskDialog.Show("STING — LPS Class Setup", $"Unknown LPS class '{classId}'.");
                return Result.Failed;
            }

            try
            {
                using (var t = new Transaction(doc, "STING — Apply LPS Class"))
                {
                    t.Start();
                    var prj = doc.ProjectInformation;
                    ParameterHelpers.SetString(prj, LpsParams.CLASS_TXT, classId, true);
                    SetDouble(prj, LpsParams.ROLLING_SPHERE_RADIUS_M, def.RollingSphereRadiusM);
                    SetDouble(prj, LpsParams.MESH_SIZE_M, def.MeshSizeM);
                    SetInt(prj, LpsParams.INSPECTION_INTERVAL_MONTHS, def.InspectionIntervalMonths);
                    string riskRef = $"BS EN 62305-2 — {DateTime.Today:yyyy-MM-dd} — Class {classId}";
                    ParameterHelpers.SetString(prj, LpsParams.RISK_ASSESSMENT_TXT, riskRef, true);

                    // Persist the project Ng override (0 clears it back to regional default).
                    SetDouble(prj, LpsParams.PROJECT_NG_OVERRIDE_NR, wizard.NgOverride);

                    // Stamp separation distance on every existing down conductor.
                    var conductors = LpsEngine.CollectLpsFamily(doc, "Down Conductor", "Down_Conductor", "DownConductor");
                    int stamped = 0;
                    foreach (var dc in conductors)
                    {
                        double zM = ConductorLengthM(dc);
                        string mat = ParameterHelpers.GetString(dc, LpsParams.CONDUCTOR_MATERIAL_TXT);
                        if (string.IsNullOrWhiteSpace(mat)) mat = "COPPER";
                        double sMm = LpsEngine.ComputeSeparationDistance(classId, zM, mat);
                        SetDouble(dc, LpsParams.SEPARATION_DISTANCE_MM, sMm);
                        if (string.IsNullOrWhiteSpace(ParameterHelpers.GetString(dc, LpsParams.CONDUCTOR_MATERIAL_TXT)))
                            ParameterHelpers.SetString(dc, LpsParams.CONDUCTOR_MATERIAL_TXT, mat);
                        stamped++;
                    }
                    t.Commit();

                    TaskDialog.Show("STING — LPS Class Setup",
                        $"Class {classId} applied to project.\n\n" +
                        $"Rolling sphere radius: {def.RollingSphereRadiusM} m\n" +
                        $"Mesh size: {def.MeshSizeM} m\n" +
                        $"Inspection interval: {def.InspectionIntervalMonths} months\n" +
                        $"Down conductors stamped with separation distance: {stamped}\n\n" +
                        $"Risk: {risk?.Notes}");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("LpsClassSetup failed", ex);
                TaskDialog.Show("STING — LPS Class Setup", "Setup failed: " + ex.Message);
                return Result.Failed;
            }
        }

        private static double ConductorLengthM(FamilyInstance fi)
        {
            try
            {
                var bb = fi.get_BoundingBox(null);
                if (bb == null) return 3.0;
                double zFt = bb.Max.Z - bb.Min.Z;
                double zM = UnitUtils.ConvertFromInternalUnits(zFt, UnitTypeId.Meters);
                return zM > 0.1 ? zM : 3.0;
            }
            catch (Exception ex) { StingLog.Warn($"ConductorLengthM: {ex.Message}"); return 3.0; }
        }

        private static void SetDouble(Element el, string paramName, double valueInRevitDisplay)
        {
            try
            {
                var p = el?.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Double) p.Set(valueInRevitDisplay);
                else if (p.StorageType == StorageType.String) p.Set(valueInRevitDisplay.ToString("F2"));
                else if (p.StorageType == StorageType.Integer) p.Set((int)Math.Round(valueInRevitDisplay));
            }
            catch (Exception ex) { StingLog.Warn($"SetDouble {paramName}: {ex.Message}"); }
        }

        private static void SetInt(Element el, string paramName, int value)
        {
            try
            {
                var p = el?.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Integer) p.Set(value);
                else if (p.StorageType == StorageType.Double) p.Set((double)value);
                else if (p.StorageType == StorageType.String) p.Set(value.ToString());
            }
            catch (Exception ex) { StingLog.Warn($"SetInt {paramName}: {ex.Message}"); }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CMD-2 — LPS Compliance Check
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsComplianceCheckCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — LPS Compliance", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            string classId = ParameterHelpers.GetString(doc.ProjectInformation, LpsParams.CLASS_TXT);
            if (string.IsNullOrWhiteSpace(classId))
            {
                TaskDialog.Show("STING — LPS Compliance", "Run LPS Class Setup first to define the project class.");
                return Result.Cancelled;
            }

            var items = LpsEngine.ValidateModel(doc);
            int pass = items.Count(i => i.Severity == LpsSeverity.Pass);
            int warn = items.Count(i => i.Severity == LpsSeverity.Warn);
            int fail = items.Count(i => i.Severity == LpsSeverity.Fail);
            string verdict = fail > 0 ? $"FAIL — {fail} items" : warn > 0 ? "WARN" : "PASS";

            try
            {
                using (var t = new Transaction(doc, "STING — Stamp LPS Compliance"))
                {
                    t.Start();
                    ParameterHelpers.SetString(doc.ProjectInformation,
                        LpsParams.COMPLIANCE_STATUS_TXT, verdict, true);
                    t.Commit();
                }
            }
            catch (Exception ex) { StingLog.Warn($"Stamp compliance: {ex.Message}"); }

            var panel = StingResultPanel.Create("LPS Compliance Check (BS EN 62305)");
            panel.SetSubtitle($"Project class: {classId} — verdict: {verdict}");
            panel.AddSection("SUMMARY")
                 .Metric("Total checks", items.Count.ToString())
                 .MetricHighlight("Pass", pass.ToString())
                 .MetricWarn("Warn", warn.ToString())
                 .MetricError("Fail", fail.ToString());

            if (fail > 0)
            {
                panel.AddSection("FAIL");
                foreach (var i in items.Where(x => x.Severity == LpsSeverity.Fail))
                    panel.Text($"  ▸ [{i.CheckName}] {i.Message}");
            }
            if (warn > 0)
            {
                panel.AddSection("WARN");
                foreach (var i in items.Where(x => x.Severity == LpsSeverity.Warn))
                    panel.Text($"  ▸ [{i.CheckName}] {i.Message}");
            }
            panel.AddSection("PASS");
            foreach (var i in items.Where(x => x.Severity == LpsSeverity.Pass))
                panel.Text($"  ✓ [{i.CheckName}] {i.Message}");

            // Action buttons — select failing elements
            var failingIds = items.Where(x => x.Severity == LpsSeverity.Fail)
                                  .SelectMany(x => x.ElementIds ?? new List<ElementId>())
                                  .Where(id => id != null && id != ElementId.InvalidElementId)
                                  .Distinct().ToList();
            if (failingIds.Count > 0)
            {
                panel.Action("Select failing elements", $"Select {failingIds.Count} flagged elements in Revit", _ =>
                {
                    try { app.ActiveUIDocument.Selection.SetElementIds(failingIds); }
                    catch (Exception ex) { StingLog.Warn($"SetElementIds: {ex.Message}"); }
                });
            }

            panel.Show();
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CMD-3 — Down Conductor Spacing Checker
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsDownConductorCheckerCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — Down Conductor Check", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            string classId = ParameterHelpers.GetString(doc.ProjectInformation, LpsParams.CLASS_TXT);
            if (string.IsNullOrWhiteSpace(classId))
            {
                TaskDialog.Show("STING — Down Conductor Check", "Run LPS Class Setup first.");
                return Result.Cancelled;
            }
            var def = LpsEngine.LoadClass(classId);
            if (def == null)
            {
                TaskDialog.Show("STING — Down Conductor Check", $"Unknown LPS class '{classId}'.");
                return Result.Cancelled;
            }

            var conductors = LpsEngine.CollectLpsFamily(doc, "Down Conductor", "Down_Conductor", "DownConductor");
            if (conductors.Count == 0)
            {
                TaskDialog.Show("STING — Down Conductor Check",
                    "No down conductor families found. Place families with 'Down Conductor' in the family or type name.");
                return Result.Succeeded;
            }

            StingProgressDialog progress = null;
            if (conductors.Count > 50) progress = StingProgressDialog.Show("Checking down conductor spacing", conductors.Count);

            int violations = 0;
            var failingIds = new List<ElementId>();
            try
            {
                using (var t = new Transaction(doc, "STING — Down Conductor Spacing"))
                {
                    t.Start();
                    foreach (var dc in conductors)
                    {
                        progress?.Increment();
                        var p = (dc.Location as LocationPoint)?.Point;
                        if (p == null) continue;
                        double minDist = double.MaxValue;
                        foreach (var other in conductors)
                        {
                            if (other.Id == dc.Id) continue;
                            var op = (other.Location as LocationPoint)?.Point;
                            if (op == null) continue;
                            double d = Math.Sqrt(Math.Pow(p.X - op.X, 2) + Math.Pow(p.Y - op.Y, 2));
                            if (d < minDist) minDist = d;
                        }
                        double minDistM = minDist == double.MaxValue ? 0
                            : UnitUtils.ConvertFromInternalUnits(minDist, UnitTypeId.Meters);
                        bool ok = minDistM <= def.DownConductorSpacingM && conductors.Count >= 2;
                        ParameterHelpers.SetString(dc, LpsParams.COMPLIANCE_STATUS_TXT,
                            ok ? "SPACING OK" : "SPACING FAIL", true);
                        if (!ok) { violations++; failingIds.Add(dc.Id); }
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("DownConductorCheck failed", ex);
                progress?.Close();
                TaskDialog.Show("STING — Down Conductor Check", "Check failed: " + ex.Message);
                return Result.Failed;
            }
            progress?.Close();

            // Compute minimum required count from approximate building perimeter.
            // Uses walls only — ProjectInformation doesn't expose extents directly.
            int minRequired = 2;
            try
            {
                var walls = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .Select(e => e.get_BoundingBox(null))
                    .Where(b => b != null)
                    .ToList();
                if (walls.Count > 0)
                {
                    double minX = walls.Min(b => b.Min.X);
                    double maxX = walls.Max(b => b.Max.X);
                    double minY = walls.Min(b => b.Min.Y);
                    double maxY = walls.Max(b => b.Max.Y);
                    double perimeterFt = 2.0 * ((maxX - minX) + (maxY - minY));
                    double perimeterM = UnitUtils.ConvertFromInternalUnits(perimeterFt, UnitTypeId.Meters);
                    minRequired = LpsEngine.ComputeMinDownConductors(classId, perimeterM);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Perimeter: {ex.Message}"); }

            var sb = new StringBuilder();
            sb.AppendLine($"Class {classId} spacing limit: {def.DownConductorSpacingM} m");
            sb.AppendLine($"Down conductors found: {conductors.Count}");
            sb.AppendLine($"Minimum required (perimeter / spacing): {minRequired}");
            sb.AppendLine($"Spacing violations: {violations}");
            if (conductors.Count < minRequired)
                sb.AppendLine($"\n⚠ Placed count {conductors.Count} < minimum required {minRequired}.");
            if (violations > 0 && app?.ActiveUIDocument != null)
            {
                var dlg = new TaskDialog("STING — Down Conductor Check") { MainContent = sb.ToString() };
                dlg.CommonButtons = TaskDialogCommonButtons.Ok;
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Select violations");
                if (dlg.Show() == TaskDialogResult.CommandLink1)
                {
                    try { app.ActiveUIDocument.Selection.SetElementIds(failingIds); }
                    catch (Exception ex) { StingLog.Warn($"SetElementIds: {ex.Message}"); }
                }
            }
            else
            {
                TaskDialog.Show("STING — Down Conductor Check", sb.ToString());
            }
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CMD-4 — Earth Resistance Validator
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsEarthResistanceValidatorCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — Earth Check", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            string classId = ParameterHelpers.GetString(doc.ProjectInformation, LpsParams.CLASS_TXT);
            var def = LpsEngine.LoadClass(string.IsNullOrWhiteSpace(classId) ? "II" : classId);
            double target = def?.EarthResistanceTargetOhm ?? 10.0;

            var electrodes = LpsEngine.CollectLpsFamily(doc, "Earth", "Ground Rod", "GroundRod", "Earth_Rod", "Earth Electrode");
            if (electrodes.Count == 0)
            {
                TaskDialog.Show("STING — Earth Check",
                    "No earth electrode families found. Place families with 'Earth' or 'Ground Rod' in the family name.");
                return Result.Succeeded;
            }

            int unread = 0, failed = 0, passed = 0;
            var failedIds = new List<ElementId>();
            try
            {
                using (var t = new Transaction(doc, "STING — Earth Resistance Validation"))
                {
                    t.Start();
                    foreach (var el in electrodes)
                    {
                        double r = LpsEngine.GetDoubleParam(el, LpsParams.EARTH_RESISTANCE_OHM);
                        if (r <= 0) { unread++; ParameterHelpers.SetString(el, LpsParams.COMPLIANCE_STATUS_TXT, "EARTH NOT TESTED", true); }
                        else if (r > target) { failed++; failedIds.Add(el.Id); ParameterHelpers.SetString(el, LpsParams.COMPLIANCE_STATUS_TXT, $"EARTH FAIL — {r:F1} ohm", true); }
                        else { passed++; ParameterHelpers.SetString(el, LpsParams.COMPLIANCE_STATUS_TXT, "EARTH OK", true); }
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("EarthCheck failed", ex);
                TaskDialog.Show("STING — Earth Check", "Validation failed: " + ex.Message);
                return Result.Failed;
            }

            // Soil resistivity advisory
            string rhoPrompt = "Soil resistivity ρ (ohm·m), default 100:";
            string rhoStr = PromptDialog(rhoPrompt, "100");
            string rhoLine = "";
            if (!string.IsNullOrWhiteSpace(rhoStr) && double.TryParse(rhoStr, out double rho) && rho > 0)
            {
                double L = 2.4;
                double rApprox = rho / (2.0 * Math.PI * L);
                rhoLine = $"\n\nFor ρ = {rho:F0} Ω·m, a {L:F1} m driven rod approximates R = {rApprox:F2} Ω " +
                          $"(R ≈ ρ / (2πL)).\n" +
                          (rApprox > target
                              ? $"Single rod likely insufficient for {target:F0} Ω target — consider rod array or earth mat."
                              : $"Single rod should achieve {target:F0} Ω target.");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Class {def?.Id ?? "II"} target: ≤ {target:F1} Ω");
            sb.AppendLine($"Electrodes found: {electrodes.Count}");
            sb.AppendLine($"  ✓ Within target: {passed}");
            sb.AppendLine($"  ⚠ No reading (test required): {unread}");
            sb.AppendLine($"  ✗ Above target: {failed}");
            sb.Append(rhoLine);
            TaskDialog.Show("STING — Earth Check", sb.ToString());
            return Result.Succeeded;
        }

        internal static string PromptDialog(string prompt, string defaultText)
        {
            try
            {
                var win = new Window
                {
                    Title = "STING",
                    Width = 420, Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize
                };
                var stack = new StackPanel { Margin = new Thickness(12) };
                stack.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
                var box = new TextBox { Text = defaultText ?? string.Empty, Margin = new Thickness(0, 0, 0, 12) };
                stack.Children.Add(box);
                var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(4, 0, 0, 0) };
                var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(4, 0, 0, 0) };
                bool accepted = false;
                ok.Click += (_, __) => { accepted = true; win.Close(); };
                cancel.Click += (_, __) => { win.Close(); };
                btnRow.Children.Add(cancel); btnRow.Children.Add(ok);
                stack.Children.Add(btnRow);
                win.Content = stack;
                StingWindowHelper.ApplyOwner(win);
                win.ShowDialog();
                return accepted ? box.Text : null;
            }
            catch (Exception ex) { StingLog.Warn($"PromptDialog: {ex.Message}"); return null; }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CMD-5 — Equipotential Bonding Inventory
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsBondingInventoryCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — Bonding Inventory", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            // Build room → LPZ map
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(r => (r as Room)?.Area > 0).Cast<Room>().ToList();
            var roomLpz = new Dictionary<ElementId, string>();
            foreach (var r in rooms)
                roomLpz[r.Id] = ParameterHelpers.GetString(r, LpsParams.ZONE_TXT) ?? "";

            // Collect candidate elements that could cross zone boundaries
            BuiltInCategory[] cats = {
                BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_Conduit, BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming
            };
            var candidates = new List<Element>();
            foreach (var bic in cats)
            {
                try
                {
                    candidates.AddRange(new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType());
                }
                catch (Exception ex) { StingLog.Warn($"Collect {bic}: {ex.Message}"); }
            }

            StingProgressDialog progress = candidates.Count > 50
                ? StingProgressDialog.Show("Bonding inventory", candidates.Count) : null;

            var rows = new List<string[]>();
            int needsReview = 0, alreadySet = 0;

            try
            {
                using (var t = new Transaction(doc, "STING — Bonding Inventory"))
                {
                    t.Start();
                    foreach (var el in candidates)
                    {
                        progress?.Increment();
                        var fromRoom = ResolveRoom(doc, el, useStart: true);
                        var toRoom = ResolveRoom(doc, el, useStart: false);
                        string fromLpz = fromRoom != null && roomLpz.TryGetValue(fromRoom.Id, out var fz) ? fz : "";
                        string toLpz   = toRoom   != null && roomLpz.TryGetValue(toRoom.Id,   out var tz) ? tz : "";
                        if (string.IsNullOrEmpty(fromLpz) || string.IsNullOrEmpty(toLpz)) continue;
                        if (string.Equals(fromLpz, toLpz, StringComparison.OrdinalIgnoreCase)) continue;

                        string existing = ParameterHelpers.GetString(el, LpsParams.BOND_TYPE_TXT);
                        bool hasBond = !string.IsNullOrWhiteSpace(existing) &&
                            (existing.Equals("DIRECT", StringComparison.OrdinalIgnoreCase) ||
                             existing.Equals("SPD", StringComparison.OrdinalIgnoreCase) ||
                             existing.Equals("ISOLATING_SPARK_GAP", StringComparison.OrdinalIgnoreCase));
                        if (!hasBond)
                        {
                            ParameterHelpers.SetString(el, LpsParams.BOND_TYPE_TXT, "REVIEW REQUIRED", true);
                            needsReview++;
                        }
                        else alreadySet++;

                        string remarks = hasBond ? "OK" : "Bond type assignment required";
                        rows.Add(new[]
                        {
                            el.Id.Value.ToString(),
                            el.Category?.Name ?? "",
                            (el as FamilyInstance)?.Symbol?.FamilyName ?? "",
                            doc.GetElement(el.LevelId)?.Name ?? "",
                            fromRoom?.Name ?? "", fromLpz,
                            toRoom?.Name ?? "", toLpz,
                            hasBond ? existing : "REVIEW REQUIRED",
                            remarks
                        });
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("BondingInventory failed", ex);
                progress?.Close();
                TaskDialog.Show("STING — Bonding Inventory", "Inventory failed: " + ex.Message);
                return Result.Failed;
            }
            progress?.Close();

            // Export CSV
            string outPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_LPS_Bonding_Inventory", ".csv");
            try
            {
                var csv = new StringBuilder();
                csv.AppendLine("ElementId,Category,FamilyName,Level,FromRoom,FromLPZ,ToRoom,ToLPZ,BondType,Remarks");
                foreach (var r in rows)
                    csv.AppendLine(string.Join(",", r.Select(CsvEscape)));
                File.WriteAllText(outPath, csv.ToString());
            }
            catch (Exception ex) { StingLog.Warn($"CSV write: {ex.Message}"); outPath = "(write failed)"; }

            TaskDialog.Show("STING — Bonding Inventory",
                $"Boundary-crossing elements: {rows.Count}\n" +
                $"  Already bonded: {alreadySet}\n" +
                $"  Flagged for review: {needsReview}\n\n" +
                $"CSV: {outPath}");
            return Result.Succeeded;
        }

        private static Room ResolveRoom(Document doc, Element el, bool useStart)
        {
            try
            {
                XYZ pt = null;
                if (el.Location is LocationPoint lp) pt = lp.Point;
                else if (el.Location is LocationCurve lc)
                {
                    pt = useStart ? lc.Curve?.GetEndPoint(0) : lc.Curve?.GetEndPoint(1);
                }
                if (pt == null) return null;
                return doc.GetRoomAtPoint(pt);
            }
            catch (Exception ex) { StingLog.Warn($"ResolveRoom: {ex.Message}"); return null; }
        }

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CMD-6 — Room LPZ Tag
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsRoomZoneTagCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — Zone Tag", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0)
                .OrderBy(r => doc.GetElement(r.LevelId)?.Name ?? "")
                .ThenBy(r => r.Number ?? "")
                .ToList();
            if (rooms.Count == 0)
            {
                TaskDialog.Show("STING — Zone Tag", "No placed rooms found.");
                return Result.Cancelled;
            }

            // Pick the LPZ first
            var zoneOptions = new List<string> { "LPZ0A", "LPZ0B", "LPZ1", "LPZ2", "LPZ3" };
            string zone = StingListPicker.Show(
                "STING — Pick LPZ Zone",
                "Lightning Protection Zone (BS EN 62305-4)",
                zoneOptions);
            if (string.IsNullOrWhiteSpace(zone)) return Result.Cancelled;

            // Multi-select rooms
            var items = rooms.Select(r => new StingListPicker.ListItem
            {
                Label = $"{doc.GetElement(r.LevelId)?.Name ?? "—"}  •  {r.Number} {r.Name}",
                Detail = "Current LPZ: " + (ParameterHelpers.GetString(r, LpsParams.ZONE_TXT) ?? "(none)"),
                Tag = r.Id
            }).ToList();
            var picked = StingListPicker.Show($"STING — Apply {zone} to rooms",
                "Select rooms to receive this zone (Ctrl-click for multi-select)", items, true);
            if (picked == null || picked.Count == 0) return Result.Cancelled;

            int taggedRooms = 0, taggedElements = 0;
            try
            {
                using (var t = new Transaction(doc, "STING — Apply LPZ Zone"))
                {
                    t.Start();
                    foreach (var p in picked)
                    {
                        if (!(p.Tag is ElementId rid)) continue;
                        var room = doc.GetElement(rid) as Room;
                        if (room == null) continue;
                        ParameterHelpers.SetString(room, LpsParams.ZONE_TXT, zone, true);
                        taggedRooms++;

                        // Stamp every element with a LocationPoint inside this room
                        try
                        {
                            var allFi = new FilteredElementCollector(doc)
                                .WhereElementIsNotElementType()
                                .OfClass(typeof(FamilyInstance))
                                .Cast<FamilyInstance>();
                            foreach (var fi in allFi)
                            {
                                var pt = (fi.Location as LocationPoint)?.Point;
                                if (pt == null) continue;
                                var hostRoom = doc.GetRoomAtPoint(pt);
                                if (hostRoom != null && hostRoom.Id == room.Id)
                                {
                                    if (ParameterHelpers.SetString(fi, LpsParams.ZONE_TXT, zone, true))
                                        taggedElements++;
                                }
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Stamp room elements: {ex.Message}"); }
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("ZoneTag failed", ex);
                TaskDialog.Show("STING — Zone Tag", "Failed: " + ex.Message);
                return Result.Failed;
            }

            TaskDialog.Show("STING — Zone Tag",
                $"Zone {zone} applied to {taggedRooms} room(s) and {taggedElements} contained element(s).");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CMD-7 — Plan-View Visualiser (rolling sphere projection)
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsPlanViewVisualizerCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — Plan Visualise", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            string projClass = ParameterHelpers.GetString(doc.ProjectInformation, LpsParams.CLASS_TXT);
            double projRadius = LpsEngine.GetDoubleParam(doc.ProjectInformation, LpsParams.ROLLING_SPHERE_RADIUS_M);
            if (projRadius <= 0)
            {
                var def = LpsEngine.LoadClass(string.IsNullOrWhiteSpace(projClass) ? "II" : projClass);
                projRadius = def?.RollingSphereRadiusM ?? 30;
            }

            var airTerminals = LpsEngine.CollectLpsFamily(doc, "Air Terminal", "Air_Terminal", "Franklin", "Air-Terminal");
            if (airTerminals.Count == 0)
            {
                TaskDialog.Show("STING — Plan Visualise",
                    "No air terminal families found. Place families with 'Air Terminal' or 'Franklin' in the family name.");
                return Result.Cancelled;
            }

            try
            {
                using (var t = new Transaction(doc, "STING — LPS Plan Visualise"))
                {
                    t.Start();

                    // Get or create the drafting view
                    const string viewName = "STING - LPS Protection Zones";
                    var draftingView = new FilteredElementCollector(doc).OfClass(typeof(ViewDrafting))
                        .Cast<ViewDrafting>().FirstOrDefault(v => string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));
                    if (draftingView == null)
                    {
                        var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>().FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                        if (vft == null) throw new InvalidOperationException("No Drafting view family type available.");
                        draftingView = ViewDrafting.Create(doc, vft.Id);
                        try { draftingView.Name = viewName; } catch (Exception ex) { StingLog.Warn($"Rename: {ex.Message}"); }
                    }

                    // Resolve filled region type
                    var frType = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType))
                        .Cast<FilledRegionType>().FirstOrDefault();
                    if (frType == null) throw new InvalidOperationException("No FilledRegionType available.");

                    // Resolve text note type
                    var textType = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType))
                        .Cast<TextNoteType>().FirstOrDefault();

                    // Clear previous filled regions in this view
                    var existingFr = new FilteredElementCollector(doc, draftingView.Id)
                        .OfClass(typeof(FilledRegion)).ToElementIds().ToList();
                    var existingTxt = new FilteredElementCollector(doc, draftingView.Id)
                        .OfClass(typeof(TextNote)).ToElementIds().ToList();
                    if (existingFr.Count > 0) doc.Delete(existingFr);
                    if (existingTxt.Count > 0) doc.Delete(existingTxt);

                    int placed = 0;
                    foreach (var at in airTerminals)
                    {
                        var p = (at.Location as LocationPoint)?.Point;
                        if (p == null) continue;
                        // Per-element radius wins over project default
                        double rM = LpsEngine.GetDoubleParam(at, LpsParams.ROLLING_SPHERE_RADIUS_M);
                        if (rM <= 0) rM = projRadius;
                        double rFt = UnitUtils.ConvertToInternalUnits(rM, UnitTypeId.Meters);

                        // Build a circle in the plane Z = p.Z using a polygonal approximation
                        const int segments = 36;
                        var pts = new List<XYZ>();
                        for (int i = 0; i < segments; i++)
                        {
                            double ang = 2.0 * Math.PI * i / segments;
                            pts.Add(new XYZ(p.X + rFt * Math.Cos(ang), p.Y + rFt * Math.Sin(ang), 0));
                        }
                        pts.Add(pts[0]);
                        var lines = new List<Curve>();
                        for (int i = 0; i < pts.Count - 1; i++)
                            lines.Add(Line.CreateBound(pts[i], pts[i + 1]));
                        var loop = CurveLoop.Create(lines);
                        try
                        {
                            FilledRegion.Create(doc, frType.Id, draftingView.Id, new List<CurveLoop> { loop });
                            placed++;
                        }
                        catch (Exception ex) { StingLog.Warn($"FilledRegion: {ex.Message}"); }

                        // Text note at centre
                        try
                        {
                            string seq = ParameterHelpers.GetString(at, ParamRegistry.SEQ);
                            string label = $"AT-{(string.IsNullOrEmpty(seq) ? "?" : seq)} R={rM:F0}m Class {projClass ?? "?"}";
                            if (textType != null)
                                TextNote.Create(doc, draftingView.Id, new XYZ(p.X, p.Y, 0), label, textType.Id);
                        }
                        catch (Exception ex) { StingLog.Warn($"TextNote: {ex.Message}"); }
                    }

                    t.Commit();

                    try
                    {
                        var uidoc = app?.ActiveUIDocument;
                        if (uidoc != null) uidoc.ActiveView = draftingView;
                    }
                    catch (Exception ex) { StingLog.Warn($"ActiveView set: {ex.Message}"); }

                    TaskDialog.Show("STING — Plan Visualise",
                        $"Protection zone plan created: '{viewName}'\n" +
                        $"Air terminals projected: {placed}\n\n" +
                        "Each circle is the rolling-sphere protection radius at terminal-tip height. " +
                        "For 3D coverage including pitched roofs, run the '3D Coverage' command — it " +
                        "samples the actual roof geometry and flags exposed points per BS EN 62305-3 §E.4.");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PlanVisualise failed", ex);
                TaskDialog.Show("STING — Plan Visualise", "Failed: " + ex.Message);
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CMD-8 — Separation Distance Checker
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsSeparationDistanceCheckerCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — Sep Distance", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            string classId = ParameterHelpers.GetString(doc.ProjectInformation, LpsParams.CLASS_TXT);
            if (string.IsNullOrWhiteSpace(classId))
            {
                TaskDialog.Show("STING — Sep Distance", "Run LPS Class Setup first.");
                return Result.Cancelled;
            }
            var conductors = LpsEngine.CollectLpsFamily(doc, "Down Conductor", "Down_Conductor", "DownConductor");
            if (conductors.Count == 0)
            {
                TaskDialog.Show("STING — Sep Distance", "No down conductors found.");
                return Result.Cancelled;
            }

            // Collect candidate MEP elements once
            BuiltInCategory[] cats = {
                BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_Conduit, BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_ElectricalEquipment
            };
            var mep = new List<Element>();
            foreach (var bic in cats)
            {
                try { mep.AddRange(new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType()); }
                catch (Exception ex) { StingLog.Warn($"Collect {bic}: {ex.Message}"); }
            }

            var rows = new List<string[]>();
            int violations = 0;
            var conflictingMepIds = new HashSet<ElementId>();
            var mepIds = mep.Select(m => m.Id).ToList();
            try
            {
                using (var t = new Transaction(doc, "STING — Separation Distance Check"))
                {
                    t.Start();
                    foreach (var dc in conductors)
                    {
                        double sMm = LpsEngine.GetDoubleParam(dc, LpsParams.SEPARATION_DISTANCE_MM);
                        if (sMm <= 0)
                        {
                            // Compute on the fly from class + length + material
                            double L = ConductorLengthM(dc);
                            string mat = ParameterHelpers.GetString(dc, LpsParams.CONDUCTOR_MATERIAL_TXT);
                            if (string.IsNullOrWhiteSpace(mat)) mat = "COPPER";
                            sMm = LpsEngine.ComputeSeparationDistance(classId, L, mat);
                            try
                            {
                                var p = dc.LookupParameter(LpsParams.SEPARATION_DISTANCE_MM);
                                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                                    p.Set(sMm);
                            }
                            catch (Exception ex) { StingLog.Warn($"Stamp s: {ex.Message}"); }
                        }
                        var bb = dc.get_BoundingBox(null);
                        if (bb == null) continue;
                        double inflateFt = UnitUtils.ConvertToInternalUnits(sMm / 1000.0, UnitTypeId.Meters);
                        var inflated = new Outline(
                            new XYZ(bb.Min.X - inflateFt, bb.Min.Y - inflateFt, bb.Min.Z - inflateFt),
                            new XYZ(bb.Max.X + inflateFt, bb.Max.Y + inflateFt, bb.Max.Z + inflateFt));
                        var bbFilter = new BoundingBoxIntersectsFilter(inflated);
                        var hits = mepIds.Count > 0
                            ? (IList<Element>)new FilteredElementCollector(doc, mepIds).WherePasses(bbFilter).ToElements()
                            : new List<Element>();
                        bool conflict = false;
                        foreach (var h in hits)
                        {
                            if (h.Id == dc.Id) continue;
                            // Skip other LPS-flagged families
                            string fn = (h as FamilyInstance)?.Symbol?.FamilyName ?? "";
                            if (fn.IndexOf("LPS", StringComparison.OrdinalIgnoreCase) >= 0
                             || fn.IndexOf("Lightning", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            conflict = true;
                            conflictingMepIds.Add(h.Id);
                            rows.Add(new[]
                            {
                                dc.Id.Value.ToString(),
                                (dc as FamilyInstance)?.Symbol?.FamilyName ?? "",
                                h.Id.Value.ToString(),
                                h.Category?.Name ?? "",
                                fn,
                                $"{sMm:F0}"
                            });
                        }
                        ParameterHelpers.SetString(dc, LpsParams.COMPLIANCE_STATUS_TXT,
                            conflict ? "SEP DIST FAIL" : "SEP DIST OK", true);
                        if (conflict) violations++;
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("SepDistCheck failed", ex);
                TaskDialog.Show("STING — Sep Distance", "Check failed: " + ex.Message);
                return Result.Failed;
            }

            var panel = StingResultPanel.Create("Separation Distance Check (BS EN 62305-3 §6.3)");
            panel.SetSubtitle($"Class {classId} — {conductors.Count} down conductor(s) checked");
            panel.AddSection("SUMMARY")
                 .Metric("Down conductors", conductors.Count.ToString())
                 .MetricError("Violations", violations.ToString());
            if (rows.Count > 0)
            {
                panel.AddSection("VIOLATIONS")
                     .Table(new[] { "Cond.Id", "Cond. Family", "MEP Id", "MEP Cat", "MEP Family", "s (mm)" },
                            rows.Take(200).ToList());
                if (rows.Count > 200) panel.Text($"(+{rows.Count - 200} more — see CSV / select)");
                panel.Action("Select conflicting MEP elements", $"Select {conflictingMepIds.Count} elements", _ =>
                {
                    try { app.ActiveUIDocument.Selection.SetElementIds(conflictingMepIds.ToList()); }
                    catch (Exception ex) { StingLog.Warn($"Select: {ex.Message}"); }
                });
            }
            panel.Show();
            return Result.Succeeded;
        }

        private static double ConductorLengthM(FamilyInstance fi)
        {
            try
            {
                var bb = fi.get_BoundingBox(null);
                if (bb == null) return 3.0;
                double zFt = bb.Max.Z - bb.Min.Z;
                double zM = UnitUtils.ConvertFromInternalUnits(zFt, UnitTypeId.Meters);
                return zM > 0.1 ? zM : 3.0;
            }
            catch (Exception ex) { StingLog.Warn($"ConductorLengthM: {ex.Message}"); return 3.0; }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CMD-9 — Inspection Scheduler
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsInspectionSchedulerCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — Inspection Schedule", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var lps = new List<FamilyInstance>();
            lps.AddRange(LpsEngine.CollectLpsFamily(doc, "Air Terminal", "Air_Terminal", "Franklin"));
            lps.AddRange(LpsEngine.CollectLpsFamily(doc, "Down Conductor", "Down_Conductor", "DownConductor"));
            lps.AddRange(LpsEngine.CollectLpsFamily(doc, "Earth", "Ground Rod", "GroundRod", "Earth Electrode"));
            lps.AddRange(LpsEngine.CollectLpsFamily(doc, "Bonding", "Bond Bar", "BondingBar"));
            if (lps.Count == 0)
            {
                TaskDialog.Show("STING — Inspection Schedule", "No LPS components found.");
                return Result.Cancelled;
            }

            string classId = ParameterHelpers.GetString(doc.ProjectInformation, LpsParams.CLASS_TXT);
            int defaultInterval = LpsEngine.LoadClass(string.IsNullOrWhiteSpace(classId) ? "II" : classId)?.InspectionIntervalMonths ?? 12;

            DateTime today = DateTime.Today;
            var rowsOverdue = new List<string[]>();
            var rowsUpcoming = new List<string[]>();
            var rowsOk = new List<string[]>();
            var rows = new List<string[]>();

            try
            {
                using (var t = new Transaction(doc, "STING — Stamp Next Inspection"))
                {
                    t.Start();
                    foreach (var el in lps)
                    {
                        int interval = (int)Math.Round(LpsEngine.GetDoubleParam(el, LpsParams.INSPECTION_INTERVAL_MONTHS));
                        if (interval <= 0) interval = defaultInterval;
                        string testStr = ParameterHelpers.GetString(el, LpsParams.TEST_DATE_TXT);
                        DateTime nextDue;
                        DateTime? lastTest = null;
                        if (DateTime.TryParse(testStr, out var dt)) { lastTest = dt; nextDue = dt.AddMonths(interval); }
                        else nextDue = today;

                        // Stamp project comments with NEXT_INSPECTION marker
                        try
                        {
                            var commentsPar = el.LookupParameter("Comments")
                                ?? el.LookupParameter("PRJ_COMMENTS_TXT");
                            if (commentsPar != null && !commentsPar.IsReadOnly && commentsPar.StorageType == StorageType.String)
                            {
                                string existing = commentsPar.AsString() ?? "";
                                string marker = $"NEXT_INSPECTION: {nextDue:yyyy-MM-dd}";
                                string cleaned = string.Join(" | ", existing.Split('|')
                                    .Select(s => s.Trim())
                                    .Where(s => !s.StartsWith("NEXT_INSPECTION:")));
                                string newVal = string.IsNullOrEmpty(cleaned) ? marker : $"{cleaned} | {marker}";
                                commentsPar.Set(newVal);
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Stamp comments: {ex.Message}"); }

                        string family = el.Symbol?.FamilyName ?? "";
                        string level = doc.GetElement(el.LevelId)?.Name ?? "";
                        var pt = (el.Location as LocationPoint)?.Point;
                        var room = pt != null ? doc.GetRoomAtPoint(pt) : null;
                        string roomName = room?.Name ?? "";
                        string cert = ParameterHelpers.GetString(el, LpsParams.CERT_REF_TXT);

                        var row = new[]
                        {
                            el.Id.Value.ToString(), family, level, roomName,
                            lastTest?.ToString("yyyy-MM-dd") ?? "", interval.ToString(),
                            nextDue.ToString("yyyy-MM-dd"), cert,
                            nextDue < today ? "OVERDUE" : (nextDue - today).TotalDays <= 90 ? "UPCOMING" : "OK"
                        };
                        rows.Add(row);
                        if (nextDue < today) rowsOverdue.Add(row);
                        else if ((nextDue - today).TotalDays <= 90) rowsUpcoming.Add(row);
                        else rowsOk.Add(row);
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("InspectionScheduler failed", ex);
                TaskDialog.Show("STING — Inspection Schedule", "Failed: " + ex.Message);
                return Result.Failed;
            }

            // Export CSV
            string outPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_LPS_Inspection_Schedule", ".csv");
            try
            {
                var csv = new StringBuilder();
                csv.AppendLine("ElementId,Family,Level,Room,LastTestDate,IntervalMonths,NextDueDate,CertRef,Status");
                foreach (var r in rows)
                    csv.AppendLine(string.Join(",", r.Select(LpsBondingInventoryCommand_CsvAccess.Escape)));
                File.WriteAllText(outPath, csv.ToString());
            }
            catch (Exception ex) { StingLog.Warn($"CSV: {ex.Message}"); outPath = "(write failed)"; }

            var panel = StingResultPanel.Create("LPS Inspection Schedule");
            panel.SetSubtitle($"Class {classId ?? "?"} default interval {defaultInterval} months — CSV: {outPath}");
            panel.AddSection("SUMMARY")
                 .Metric("Total components", lps.Count.ToString())
                 .MetricError("Overdue", rowsOverdue.Count.ToString())
                 .MetricWarn("Due ≤ 90 days", rowsUpcoming.Count.ToString())
                 .MetricHighlight("Up to date", rowsOk.Count.ToString());

            string[] hdr = { "Id", "Family", "Level", "Room", "Last Test", "Interval", "Next Due", "Cert", "Status" };
            if (rowsOverdue.Count > 0) panel.AddSection("OVERDUE").Table(hdr, rowsOverdue.Take(50).ToList());
            if (rowsUpcoming.Count > 0) panel.AddSection("DUE ≤ 90 DAYS").Table(hdr, rowsUpcoming.Take(50).ToList());
            panel.Show();
            return Result.Succeeded;
        }
    }

    // Tiny shim so CMD-9 can reuse CMD-5's CSV escape without an awkward
    // public method on a command class.
    internal static class LpsBondingInventoryCommand_CsvAccess
    {
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CMD-10 — Full Compliance Report
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LpsFullReportCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            return RunInternal(ctx.App, ctx.Doc);
        }

        public Result Execute(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("STING — LPS Report", "No active document."); return Result.Cancelled; }
            return RunInternal(app, doc);
        }

        private Result RunInternal(UIApplication app, Document doc)
        {
            var prj = doc.ProjectInformation;
            string classId = ParameterHelpers.GetString(prj, LpsParams.CLASS_TXT);
            if (string.IsNullOrWhiteSpace(classId))
            {
                TaskDialog.Show("STING — LPS Report", "Run LPS Class Setup first.");
                return Result.Cancelled;
            }
            var def = LpsEngine.LoadClass(classId);
            var items = LpsEngine.ValidateModel(doc);
            int pass = items.Count(i => i.Severity == LpsSeverity.Pass);
            int warn = items.Count(i => i.Severity == LpsSeverity.Warn);
            int fail = items.Count(i => i.Severity == LpsSeverity.Fail);
            string verdict = fail > 0 ? $"FAIL — {fail} items" : warn > 0 ? "WARN" : "PASS";

            var ats = LpsEngine.CollectLpsFamily(doc, "Air Terminal", "Franklin");
            var dcs = LpsEngine.CollectLpsFamily(doc, "Down Conductor", "Down_Conductor", "DownConductor");
            var ees = LpsEngine.CollectLpsFamily(doc, "Earth", "Ground Rod", "GroundRod", "Earth Electrode");
            double earthSum = ees.Sum(e => LpsEngine.GetDoubleParam(e, LpsParams.EARTH_RESISTANCE_OHM));
            double earthAvg = ees.Count > 0 ? earthSum / ees.Count : 0;

            // Build the token + loop dict shared by docx and CSV paths.
            var tokens = BuildTokenDict(doc, prj, classId, def, items, pass, warn, fail, verdict,
                                       ats, dcs, ees, earthAvg);
            var complianceRows = BuildComplianceRows(items);
            var conductorRows = BuildConductorRows(doc, dcs);
            var electrodeRows = BuildElectrodeRows(doc, ees);

            string docxOut = TryRenderDocx(doc, tokens, complianceRows, conductorRows, electrodeRows,
                                           out string docxError);
            string csvOut = WriteCsvFallback(doc, tokens, complianceRows, conductorRows, electrodeRows);

            var summary = new StringBuilder();
            if (!string.IsNullOrEmpty(docxOut))
                summary.AppendLine($"DOCX report:\n{docxOut}");
            else
                summary.AppendLine($"DOCX render skipped ({docxError}).");
            summary.AppendLine();
            summary.AppendLine($"CSV (audit trail):\n{csvOut ?? "(write failed)"}");
            StingLog.Info($"LPS compliance report — docx={docxOut ?? "skipped"} csv={csvOut ?? "fail"}");
            TaskDialog.Show("STING — LPS Report",
                "LPS Compliance Report (BS EN 62305) generated.\n\n" + summary.ToString());
            return Result.Succeeded;
        }

        private static string Esc(string s) => LpsBondingInventoryCommand_CsvAccess.Escape(s ?? "");


        // ── Token + row builders ─────────────────────────────────────

        private static Dictionary<string, object> BuildTokenDict(
            Document doc, ProjectInfo prj, string classId, LpsClassDef def,
            IReadOnlyList<LpsComplianceItem> items, int pass, int warn, int fail, string verdict,
            List<FamilyInstance> ats, List<FamilyInstance> dcs, List<FamilyInstance> ees, double earthAvg)
        {
            string regionDefault = "ug";
            double ng = LpsEngine.GetEffectiveFlashDensity(doc, regionDefault);
            double sphereR = def?.RollingSphereRadiusM ?? 0;
            double meshM = def?.MeshSizeM ?? 0;
            double spaceM = def?.DownConductorSpacingM ?? 0;
            double earthTarget = def?.EarthResistanceTargetOhm ?? 10;
            int interval = def?.InspectionIntervalMonths ?? 12;
            double maxSepMm = dcs.Count > 0 ? dcs.Max(d => LpsEngine.GetDoubleParam(d, LpsParams.SEPARATION_DISTANCE_MM)) : 0;
            double crossMm2 = LpsEngine.GetDoubleParam(prj, LpsParams.CONDUCTOR_CROSS_SECT_MM2);
            double angleDeg = LpsEngine.GetDoubleParam(prj, LpsParams.PROTECTION_ANGLE_DEG);

            return new Dictionary<string, object>
            {
                ["project_name"]               = prj.Name ?? doc.Title ?? "",
                ["project_code"]               = prj.Number ?? "",
                ["building_name"]              = prj.BuildingName ?? "",
                ["client_name"]                = ParameterHelpers.GetString(prj, "Client Name") ?? "",
                ["report_date"]                = DateTime.Today.ToString("yyyy-MM-dd"),
                ["lps_class"]                  = classId,
                ["rolling_sphere_m"]           = sphereR.ToString("F0"),
                ["mesh_size_m"]                = meshM.ToString("F0"),
                ["protection_angle_deg"]       = angleDeg.ToString("F0"),
                ["down_conductor_spacing_m"]   = spaceM.ToString("F0"),
                ["earth_resistance_target_ohm"] = earthTarget.ToString("F1"),
                ["conductor_cross_sect_mm2"]   = crossMm2 > 0 ? crossMm2.ToString("F0") : "—",
                ["conductor_material"]         = ParameterHelpers.GetString(prj, LpsParams.CONDUCTOR_MATERIAL_TXT) ?? "—",
                ["surge_protection_lvl"]       = ParameterHelpers.GetString(prj, LpsParams.SURGE_PROTECTION_LVL_TXT) ?? "—",
                ["separation_distance_mm"]     = maxSepMm > 0 ? maxSepMm.ToString("F0") : "—",
                ["inspection_interval"]        = interval.ToString(),
                ["risk_assessment_ref"]        = ParameterHelpers.GetString(prj, LpsParams.RISK_ASSESSMENT_TXT) ?? "—",
                ["ng_value"]                   = ng.ToString("F2"),
                ["annual_strikes"]             = "—",
                ["collection_area_m2"]         = "—",
                ["air_terminal_count"]         = ats.Count.ToString(),
                ["down_conductor_count"]       = dcs.Count.ToString(),
                ["earth_electrode_count"]      = ees.Count.ToString(),
                ["earth_resistance_avg_ohm"]   = earthAvg.ToString("F2"),
                ["compliance_status"]          = verdict,
                ["compliance_pass"]            = pass.ToString(),
                ["compliance_warn"]            = warn.ToString(),
                ["compliance_fail"]            = fail.ToString(),
                ["test_date"]                  = ParameterHelpers.GetString(prj, LpsParams.TEST_DATE_TXT) ?? "—",
                ["cert_ref"]                   = ParameterHelpers.GetString(prj, LpsParams.CERT_REF_TXT) ?? "—",
            };
        }

        private static List<Dictionary<string, object>> BuildComplianceRows(IReadOnlyList<LpsComplianceItem> items)
        {
            var rows = new List<Dictionary<string, object>>();
            foreach (var it in items)
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["CheckName"] = it.CheckName ?? "",
                    ["Severity"]  = it.Severity.ToString(),
                    ["Message"]   = it.Message ?? "",
                });
            }
            return rows;
        }

        private static List<Dictionary<string, object>> BuildConductorRows(Document doc, List<FamilyInstance> dcs)
        {
            var rows = new List<Dictionary<string, object>>();
            foreach (var dc in dcs)
            {
                double zFt = (dc.get_BoundingBox(null)?.Max.Z ?? 0) - (dc.get_BoundingBox(null)?.Min.Z ?? 0);
                double zM = UnitUtils.ConvertFromInternalUnits(zFt, UnitTypeId.Meters);
                rows.Add(new Dictionary<string, object>
                {
                    ["Id"]           = dc.Id.Value.ToString(),
                    ["Family"]       = dc.Symbol?.FamilyName ?? "",
                    ["Level"]        = doc.GetElement(dc.LevelId)?.Name ?? "",
                    ["LengthM"]      = zM.ToString("F1"),
                    ["Material"]     = ParameterHelpers.GetString(dc, LpsParams.CONDUCTOR_MATERIAL_TXT) ?? "—",
                    ["CrossSectMm2"] = LpsEngine.GetDoubleParam(dc, LpsParams.CONDUCTOR_CROSS_SECT_MM2).ToString("F0"),
                    ["SepDistMm"]    = LpsEngine.GetDoubleParam(dc, LpsParams.SEPARATION_DISTANCE_MM).ToString("F0"),
                    ["Status"]       = ParameterHelpers.GetString(dc, LpsParams.COMPLIANCE_STATUS_TXT) ?? "",
                });
            }
            return rows;
        }

        private static List<Dictionary<string, object>> BuildElectrodeRows(Document doc, List<FamilyInstance> ees)
        {
            var rows = new List<Dictionary<string, object>>();
            foreach (var el in ees)
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["Id"]            = el.Id.Value.ToString(),
                    ["Family"]        = el.Symbol?.FamilyName ?? "",
                    ["Level"]         = doc.GetElement(el.LevelId)?.Name ?? "",
                    ["EarthType"]     = ParameterHelpers.GetString(el, LpsParams.EARTH_TYPE_TXT) ?? "",
                    ["ResistanceOhm"] = LpsEngine.GetDoubleParam(el, LpsParams.EARTH_RESISTANCE_OHM).ToString("F2"),
                    ["TestDate"]      = ParameterHelpers.GetString(el, LpsParams.TEST_DATE_TXT) ?? "",
                    ["CertRef"]       = ParameterHelpers.GetString(el, LpsParams.CERT_REF_TXT) ?? "",
                    ["Status"]        = ParameterHelpers.GetString(el, LpsParams.COMPLIANCE_STATUS_TXT) ?? "",
                });
            }
            return rows;
        }

        private static string TryRenderDocx(
            Document doc,
            Dictionary<string, object> tokens,
            List<Dictionary<string, object>> complianceRows,
            List<Dictionary<string, object>> conductorRows,
            List<Dictionary<string, object>> electrodeRows,
            out string error)
        {
            error = "";
            string templatePath = StingToolsApp.FindDataFile("lps_compliance_report.docx");
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                // Fall back to extracting the embedded resource to a temp path.
                try
                {
                    var asm = typeof(LpsFullReportCommand).Assembly;
                    string resourceName = asm.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith("lps_compliance_report.docx", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(resourceName))
                    {
                        string tmp = Path.Combine(Path.GetTempPath(), "STING_lps_compliance_report.docx");
                        using (var s = asm.GetManifestResourceStream(resourceName))
                        using (var fs = File.Create(tmp))
                        { s.CopyTo(fs); }
                        templatePath = tmp;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Embedded template extract: {ex.Message}"); }
            }
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                error = "template file lps_compliance_report.docx not found";
                return null;
            }

            // MiniWord auto-binds list values to table rows by matching {{field}}
            // tokens in a row against keys in any IEnumerable<Dictionary<string,object>>
            // value in the dict.
            var dict = new Dictionary<string, object>(tokens)
            {
                ["compliance_items"] = complianceRows,
                ["down_conductors"]  = conductorRows,
                ["earth_electrodes"] = electrodeRows,
            };

            string outPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_LPS_Compliance_Report", ".docx");
            try
            {
                MiniWord.SaveAsByTemplate(outPath, templatePath, dict);
                return outPath;
            }
            catch (Exception ex)
            {
                StingLog.Error("MiniWord render failed", ex);
                error = "render failed: " + ex.Message;
                return null;
            }
        }

        private static string WriteCsvFallback(
            Document doc,
            Dictionary<string, object> tokens,
            List<Dictionary<string, object>> complianceRows,
            List<Dictionary<string, object>> conductorRows,
            List<Dictionary<string, object>> electrodeRows)
        {
            string outPath = OutputLocationHelper.GetTimestampedPath(doc, "STING_LPS_Compliance_Report", ".csv");
            try
            {
                var csv = new StringBuilder();
                csv.AppendLine("STING Lightning Protection Compliance Report");
                csv.AppendLine($"Project,{Esc(tokens["project_name"]?.ToString())}");
                csv.AppendLine($"Generated,{tokens["report_date"]}");
                csv.AppendLine("Standard,BS EN 62305");
                csv.AppendLine();
                csv.AppendLine("Section,Field,Value");
                foreach (var kv in tokens)
                    csv.AppendLine($"Tokens,{Esc(kv.Key)},{Esc(kv.Value?.ToString())}");
                csv.AppendLine();
                csv.AppendLine("CheckName,Severity,Message");
                foreach (var r in complianceRows)
                    csv.AppendLine($"{Esc(r["CheckName"].ToString())},{r["Severity"]},{Esc(r["Message"].ToString())}");
                csv.AppendLine();
                csv.AppendLine("DownConductor,Id,Family,Level,LengthM,Material,CrossSectMm2,SepDistMm,Status");
                foreach (var r in conductorRows)
                    csv.AppendLine(string.Join(",", new[] { "DC",
                        Esc(r["Id"].ToString()), Esc(r["Family"].ToString()), Esc(r["Level"].ToString()),
                        r["LengthM"].ToString(), Esc(r["Material"].ToString()),
                        r["CrossSectMm2"].ToString(), r["SepDistMm"].ToString(),
                        Esc(r["Status"].ToString()) }));
                csv.AppendLine();
                csv.AppendLine("EarthElectrode,Id,Family,Level,EarthType,ResistanceOhm,TestDate,CertRef,Status");
                foreach (var r in electrodeRows)
                    csv.AppendLine(string.Join(",", new[] { "EE",
                        Esc(r["Id"].ToString()), Esc(r["Family"].ToString()), Esc(r["Level"].ToString()),
                        Esc(r["EarthType"].ToString()), r["ResistanceOhm"].ToString(),
                        Esc(r["TestDate"].ToString()), Esc(r["CertRef"].ToString()),
                        Esc(r["Status"].ToString()) }));
                File.WriteAllText(outPath, csv.ToString());
                return outPath;
            }
            catch (Exception ex)
            {
                StingLog.Error("LPS CSV fallback write failed", ex);
                return null;
            }
        }

    }

    // ════════════════════════════════════════════════════════════════
    //  Helper window for CMD-1 wizard
    // ════════════════════════════════════════════════════════════════

    internal class LpsClassSetupWizard : Window
    {
        private readonly Document _doc;
        private TextBox _bldName, _heightM, _planArea, _perimeter;
        private ComboBox _bldType, _content, _occupant, _consequence, _region, _lossType;
        private CheckBox _svcPower, _svcTelecom, _svcGas, _svcWater;
        private TextBlock _ngLabel, _riskResult;
        private TextBox _ngOverride;
        private ComboBox _classOverride;

        public bool IsCompleted { get; private set; }
        public string SelectedClass { get; private set; }
        public LpsRiskResult RiskResult { get; private set; }
        /// <summary>Project Ng override the user typed; 0 means "use regional default".</summary>
        public double NgOverride { get; private set; }

        public LpsClassSetupWizard(Document doc)
        {
            _doc = doc;
            Title = "STING — LPS Class Setup (BS EN 62305)";
            Width = 720; Height = 660;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 250));
            Build();
            try { StingWindowHelper.ApplyOwner(this); } catch { }
        }

        private void Build()
        {
            var root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var sp = new StackPanel { Margin = new Thickness(16) };
            root.Content = sp;
            Content = root;

            sp.Children.Add(SectionHeader("1. STRUCTURE"));
            string projBld = "";
            try
            {
                projBld = _doc.ProjectInformation?.BuildingName;
                if (string.IsNullOrEmpty(projBld))
                    projBld = ParameterHelpers.GetString(_doc.ProjectInformation, "Building Name");
            }
            catch (Exception ex) { StingLog.Warn($"BuildingName lookup: {ex.Message}"); }
            _bldName = LabeledBox("Building name", string.IsNullOrEmpty(projBld) ? _doc.Title : projBld, sp);
            _heightM = LabeledBox("Height (m)", "10", sp);
            _planArea = LabeledBox("Plan area (m²)", "500", sp);
            _perimeter = LabeledBox("Perimeter (m)", "100", sp);

            _bldType = LabeledCombo("Building type", new[]
            {
                "RESIDENTIAL — Residential",
                "COMMERCIAL — Commercial / Office",
                "INDUSTRIAL — Industrial / Factory",
                "HEALTHCARE — Healthcare / Hospital",
                "EDUCATION — Education / School",
                "CULTURAL — Cultural / Heritage",
                "HAZARDOUS — Hazardous / Explosive"
            }, sp);
            _content = LabeledCombo("Internal content", new[]
            {
                "ORDINARY — Ordinary contents",
                "VALUABLE — Valuable contents",
                "IRREPLACEABLE — Irreplaceable / cultural",
                "LOW_FIRE — Low fire ignition risk",
                "HIGH_FIRE — High fire ignition risk",
                "EXPLOSIVE — Explosive / flammable contents"
            }, sp);
            _occupant = LabeledCombo("Occupant hazard", new[]
            {
                "LOW — Few occupants",
                "MEDIUM — Public access",
                "HIGH — Vulnerable / mass occupancy"
            }, sp);
            _consequence = LabeledCombo("Consequence of failure", new[]
            {
                "LOW", "MEDIUM", "HIGH", "EXTREME"
            }, sp);

            sp.Children.Add(SectionHeader("2. LOCATION & RISK"));
            var regions = (LpsEngine.GetFlashDensityLibrary()?["regions"] as JArray)?
                .Select(r => $"{r["id"]} — {r["name"]} (Ng={r["ng"]})").ToArray()
                ?? new[] { "ug — Uganda (Ng=4)" };
            _region = LabeledCombo("Region (Ng)", regions, sp);
            _ngLabel = new TextBlock { Margin = new Thickness(0, 0, 0, 4), FontStyle = FontStyles.Italic };
            sp.Children.Add(_ngLabel);
            _region.SelectionChanged += (_, __) => UpdateNg();

            // Project-specific override pre-loaded from ELC_LPS_PROJECT_NG_OVERRIDE_NR.
            // Set this from a lightning location service for site-specific accuracy.
            string preload = "";
            try
            {
                double v = LpsEngine.GetDoubleParam(_doc.ProjectInformation, LpsParams.PROJECT_NG_OVERRIDE_NR);
                if (v > 0) preload = v.ToString("F2");
            }
            catch (Exception ex) { StingLog.Warn($"NG override read: {ex.Message}"); }
            _ngOverride = LabeledBox("Project Ng override (blank = use region; from lightning location service, flashes/km²/yr)", preload, sp);
            _ngOverride.TextChanged += (_, __) => UpdateNg();

            _lossType = LabeledCombo("Primary loss type (BS EN 62305-2)", new[]
            {
                "L1 — Loss of human life",
                "L2 — Loss of public service",
                "L3 — Loss of cultural heritage",
                "L4 — Economic loss"
            }, sp);

            sp.Children.Add(new TextBlock { Text = "Connected services:", Margin = new Thickness(0, 4, 0, 4) });
            _svcPower = new CheckBox { Content = "Power (overhead)", Margin = new Thickness(0, 0, 0, 2) };
            _svcTelecom = new CheckBox { Content = "Telecom", Margin = new Thickness(0, 0, 0, 2) };
            _svcGas = new CheckBox { Content = "Gas", Margin = new Thickness(0, 0, 0, 2) };
            _svcWater = new CheckBox { Content = "Water", Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(_svcPower); sp.Children.Add(_svcTelecom); sp.Children.Add(_svcGas); sp.Children.Add(_svcWater);

            var runRisk = new Button { Content = "Run risk assessment", Width = 200, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            runRisk.Click += (_, __) => RunRisk();
            sp.Children.Add(runRisk);
            _riskResult = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 245))
            };
            sp.Children.Add(_riskResult);

            sp.Children.Add(SectionHeader("3. CLASS CONFIRMATION"));
            _classOverride = LabeledCombo("LPS class (override)", new[] { "I", "II", "III", "IV", "NONE" }, sp);
            _classOverride.SelectedIndex = 1;

            sp.Children.Add(SectionHeader("4. APPLY"));
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Apply", Width = 100, Margin = new Thickness(4) };
            var cancel = new Button { Content = "Cancel", Width = 100, Margin = new Thickness(4) };
            ok.Click += (_, __) => { Apply(); };
            cancel.Click += (_, __) => { Close(); };
            btnRow.Children.Add(cancel); btnRow.Children.Add(ok);
            sp.Children.Add(btnRow);

            UpdateNg();
        }

        private TextBlock SectionHeader(string text) => new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 12, 0, 6),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(88, 44, 131))
        };

        private TextBox LabeledBox(string label, string value, StackPanel parent)
        {
            parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) });
            var box = new TextBox { Text = value ?? "", Margin = new Thickness(0, 0, 0, 4) };
            parent.Children.Add(box);
            return box;
        }

        private ComboBox LabeledCombo(string label, IEnumerable<string> items, StackPanel parent)
        {
            parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) });
            var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var i in items) combo.Items.Add(i);
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            parent.Children.Add(combo);
            return combo;
        }

        private string PickId(ComboBox combo)
        {
            string s = combo?.SelectedItem?.ToString() ?? "";
            int dash = s.IndexOf(' ');
            return dash > 0 ? s.Substring(0, dash) : s;
        }

        private void UpdateNg()
        {
            string r = PickId(_region);
            double regional = LpsEngine.GetFlashDensity(r);
            double ng = regional;
            string source = $"region {r}";
            if (_ngOverride != null && double.TryParse(_ngOverride.Text, out double ov) && ov > 0)
            {
                ng = ov;
                source = "project override";
            }
            _ngLabel.Text = $"Ng = {ng:F2} flashes / km² / year ({source}; regional default {regional:F2})";
        }

        private void RunRisk()
        {
            try
            {
                double.TryParse(_heightM.Text, out double H);
                double.TryParse(_planArea.Text, out double area);
                double.TryParse(_perimeter.Text, out double perim);
                double L = Math.Sqrt(Math.Max(area, 1));
                double W = L;
                if (perim > 0 && area > 0)
                {
                    // Solve L*W=area, 2*(L+W)=perim → L,W roots of t^2 - (perim/2)t + area = 0
                    double half = perim / 2.0;
                    double disc = half * half - 4 * area;
                    if (disc >= 0)
                    {
                        double sq = Math.Sqrt(disc);
                        L = (half + sq) / 2.0;
                        W = (half - sq) / 2.0;
                        if (W < 1) { W = 1; L = area; }
                    }
                }

                string regionId = PickId(_region);
                double ng = LpsEngine.GetFlashDensity(regionId);
                if (_ngOverride != null && double.TryParse(_ngOverride.Text, out double ovNg) && ovNg > 0)
                    ng = ovNg;

                var lib = LpsEngine.GetRiskFactorLibrary();
                double cb = LookupFactor(lib, "buildingTypes", PickId(_bldType), "cb", 1.0);
                double cc = LookupFactor(lib, "internalContent", PickId(_content), "cc", 1.0);
                double cd = LookupFactor(lib, "occupantHazard", PickId(_occupant), "cd", 1.0);
                double ce = LookupFactor(lib, "consequenceOfFailure", PickId(_consequence), "ce", 1.0);
                double rt = LookupFactor(lib, "lossTypes", PickId(_lossType), "rt", 1e-5);

                double locationCd = 1.0; // could refine via terrain factor in future
                int services = (_svcPower.IsChecked == true ? 1 : 0)
                             + (_svcTelecom.IsChecked == true ? 1 : 0)
                             + (_svcGas.IsChecked == true ? 1 : 0)
                             + (_svcWater.IsChecked == true ? 1 : 0);
                if (services > 0) locationCd *= (1.0 + 0.25 * services);

                var input = new LpsRiskInput
                {
                    BuildingTypeId = PickId(_bldType), InternalContentId = PickId(_content),
                    OccupantHazardId = PickId(_occupant), ConsequenceId = PickId(_consequence),
                    LossTypeId = PickId(_lossType), RegionId = regionId,
                    GroundFlashDensity = ng, PlanLengthM = L, PlanWidthM = W,
                    PlanAreaM2 = area, HeightM = H,
                    BuildingTypeCb = cb, InternalContentCc = cc,
                    OccupantHazardCd = cd, ConsequenceCe = ce,
                    LocationFactorCd = locationCd, TolerableRisk = rt
                };
                RiskResult = LpsEngine.RunRiskAssessment(input);

                _riskResult.Text = (RiskResult.RequiresLps
                    ? $"LPS REQUIRED — Class {RiskResult.RecommendedClass} recommended.\n"
                    : "LPS NOT REQUIRED based on risk inputs.\n") + RiskResult.Notes;
                if (RiskResult.RequiresLps)
                {
                    int idx = new[] { "I", "II", "III", "IV" }.ToList().IndexOf(RiskResult.RecommendedClass);
                    if (idx >= 0) _classOverride.SelectedIndex = idx;
                }
                else _classOverride.SelectedIndex = 4;
            }
            catch (Exception ex)
            {
                StingLog.Error("Risk run failed", ex);
                _riskResult.Text = "Risk assessment failed: " + ex.Message;
            }
        }

        private static double LookupFactor(JObject lib, string section, string id, string field, double fallback)
        {
            try
            {
                var arr = lib?[section] as JArray;
                if (arr == null) return fallback;
                foreach (var t in arr)
                {
                    if (string.Equals(t["id"]?.ToString(), id, StringComparison.OrdinalIgnoreCase))
                        return t[field]?.Value<double>() ?? fallback;
                }
            }
            catch { }
            return fallback;
        }

        private void Apply()
        {
            string c = _classOverride.SelectedItem?.ToString() ?? "II";
            if (c == "NONE")
            {
                MessageBoxAsTaskDialog("LPS Class Setup",
                    "Class set to NONE — no protection system required by current assessment. Apply will not stamp project parameters.");
                Close();
                return;
            }
            SelectedClass = c;
            if (_ngOverride != null && double.TryParse(_ngOverride.Text, out double ov) && ov > 0)
                NgOverride = ov;
            IsCompleted = true;
            Close();
        }

        private static void MessageBoxAsTaskDialog(string title, string content)
            => TaskDialog.Show("STING — " + title, content);
    }
}
