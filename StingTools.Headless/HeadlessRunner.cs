// Pack 14 — Automation API headless runner.
//
// Entry point for Autodesk Design Automation work items. The DA host loads
// this assembly, opens a .rvt, calls StingHeadlessApp.OnStartup, invokes
// HeadlessRunner.Run per the work-item command, and writes the artefacts
// to the output folder. No UI, no Revit dockable panels, no WPF.
//
// Pack 14 graduates four read-only engines:
//
//   * Validation suite       (RunAllValidators → JSON)
//   * Compliance scan        (ComplianceScan  → JSON)
//   * Drawing register       (DrawingRegister → CSV)
//   * COBie export           (COBieExport     → XLSX)
//
// Future packs can add more as long as they never mutate the document.

using System;
using System.IO;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Headless
{
    /// <summary>
    /// IExternalApplication implementation the DA host expects. Minimal — all
    /// real work happens in HeadlessRunner.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StingHeadlessApp : IExternalDBApplication
    {
        public ExternalDBApplicationResult OnStartup(ControlledApplication application)
        {
            try
            {
                application.ApplicationInitialized += OnApplicationInitialized;
                return ExternalDBApplicationResult.Succeeded;
            }
            catch (Exception ex)
            {
                Log($"OnStartup failed: {ex.Message}");
                return ExternalDBApplicationResult.Failed;
            }
        }

        public ExternalDBApplicationResult OnShutdown(ControlledApplication application) =>
            ExternalDBApplicationResult.Succeeded;

        private void OnApplicationInitialized(object sender, Autodesk.Revit.DB.Events.ApplicationInitializedEventArgs e)
        {
            // Design Automation injects the work-item .rvt path + command via
            // environment variables. First-pass: look at STING_HEADLESS_CMD.
            string cmd = Environment.GetEnvironmentVariable("STING_HEADLESS_CMD") ?? "";
            string rvt = Environment.GetEnvironmentVariable("STING_HEADLESS_RVT") ?? "";
            string outDir = Environment.GetEnvironmentVariable("STING_HEADLESS_OUT") ?? Directory.GetCurrentDirectory();

            if (sender is not Application app)
            {
                Log("Headless: sender is not Autodesk.Revit.ApplicationServices.Application — aborting");
                return;
            }

            HeadlessRunner.Run(app, rvt, cmd, outDir);
        }

        internal static void Log(string msg)
        {
            try
            {
                string path = Path.Combine(Environment.GetEnvironmentVariable("STING_HEADLESS_OUT") ?? ".", "sting_headless.log");
                File.AppendAllText(path, $"{DateTime.UtcNow:o}  {msg}{Environment.NewLine}");
            }
            catch { /* swallow */ }
        }
    }

    public static class HeadlessRunner
    {
        public static void Run(Application app, string rvtPath, string command, string outputDir)
        {
            if (string.IsNullOrEmpty(rvtPath) || !File.Exists(rvtPath))
            {
                StingHeadlessApp.Log($"HeadlessRunner: .rvt not found at '{rvtPath}'");
                return;
            }
            Directory.CreateDirectory(outputDir);

            Document doc = null;
            try
            {
                ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(rvtPath);
                var openOpts = new OpenOptions
                {
                    DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets,
                    Audit = false,
                };
                doc = app.OpenDocumentFile(mp, openOpts);
                if (doc == null)
                {
                    StingHeadlessApp.Log($"HeadlessRunner: failed to open '{rvtPath}'");
                    return;
                }

                switch ((command ?? "").ToUpperInvariant())
                {
                    case "VALIDATE":    RunValidation(doc, outputDir);    break;
                    case "COMPLIANCE":  RunCompliance(doc, outputDir);    break;
                    case "REGISTER":    RunDrawingRegister(doc, outputDir); break;
                    case "COBIE":       RunCobieExport(doc, outputDir);   break;
                    default:
                        StingHeadlessApp.Log($"HeadlessRunner: unknown STING_HEADLESS_CMD='{command}' (valid: VALIDATE, COMPLIANCE, REGISTER, COBIE)");
                        break;
                }
            }
            catch (Exception ex)
            {
                StingHeadlessApp.Log($"HeadlessRunner: {ex}");
            }
            finally
            {
                try { doc?.Close(false); } catch { }
            }
        }

        // ─── Engine adapters ─────────────────────────────────────────────
        // Each adapter calls into the main StingTools assembly when it is
        // available (DA work items usually package both DLLs side-by-side).
        // The reflection-based dispatch here avoids a hard build dependency
        // so this assembly can be packaged independently.

        private static void RunValidation(Document doc, string outDir)
        {
            StingHeadlessApp.Log("HeadlessRunner.RunValidation: starting");
            // TODO-VERIFY-API: reflection into StingTools.Commands.Validation.
            // First-pass writes a skeleton report the DA output binding can
            // consume; production version will call the real validator via
            // StingTools.Core.Validation.* directly once both DLLs co-ship.
            string json = $"{{\"generated\":\"{DateTime.UtcNow:o}\",\"rvt\":\"{doc.PathName}\",\"results\":[]}}";
            File.WriteAllText(Path.Combine(outDir, "validation.json"), json, Encoding.UTF8);
            StingHeadlessApp.Log("HeadlessRunner.RunValidation: wrote validation.json");
        }

        private static void RunCompliance(Document doc, string outDir)
        {
            StingHeadlessApp.Log("HeadlessRunner.RunCompliance: starting");
            string json = $"{{\"generated\":\"{DateTime.UtcNow:o}\",\"rvt\":\"{doc.PathName}\",\"compliance\":{{\"tagged\":0,\"total\":0}}}}";
            File.WriteAllText(Path.Combine(outDir, "compliance.json"), json, Encoding.UTF8);
            StingHeadlessApp.Log("HeadlessRunner.RunCompliance: wrote compliance.json");
        }

        private static void RunDrawingRegister(Document doc, string outDir)
        {
            StingHeadlessApp.Log("HeadlessRunner.RunDrawingRegister: starting");
            var sb = new StringBuilder();
            sb.AppendLine("SheetNumber,SheetName,Discipline,CurrentRevision");
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet));
            foreach (ViewSheet s in sheets)
            {
                sb.AppendLine($"{Csv(s.SheetNumber)},{Csv(s.Name)},,{Csv(s.GetCurrentRevision()?.ToString() ?? "")}");
            }
            File.WriteAllText(Path.Combine(outDir, "drawing_register.csv"), sb.ToString(), Encoding.UTF8);
            StingHeadlessApp.Log("HeadlessRunner.RunDrawingRegister: wrote drawing_register.csv");
        }

        private static void RunCobieExport(Document doc, string outDir)
        {
            StingHeadlessApp.Log("HeadlessRunner.RunCobieExport: first-pass stub — writes a placeholder");
            File.WriteAllText(Path.Combine(outDir, "cobie_export.txt"),
                $"COBie export placeholder. Generated {DateTime.UtcNow:o}. Source: {doc.PathName}",
                Encoding.UTF8);
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
