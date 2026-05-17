// Phase 179 — E4: Electrical calculation seed command
// Exports a CSV/JSON seed file from the current Revit model so that external
// calculation tools (Amtech, EasyPower, Trimble, DIALux, etc.) can be
// pre-populated with the correct panel names, circuit references, and initial
// load data without re-keying from drawings.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Import
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElecCalcSeedCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;
            try
            {
                // 1. Collect panel data from Revit model.
                var panels  = CollectPanels(doc);
                var circuits = CollectCircuits(doc);

                if (panels.Count == 0)
                {
                    TaskDialog.Show("Calc Seed Export",
                        "No electrical equipment found in the model.\n" +
                        "Ensure panels are placed and have RBS_PANEL_NAME populated.");
                    return Result.Succeeded;
                }

                // 2. Ask user: CSV or JSON?
                var fmtDlg = new TaskDialog("Calc Seed Export")
                {
                    MainInstruction = "Export format",
                    MainContent = $"Found {panels.Count} panels and {circuits.Count} circuits.\n\nSelect export format:",
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };
                fmtDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "CSV (Amtech / Trimble)");
                fmtDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "JSON (EasyPower / generic)");
                fmtDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Both");
                var fmt = fmtDlg.Show();

                bool doCsv  = fmt == TaskDialogResult.CommandLink1 || fmt == TaskDialogResult.CommandLink3;
                bool doJson = fmt == TaskDialogResult.CommandLink2 || fmt == TaskDialogResult.CommandLink3;
                if (fmt == TaskDialogResult.Cancel) return Result.Cancelled;

                string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc), "ElecCalcSeed");
                Directory.CreateDirectory(outDir);
                string proj   = SanitiseName(doc.ProjectInformation?.Name ?? "project");
                var files = new List<string>();

                if (doCsv)
                {
                    string csvPath = Path.Combine(outDir, $"{proj}_ElecCalcSeed.csv");
                    WriteCsv(csvPath, panels, circuits);
                    files.Add(csvPath);
                }
                if (doJson)
                {
                    string jsonPath = Path.Combine(outDir, $"{proj}_ElecCalcSeed.json");
                    WriteJson(jsonPath, panels, circuits);
                    files.Add(jsonPath);
                }

                TaskDialog.Show("Calc Seed Export",
                    $"Export complete.\nFiles written to:\n{string.Join("\n", files)}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ElecCalcSeedCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Revit data extraction ────────────────────────────────────────────

        private static List<PanelSeedRecord> CollectPanels(Document doc)
        {
            var list = new List<PanelSeedRecord>();
            foreach (var fi in new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .Cast<FamilyInstance>())
            {
                list.Add(new PanelSeedRecord
                {
                    Name         = fi.Name,
                    PanelName    = fi.LookupParameter("RBS_PANEL_NAME")?.AsString() ?? fi.Name,
                    MainRating   = fi.LookupParameter("RBS_ELEC_PANEL_TOTAL_INSTALLED_LOAD_PARAM")?.AsValueString() ?? "",
                    BusbarRating = fi.LookupParameter("ELC_BUSBAR_RATING_TXT")?.AsString() ?? "",
                    VoltageV     = fi.LookupParameter("RBS_ELEC_VOLTAGE_PARAM")?.AsDouble() ?? 0,
                    PhaseConfig  = fi.LookupParameter("RBS_ELEC_NUMBER_OF_POLES")?.AsInteger().ToString() ?? "",
                    Level        = fi.LevelId != ElementId.InvalidElementId
                        ? (doc.GetElement(fi.LevelId) as Level)?.Name ?? "" : "",
                    FeedFrom     = fi.LookupParameter("RBS_ELEC_PANEL_FEED_PANEL_NAME")?.AsString() ?? ""
                });
            }
            return list;
        }

        private static List<CircuitSeedRecord> CollectCircuits(Document doc)
        {
            var list = new List<CircuitSeedRecord>();
            foreach (var sys in new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>())
            {
                list.Add(new CircuitSeedRecord
                {
                    Panel         = sys.BaseEquipment?.Name ?? "",
                    CircuitNumber = sys.CircuitNumber ?? "",
                    LoadNameTxt   = sys.LookupParameter("ELC_CIRCUIT_DESC_TXT")?.AsString() ?? "",
                    Rating        = sys.LookupParameter("ELC_CIRCUIT_RATING_TXT")?.AsString() ?? "",
                    LoadKVA       = sys.LookupParameter("RBS_ELEC_APPARENT_LOAD") != null
                        ? (sys.LookupParameter("RBS_ELEC_APPARENT_LOAD").AsDouble() / 1000.0).ToString("F2")
                        : "",
                    CsaMm2        = sys.LookupParameter("ELC_CABLE_CSA_MM2_TXT")?.AsString() ?? "",
                    Poles         = sys.LookupParameter("RBS_ELEC_NUMBER_OF_POLES")?.AsInteger().ToString() ?? ""
                });
            }
            return list.OrderBy(c => c.Panel).ThenBy(c => c.CircuitNumber).ToList();
        }

        // ── Writers ──────────────────────────────────────────────────────────

        private static void WriteCsv(string path, List<PanelSeedRecord> panels, List<CircuitSeedRecord> circuits)
        {
            using var sw = new StreamWriter(path);

            // Sheet 1: Panels
            sw.WriteLine("### PANELS ###");
            sw.WriteLine("Name,PanelName,BusbarRating,VoltageV,PhaseConfig,Level,FeedFrom,MainRating");
            foreach (var p in panels)
                sw.WriteLine($"{Q(p.Name)},{Q(p.PanelName)},{Q(p.BusbarRating)},{p.VoltageV:F0},{Q(p.PhaseConfig)},{Q(p.Level)},{Q(p.FeedFrom)},{Q(p.MainRating)}");

            sw.WriteLine();

            // Sheet 2: Circuits
            sw.WriteLine("### CIRCUITS ###");
            sw.WriteLine("Panel,Circuit,Description,Rating,LoadKVA,CsaMm2,Poles");
            foreach (var c in circuits)
                sw.WriteLine($"{Q(c.Panel)},{Q(c.CircuitNumber)},{Q(c.LoadNameTxt)},{Q(c.Rating)},{Q(c.LoadKVA)},{Q(c.CsaMm2)},{Q(c.Poles)}");
        }

        private static void WriteJson(string path, List<PanelSeedRecord> panels, List<CircuitSeedRecord> circuits)
        {
            var obj = new { panels, circuits };
            File.WriteAllText(path, JsonConvert.SerializeObject(obj, Formatting.Indented));
        }

        private static string Q(string s) => $"\"{s?.Replace("\"", "\"\"")}\"";

        private static string SanitiseName(string s) =>
            new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());

        // ── POCOs ────────────────────────────────────────────────────────────

        private class PanelSeedRecord
        {
            public string Name         { get; set; } = "";
            public string PanelName    { get; set; } = "";
            public string MainRating   { get; set; } = "";
            public string BusbarRating { get; set; } = "";
            public double VoltageV     { get; set; }
            public string PhaseConfig  { get; set; } = "";
            public string Level        { get; set; } = "";
            public string FeedFrom     { get; set; } = "";
        }

        private class CircuitSeedRecord
        {
            public string Panel         { get; set; } = "";
            public string CircuitNumber { get; set; } = "";
            public string LoadNameTxt   { get; set; } = "";
            public string Rating        { get; set; } = "";
            public string LoadKVA       { get; set; } = "";
            public string CsaMm2        { get; set; } = "";
            public string Poles         { get; set; } = "";
        }
    }
}
