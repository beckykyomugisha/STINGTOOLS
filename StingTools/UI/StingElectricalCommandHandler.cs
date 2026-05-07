using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// IExternalEventHandler that dispatches dock-panel button clicks for the
    /// STING Electrical Center to <see cref="IExternalCommand"/> classes on the
    /// Revit API thread. Mirrors <see cref="StingCommandHandler"/> in shape so
    /// future merging is trivial.
    /// </summary>
    public class StingElectricalCommandHandler : IExternalEventHandler
    {
        public static StingElectricalCommandHandler Instance { get; private set; }
        public static ExternalEvent Event { get; private set; }

        private readonly StingElectricalPanel _panel;
        private readonly object _lock = new object();
        private string _pendingTag;

        // ── Static bridge for ad-hoc inputs / outputs ────────────────────
        public static CableSizerInputSnapshot CurrentCableSizeInput;
        public static StingTools.Commands.Electrical.CableSizer.CableSizeResult LastCableSizeResult;
        public static DescriptionAutoFillOptions CurrentDescOptions;
        public static PanelParamsSnapshot CurrentPanelParams;
        public static VDOptionsSnapshot CurrentVDOptions;
        public static BreakerOptionsSnapshot CurrentBreakerOptions;
        public static BalanceOptionsSnapshot CurrentBalanceOptions;
        public static ConduitFillInputSnapshot CurrentConduitFill;
        public static StingElectricalPanel ActivePanel;
        public static List<StingTools.Commands.Electrical.BreakerProposal> LastBreakerProposals;

        // ── Phase 178 — extra inputs / outputs ──────────────────────────
        public static double CurrentUtilityFaultKa = 25.0;
        public static StingTools.Commands.Electrical.FeederSizing.FeederSettingsSnapshot CurrentFeederSettings;
        public static string CurrentSheetPlacementMode = "GuidedManual";
        public static Autodesk.Revit.DB.ElementId CurrentSheetPlacementSheetId;
        public static StingTools.Commands.SLD.RiserOptions CurrentRiserOptions
            = new StingTools.Commands.SLD.RiserOptions { Layout = "Horizontal", ShowFaultKa = true, ShowFeederCsa = true, ShowLoadingPct = true };
        public static string CurrentLpdStandard = "ASHRAE_90_1_2019";
        public static double CurrentLpdCustomLimit = 0;

        // Snapshot outputs surfaced by Phase 178 commands.
        public static List<StingTools.UI.ConduitFillData> LastConduitFills = new();
        public static List<StingTools.UI.EmergAuditRow> LastEmergAudit = new();
        public static List<StingTools.UI.LpdRow> LastLpdRows = new();

        private StingElectricalCommandHandler(StingElectricalPanel panel) { _panel = panel; }

        public static void Initialize(StingElectricalPanel panel)
        {
            if (Instance != null) return;
            Instance = new StingElectricalCommandHandler(panel);
            ActivePanel = panel;
            try { Event = ExternalEvent.Create(Instance); }
            catch (Exception ex) { StingLog.Error("ElectricalCommandHandler ExternalEvent.Create", ex); }
        }

        public void SetCommand(string tag)
        {
            lock (_lock) _pendingTag = tag ?? "";
            try { Event?.Raise(); }
            catch (Exception ex) { StingLog.Warn($"ElectricalCommandHandler.SetCommand: {ex.Message}"); }
        }

        public string GetName() => "STING Electrical Command Dispatcher";

        public void Execute(UIApplication app)
        {
            // Publish the UIApplication so ParameterHelpers.GetApp() can fall
            // back to it for commands dispatched with null ExternalCommandData.
            try { StingCommandHandler.SetCurrentApp(app); } catch { }

            string tag;
            lock (_lock) tag = _pendingTag;
            if (string.IsNullOrEmpty(tag)) return;

            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null && tag != "ElectricalHub")
            {
                TaskDialog.Show("STING Electrical", "No document is open.");
                return;
            }

            // Snapshot inputs from the panel (must run on UI thread? Already on Revit thread,
            // so dispatch via the panel's Dispatcher.Invoke).
            try { SnapshotInputs(); } catch (Exception ex) { StingLog.Warn($"SnapshotInputs: {ex.Message}"); }

            try
            {
                Dispatch(tag, app, doc);
            }
            catch (Exception ex)
            {
                StingLog.Error($"ElectricalCommandHandler [{tag}]", ex);
                try { TaskDialog.Show("STING Electrical", $"Command failed: {ex.Message}"); } catch { }
            }

            // After every command, push a fresh snapshot so the panel grids stay in sync.
            try { PushSnapshot(doc); }
            catch (Exception ex) { StingLog.Warn($"PushSnapshot: {ex.Message}"); }

            try { _panel?.UpdateStatus("Ready"); } catch { }
        }

        private void SnapshotInputs()
        {
            if (_panel == null) return;
            _panel.Dispatcher.Invoke(() =>
            {
                CurrentCableSizeInput = _panel.ReadCableSizerInputs();
                CurrentDescOptions = _panel.ReadDescriptionOptions();
                CurrentPanelParams = _panel.ReadPanelParams();
                CurrentVDOptions = _panel.ReadVDOptions();
                CurrentBreakerOptions = _panel.ReadBreakerOptions();
                CurrentBalanceOptions = _panel.ReadBalanceOptions();
                CurrentConduitFill = _panel.ReadConduitFillInputs();
            });
        }

        private void Dispatch(string tag, UIApplication app, Document doc)
        {
            switch (tag)
            {
                // ── Toggle the panel from the ribbon button ──────────
                case "ElectricalHub":
                    ToggleVisibility(app);
                    break;

                // ── PNLS ─────────────────────────────────────────────
                case "Panel_BatchSchedules":
                    RunCommand<StingTools.Commands.Panels.BatchPanelSchedulesCommand>(app); break;
                case "Panel_Audit":
                    RunCommand<StingTools.Commands.Panels.PanelScheduleAuditCommand>(app); break;
                case "Panel_FillSlots":
                    RunCommand<StingTools.Commands.Panels.FillSparesAllSchedulesCommand>(app); break;
                case "Panel_AddSpare":
                    RunCommand<StingTools.Commands.Panels.FillEmptySlotsWithSparesCommand>(app); break;
                case "Panel_AddSpace":
                    RunCommand<StingTools.Commands.Panels.FillEmptySlotsWithSpacesCommand>(app); break;
                case "Panel_ConvertSpaceToSpare":
                    RunCommand<StingTools.Commands.Panels.ConvertSpacesToSparesCommand>(app); break;
                case "Panel_ClearSlots":
                    RunCommand<StingTools.Commands.Panels.ClearSparesAndSpacesCommand>(app); break;
                case "Panel_ExcelExport":
                    RunCommand<StingTools.Commands.Panels.ExportPanelSchedulesToExcelCommand>(app); break;
                case "Panel_SyncParams":
                    RunCommand<StingTools.Commands.Electrical.ElecPanelParamSyncCommand>(app); break;
                case "Panel_WriteParams":
                    RunCommand<StingTools.Commands.Electrical.ElecPanelWriteParamsCommand>(app); break;
                case "Panel_EditTemplateRules":
                    OpenTemplateRulesFile(doc); break;
                // Panel_PlaceOnSheets dispatched in the Phase 178 block below.

                // ── CIRCTS ───────────────────────────────────────────
                case "Circuit_AutoDesc":
                    RunCommand<StingTools.Commands.Electrical.CircuitDescriptionCommand>(app); break;
                case "Circuit_PreviewDesc":
                    PreviewCircuitDescriptions(doc); break;
                case "Circuit_ApplyDesc":
                    RunCommand<StingTools.Commands.Electrical.CircuitDescriptionCommand>(app); break;
                case "Circuit_Balance":
                    RunCommand<StingTools.Commands.Electrical.PhaseBalanceCommand>(app); break;
                case "Circuit_Renumber":
                    RunCommand<StingTools.Commands.Electrical.ElecCircuitRenumberCommand>(app); break;
                case "Circuit_Excel":
                    RunCommand<StingTools.Commands.Panels.ExportPanelSchedulesToExcelCommand>(app); break;
                case "Circuit_Move":
                case "Circuit_Create":
                case "Circuit_Delete":
                case "Circuit_Sort":
                    TaskDialog.Show("STING Electrical",
                        $"{tag} is planned for Phase 178. Use the panel schedule UI for now.");
                    break;

                // ── CALCS ────────────────────────────────────────────
                case "Calc_LoadSummary":
                    RunCommand<StingTools.Commands.Electrical.ElecLoadSummaryCommand>(app); break;
                case "Calc_VoltageDrop":
                    RunCommand<StingTools.Commands.Electrical.VoltageDrop.VoltageDropCommand>(app); break;
                case "Calc_FlagVD":
                    RunCommand<StingTools.Commands.Electrical.VoltageDrop.VoltageDropFlagCommand>(app); break;
                case "Calc_SizeBreakers":
                    RunCommand<StingTools.Commands.Electrical.BreakerSizerCommand>(app); break;
                case "Calc_ApplyBreakers":
                    RunCommand<StingTools.Commands.Electrical.BreakerSizerApplyCommand>(app); break;

                // ── SLD ──────────────────────────────────────────────
                case "SLD_Generate":
                case "SLD_Update":
                    TryRunByTypeName("StingTools.Commands.SLD.SLDGeneratorCommand", app);
                    break;
                case "SLD_Refresh":
                    /* fresh snapshot will run after Dispatch */
                    break;
                case "SLD_Export":
                    TryRunByTypeName("StingTools.Commands.SLD.SLDExportCommand", app);
                    break;
                case "SLD_ZoomTo":
                    ZoomToSelectedSld(app, doc);
                    break;
                case "SLD_OpenSchedule":
                    OpenScheduleForSelectedSld(app, doc);
                    break;

                // ── CABLE ────────────────────────────────────────────
                case "Cable_Calculate":
                    RunCommand<StingTools.Commands.Electrical.CableSizer.CableSizerCommand>(app); break;
                case "Cable_ApplyToCircuit":
                    ApplyCableSizeToCircuit(app, doc); break;
                case "Cable_ConduitFill":
                    RunConduitFill(); break;

                // ── LITE ─────────────────────────────────────────────
                case "Lite_Refresh":
                    /* snapshot picks up lighting refresh */
                    break;
                case "Lite_CreateSchedule":
                    RunCommand<StingTools.Commands.Electrical.ElecLightingScheduleCommand>(app); break;
                case "Lite_UpdateTargets":
                    /* snapshot picks up targets */
                    break;

                // ── RPRT ─────────────────────────────────────────────
                case "Rprt_Audit":
                    RunCommand<StingTools.Commands.Panels.PanelScheduleAuditCommand>(app); break;
                case "Rprt_PDF":
                    TaskDialog.Show("STING Electrical",
                        "PDF report generation is queued for Phase 178. Excel export is available now.");
                    break;
                case "Rprt_ExcelExport":
                    RunCommand<StingTools.Commands.Panels.ExportPanelSchedulesToExcelCommand>(app); break;
                case "Rprt_ExcelImport":
                    RunCommand<StingTools.Commands.Panels.ImportPanelSchedulesFromExcelCommand>(app); break;
                case "Rprt_ShowDiff":
                    TaskDialog.Show("STING Electrical",
                        "Last import diff is logged in StingTools.log. A dedicated viewer arrives in Phase 178.");
                    break;
                case "Rprt_CircuitExport":
                    RunCommand<StingTools.Commands.Electrical.ExportCircuitsCommand>(app); break;
                case "Rprt_COBie":
                case "Rprt_COBieStandalone":
                    TryRunByTypeName("StingTools.BIMManager.COBieExportCommand", app);
                    break;
                case "Rprt_VDSchedule":
                    RunCommand<StingTools.Commands.Electrical.Reports.VoltageDropScheduleCommand>(app); break;

                // ── Phase 178 — newly unlocked dispatch ──────────────
                case "Calc_UpsizeWires":
                    RunCommand<StingTools.Commands.Electrical.AutoUpsizeWiresCommand>(app); break;
                case "Calc_FeederSize":
                    RunCommand<StingTools.Commands.Electrical.FeederSizing.FeederSizerCommand>(app); break;
                case "Calc_FaultCurrent":
                    RunCommand<StingTools.Commands.Electrical.FaultCurrent.FaultCurrentCommand>(app); break;
                case "Calc_AicStamp":
                    RunCommand<StingTools.Commands.Electrical.FaultCurrent.AicRatingCommand>(app); break;
                case "SLD_RiserDiagram":
                    RunCommand<StingTools.Commands.SLD.SLDRiserDiagramCommand>(app); break;
                case "SLD_UpdateRiser":
                    RunCommand<StingTools.Commands.SLD.SLDUpdateRiserCommand>(app); break;
                case "Panel_PlaceOnSheets":
                    RunCommand<StingTools.Commands.Electrical.PanelViewScheduleCommand>(app); break;
                case "Circuit_WizardPropose":
                    RunWizardPropose(app, doc); break;
                case "Circuit_CreateWizard":
                    OpenCircuitWizard(app); break;
                case "Cable_ValidateConduitFill":
                    RunCommand<StingTools.Commands.Electrical.ConduitFillValidateCommand>(app); break;
                case "Lite_EmergAudit":
                    RunCommand<StingTools.Commands.Electrical.Lighting.EmergencyLightingAuditCommand>(app); break;
                case "Lite_MarkEmerg":
                    RunCommand<StingTools.Commands.Electrical.Lighting.EmergencyLightingMarkCommand>(app); break;
                case "Lite_LPD":
                    RunCommand<StingTools.Commands.Electrical.Lighting.LightingPowerDensityCommand>(app); break;
                case "Lite_LpdColor":
                    RunCommand<StingTools.Commands.Electrical.Lighting.LpdColorCommand>(app); break;
                case "Rprt_FaultSchedule":
                    RunCommand<StingTools.Commands.Electrical.Reports.FaultCurrentScheduleCommand>(app); break;
                case "Rprt_DemandFactors":
                    RunCommand<StingTools.Commands.Electrical.Reports.DemandFactorReportCommand>(app); break;

                // ── Phase 179 — advanced analysis & external integration ──
                case "Elec_ArcFlash":
                    RunCommand<StingTools.Commands.Electrical.ArcFlash.ArcFlashCommand>(app); break;
                case "Elec_ArcFlashLabels":
                    RunCommand<StingTools.Commands.Electrical.ArcFlash.ArcFlashLabelSheetCommand>(app); break;
                case "Elec_ArcFlashSched":
                    RunCommand<StingTools.Commands.Electrical.ArcFlash.ArcFlashScheduleCommand>(app); break;
                case "Elec_SelectCoord":
                    RunCommand<StingTools.Commands.Electrical.Coordination.SelectiveCoordCommand>(app); break;
                case "Elec_ExportEasyPower":
                    RunCommand<StingTools.Commands.Electrical.Export.EasyPowerExportCommand>(app); break;
                case "Elec_ExportDIALux":
                    RunCommand<StingTools.Commands.Electrical.Export.DIALuxExportCommand>(app); break;
                case "Elec_ExportEtap":
                    RunCommand<StingTools.Commands.Electrical.Export.EtapExportCommand>(app); break;
                case "Elec_AutoRoute":
                    RunCommand<StingTools.Commands.Electrical.Routing.ConduitAutoRouteCommand>(app); break;
                case "Elec_BusbarModel":
                    RunCommand<StingTools.Commands.Electrical.Busbar.BusbarModelingCommand>(app); break;
                case "Elec_PhotoLink":
                    RunCommand<StingTools.Commands.Electrical.Photometric.PhotometricLinkCommand>(app); break;

                default:
                    StingLog.Info($"ElectricalCommandHandler: unknown tag '{tag}'");
                    break;
            }
        }

        // ── helpers ──────────────────────────────────────────────────────

        private void RunCommand<T>(UIApplication app) where T : IExternalCommand, new()
        {
            try
            {
                StingLog.Info($"ElecRunCommand<{typeof(T).Name}>: start");
                var cmd = new T();
                string msg = "";
                var els = new ElementSet();
                cmd.Execute(null, ref msg, els);
                StingLog.Info($"ElecRunCommand<{typeof(T).Name}>: done");
            }
            catch (Exception ex)
            {
                StingLog.Error($"ElecRunCommand<{typeof(T).Name}>: {ex.Message}", ex);
            }
        }

        private void TryRunByTypeName(string fullTypeName, UIApplication app)
        {
            try
            {
                var t = Type.GetType(fullTypeName)
                     ?? AppDomain.CurrentDomain.GetAssemblies()
                            .Select(a => a.GetType(fullTypeName)).FirstOrDefault(x => x != null);
                if (t == null)
                {
                    TaskDialog.Show("STING Electrical",
                        $"Command '{fullTypeName}' is planned for a later phase.");
                    return;
                }
                if (Activator.CreateInstance(t) is IExternalCommand cmd)
                {
                    string msg = "";
                    cmd.Execute(null, ref msg, new ElementSet());
                }
            }
            catch (Exception ex) { StingLog.Warn($"TryRunByTypeName({fullTypeName}): {ex.Message}"); }
        }

        private void ToggleVisibility(UIApplication app)
        {
            try
            {
                var pane = app.GetDockablePane(StingElectricalPanelProvider.PaneId);
                if (pane == null) return;
                if (pane.IsShown()) pane.Hide(); else pane.Show();
            }
            catch (Exception ex) { StingLog.Warn($"Toggle Electrical pane: {ex.Message}"); }
        }

        private void OpenTemplateRulesFile(Document doc)
        {
            try
            {
                string path = StingTools.Core.StingToolsApp.FindDataFile("STING_PANEL_SCHEDULE_TEMPLATES.json");
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    TaskDialog.Show("STING Electrical",
                        "Template rules file not found. Project overrides go to <project>/_BIM_COORD/panel_schedule_templates.json.");
                    return;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { StingLog.Warn($"OpenTemplateRulesFile: {ex.Message}"); }
        }

        private void PlaceOnSheetsGuided(UIApplication app, Document doc)
        {
            // Revit 2024+ broke PanelScheduleSheetInstance.Create. Walk the
            // user through each sheet and let them drag manually.
            TaskDialog.Show("STING Electrical — Place Schedules on Sheets",
                "PanelScheduleSheetInstance.Create is broken in Revit 2024+ and STING does not call it. " +
                "Open each panel schedule and drag it onto the sheet manually. " +
                "STING has stamped each schedule with the elec-panel-schedule-A3 Drawing Type so " +
                "Browser Organizer + drift detection still work.");
        }

        private void PreviewCircuitDescriptions(Document doc)
        {
            try
            {
                var opts = CurrentDescOptions ?? new DescriptionAutoFillOptions();
                var preview = StingTools.Commands.Electrical.CircuitDescriptionCommand
                    .PreviewDescriptions(doc, opts.Source1, opts.Source2, opts.Source3,
                                         opts.Separator, opts.TitleCase);
                string sample = preview.FirstOrDefault().proposed ?? "(no circuits found)";
                _panel?.RefreshDescriptionPreview(sample);
            }
            catch (Exception ex) { StingLog.Warn($"PreviewCircuitDescriptions: {ex.Message}"); }
        }

        private void RunConduitFill()
        {
            try
            {
                var inp = CurrentConduitFill;
                if (inp == null || inp.Wires == null || inp.Wires.Count == 0)
                {
                    TaskDialog.Show("STING Electrical", "Add at least one wire before calculating conduit fill.");
                    return;
                }
                var r = StingTools.Commands.Electrical.CableSizer.CableSizerEngine.CalculateConduitFill(
                    inp.ConduitKey, inp.MaxFillPct, inp.Wires);
                string text = $"Total: {r.TotalWireAreaMm2:0} mm² | Conduit: {r.ConduitInternalAreaMm2:0} mm² | Fill: {r.FillPct:0}%";
                if (!string.IsNullOrEmpty(r.Recommendation)) text += $"  →  {r.Recommendation}";
                _panel?.RefreshConduitFillResult(text, r.Exceeds);
            }
            catch (Exception ex) { StingLog.Warn($"RunConduitFill: {ex.Message}"); }
        }

        private void ApplyCableSizeToCircuit(UIApplication app, Document doc)
        {
            try
            {
                var r = LastCableSizeResult;
                if (r == null || r.RecommendedCsaMm2 <= 0)
                {
                    TaskDialog.Show("STING Electrical",
                        "Run Calculate first to produce a cable size, then select a circuit and click Apply.");
                    return;
                }
                var sel = app.ActiveUIDocument?.Selection?.GetElementIds();
                if (sel == null || sel.Count == 0)
                {
                    TaskDialog.Show("STING Electrical",
                        "Select an ElectricalSystem or panel circuit before applying.");
                    return;
                }
                int updated = 0;
                using (var tx = new Transaction(doc, "STING Apply Cable Size"))
                {
                    tx.Start();
                    foreach (var id in sel)
                    {
                        if (!(doc.GetElement(id) is ElectricalSystem sys)) continue;
                        var p = sys.LookupParameter("Wire Size");
                        if (p == null) continue;
                        try { p.Set(r.CsaLabel); updated++; }
                        catch (Exception ex) { StingLog.Warn($"Apply wire size on system {id}: {ex.Message}"); }
                    }
                    tx.Commit();
                }
                TaskDialog.Show("STING Electrical", $"Applied {r.CsaLabel} to {updated} circuit(s).");
            }
            catch (Exception ex) { StingLog.Warn($"ApplyCableSize: {ex.Message}"); }
        }

        private void ZoomToSelectedSld(UIApplication app, Document doc)
        {
            try
            {
                var item = _panel?.SLDTree?.SelectedItem as SLDNodeViewModel;
                if (item == null || item.RevitElementIdValue == 0)
                {
                    TaskDialog.Show("STING Electrical", "Select a panel in the SLD tree first.");
                    return;
                }
                var id = new ElementId(item.RevitElementIdValue);
                if (doc.GetElement(id) == null) return;
                app.ActiveUIDocument.ShowElements(id);
                app.ActiveUIDocument.Selection.SetElementIds(new[] { id });
            }
            catch (Exception ex) { StingLog.Warn($"ZoomToSelectedSld: {ex.Message}"); }
        }

        private void OpenScheduleForSelectedSld(UIApplication app, Document doc)
        {
            try
            {
                var item = _panel?.SLDTree?.SelectedItem as SLDNodeViewModel;
                if (item == null || item.RevitElementIdValue == 0) return;
                var psv = new FilteredElementCollector(doc)
                    .OfClass(typeof(PanelScheduleView))
                    .Cast<PanelScheduleView>()
                    .FirstOrDefault(v => v.GetPanel()?.Value == item.RevitElementIdValue);
                if (psv == null)
                {
                    TaskDialog.Show("STING Electrical", "No schedule view exists for that panel yet. Run Panel Audit / Batch first.");
                    return;
                }
                app.ActiveUIDocument.ActiveView = psv;
            }
            catch (Exception ex) { StingLog.Warn($"OpenScheduleForSelectedSld: {ex.Message}"); }
        }

        // ── Snapshot push ────────────────────────────────────────────────

        private void PushSnapshot(Document doc)
        {
            if (_panel == null || doc == null) return;
            var snap = StingTools.Commands.Electrical.ElectricalSnapshotBuilder.Build(doc);
            _panel.RefreshFromData(snap);
        }

        // ── Phase 178 wizard helpers ────────────────────────────────────

        private void OpenCircuitWizard(UIApplication app)
        {
            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null) return;
            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var dlg = new CircuitWizardDialog(app);
                    try { dlg.Owner = System.Windows.Application.Current?.MainWindow; } catch { }
                    dlg.ShowDialog();
                });
            }
            catch (Exception ex) { StingLog.Warn($"OpenCircuitWizard: {ex.Message}"); }
        }

        private void RunWizardPropose(UIApplication app, Document doc)
        {
            // The dialog handles the Propose step internally; this dispatch
            // case is reserved for non-modal future use. For now we surface a
            // hint so the button on the dock panel is discoverable.
            TaskDialog.Show("STING Circuit Wizard",
                "Click 'Launch Wizard' to open the modal proposer / editor.");
        }

        public void RefreshWireRefTable(StingElectricalPanel panel)
        {
            try
            {
                if (panel == null) panel = _panel;
                var rows = StingTools.Commands.Electrical.ElectricalSnapshotBuilder
                    .BuildWireRefRows(panel.GetWireRefMaterial(),
                                      panel.GetWireRefInsulation(),
                                      panel.GetWireRefMethod());
                panel.RefreshFromData(new ElectricalPanelSnapshot { WireRefRows = rows });
            }
            catch (Exception ex) { StingLog.Warn($"RefreshWireRefTable: {ex.Message}"); }
        }
    }
}
