using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using StingTools.UI;

namespace StingTools.Core
{
    /// <summary>
    /// Main Revit external application. Registers the STING Tools dockable panel
    /// as the single unified UI for all 160+ commands across 6 tabs:
    /// SELECT, ORGANISE, DOCS, TEMP, CREATE, VIEW.
    /// The ribbon tab contains only a single toggle button to show/hide the panel.
    /// </summary>
    public class StingToolsApp : IExternalApplication
    {
        public static string AssemblyPath { get; private set; }
        public static string DataPath { get; private set; }

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                AssemblyPath = Assembly.GetExecutingAssembly().Location;
                DataPath = Path.Combine(
                    Path.GetDirectoryName(AssemblyPath) ?? string.Empty,
                    "data");

                // Validate data directory and critical files at startup
                ValidateDataFiles();

                // Pre-flight: log assembly environment for crash diagnostics
                LogAssemblyEnvironment();

                // Register the dockable panel — the single unified UI
                RegisterDockablePanel(application);

                // Register the real-time auto-tagger (IUpdater) — starts disabled
                StingAutoTagger.Register(application);

                // CRASH FIX: Subscribe to DocumentClosing to clear stale static caches.
                // ElementId-based caches and Definition caches become invalid when a
                // document closes. Using them against a new document causes native crashes.
                application.ControlledApplication.DocumentClosing += OnDocumentClosing;

                StingLog.Info("STING Tools dockable panel loaded successfully");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("STING Tools",
                    "Failed to initialise STING Tools:\n" + ex.Message);
                StingLog.Error("Startup failed", ex);
                return Result.Failed;
            }
        }

        /// <summary>
        /// CRASH FIX: Clear all static caches that hold ElementId or Definition references.
        /// These become invalid when a document closes and cause native crashes if used
        /// against a different document.
        /// </summary>
        private static void OnDocumentClosing(object sender,
            Autodesk.Revit.DB.Events.DocumentClosingEventArgs e)
        {
            try
            {
                ParameterHelpers.ClearParamCache();
                ComplianceScan.InvalidateCache();
                UI.StingCommandHandler.ClearStaticState();
                StingLog.Info("DocumentClosing: cleared parameter, compliance, and selection caches");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DocumentClosing cleanup: {ex.Message}");
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            StingLog.Info("STING Tools shutting down");
            StingAutoTagger.Unregister();
            StingLog.Shutdown();
            return Result.Succeeded;
        }

        // ── Assembly Pre-Flight Check ────────────────────────────────

        /// <summary>
        /// Logs the assembly environment at startup for crash diagnostics.
        /// Detects known conflict patterns (version mismatches for key assemblies)
        /// that have been observed to cause native Revit crashes.
        /// </summary>
        private static void LogAssemblyEnvironment()
        {
            try
            {
                var stingAsm = Assembly.GetExecutingAssembly().GetName();
                StingLog.Info($"STING Tools v{stingAsm.Version} loaded from {AssemblyPath}");

                // Check for assemblies we depend on that may conflict with other addins
                var criticalAssemblies = new[] {
                    "Newtonsoft.Json", "ClosedXML", "DocumentFormat.OpenXml",
                    "System.IO.Packaging", "WindowsBase", "RevitAPI", "RevitAPIUI"
                };

                var loaded = AppDomain.CurrentDomain.GetAssemblies();
                var conflicts = new List<string>();

                // Group loaded assemblies by short name to detect version conflicts
                var grouped = loaded
                    .Where(a => !a.IsDynamic)
                    .GroupBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1 || criticalAssemblies.Contains(g.Key));

                foreach (var g in grouped)
                {
                    var versions = g.Select(a => a.GetName().Version?.ToString() ?? "?").Distinct().ToList();
                    if (versions.Count > 1)
                    {
                        string msg = $"CONFLICT: {g.Key} loaded with {versions.Count} versions: " +
                            string.Join(", ", versions);
                        StingLog.Warn(msg);
                        conflicts.Add(msg);
                    }
                    else if (criticalAssemblies.Contains(g.Key))
                    {
                        StingLog.Info($"Assembly: {g.Key} v{versions[0]}");
                    }
                }

                if (conflicts.Count > 0)
                {
                    StingLog.Warn($"Assembly pre-flight: {conflicts.Count} version conflict(s) detected. " +
                        "These may cause intermittent crashes. Check log for details.");
                }
                else
                {
                    StingLog.Info("Assembly pre-flight: no version conflicts detected.");
                }
            }
            catch (Exception ex)
            {
                // Pre-flight check is diagnostic only — never block startup
                StingLog.Warn($"Assembly pre-flight check failed: {ex.Message}");
            }
        }

        // ── Data File Validation ─────────────────────────────────────

        /// <summary>
        /// Validates that the data directory and critical data files exist at startup.
        /// Logs warnings for missing files and shows a one-time dialog if the data
        /// directory is completely missing — this is the #1 cause of command crashes.
        /// </summary>
        private static void ValidateDataFiles()
        {
            if (!Directory.Exists(DataPath))
            {
                StingLog.Warn($"Data directory not found: {DataPath}");
                StingLog.Warn("Data-dependent commands will use fallback defaults. " +
                    "Deploy the data/ folder alongside StingTools.dll.");
                // Don't block startup with a dialog — log is sufficient.
                // Commands that need data files already show their own error dialogs.
                return;
            }

            // Critical files that many commands depend on
            string[] criticalFiles = new[]
            {
                "PARAMETER_REGISTRY.json",
                "MR_PARAMETERS.txt",
                "MR_PARAMETERS.csv",
                "BLE_MATERIALS.csv",
                "MEP_MATERIALS.csv",
                "MR_SCHEDULES.csv",
                "FORMULAS_WITH_DEPENDENCIES.csv",
                "CATEGORY_BINDINGS.csv",
                "LABEL_DEFINITIONS.json",
            };

            var missing = new List<string>();
            foreach (string file in criticalFiles)
            {
                string path = FindDataFile(file);
                if (path == null)
                    missing.Add(file);
            }

            if (missing.Count > 0)
            {
                string list = string.Join(", ", missing);
                StingLog.Warn($"Missing {missing.Count} critical data files: {list}");
                StingLog.Warn($"Data path: {DataPath}");
                StingLog.Warn("Commands that depend on these files will show individual error messages.");
            }
            else
            {
                StingLog.Info($"Data validation passed: all {criticalFiles.Length} critical files found in {DataPath}");
            }
        }

        // ── Dockable Panel Registration ──────────────────────────────

        private void RegisterDockablePanel(UIControlledApplication application)
        {
            try
            {
                // Initialise the external event handler for panel button dispatching
                StingDockPanel.Initialise(application);

                // Register the dockable pane with Revit
                var provider = new StingDockPanelProvider();
                application.RegisterDockablePane(
                    StingDockPanelProvider.PaneId,
                    "STING Tools",
                    provider);

                // Create a minimal ribbon tab with just a toggle button
                const string tabName = "STING Tools";
                application.CreateRibbonTab(tabName);
                string asmPath = AssemblyPath;
                var togglePanel = application.CreateRibbonPanel(tabName, "Panel");
                AddButton(togglePanel, "btnTogglePanel", "STING\nPanel",
                    asmPath, typeof(ToggleDockPanelCommand).FullName,
                    "Show/hide the STING Tools dockable panel");

                StingLog.Info("Dockable panel registered successfully");
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to register dockable panel", ex);
            }
        }

        /* Ribbon panels removed — all commands now accessible via the dockable panel.
           Original panels: Select (22 cmds), Docs (17 cmds), Tags (28 cmds),
           Organise (32 cmds), Temp (64 cmds). */

        // ── Data file utilities ───────────────────────────────────────

        /// <summary>Find a data file by name, searching DataPath, subdirectories,
        /// and common alternative locations relative to the DLL.</summary>
        public static string FindDataFile(string fileName)
        {
            if (string.IsNullOrEmpty(DataPath) && string.IsNullOrEmpty(AssemblyPath))
                return null;

            // 1. Primary: DataPath/fileName (e.g. .../CompiledPlugin/data/BLE_MATERIALS.csv)
            if (!string.IsNullOrEmpty(DataPath))
            {
                try
                {
                    string direct = Path.Combine(DataPath, fileName);
                    if (File.Exists(direct)) return direct;
                }
                catch { /* Path.Combine or File.Exists can fail on invalid paths */ }
            }

            // 2. Search DataPath subdirectories (only if directory actually exists)
            if (!string.IsNullOrEmpty(DataPath))
            {
                try
                {
                    if (Directory.Exists(DataPath))
                    {
                        foreach (string f in Directory.GetFiles(
                            DataPath, fileName, SearchOption.AllDirectories))
                        {
                            return f;
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"FindDataFile '{fileName}': {ex.Message}");
                }
            }

            // 3. Fallback: search alternative paths relative to DLL location
            //    Handles deployment where data/ is at a sibling or parent level
            string dllDir = Path.GetDirectoryName(AssemblyPath) ?? "";
            string[] alternativePaths = new[]
            {
                Path.Combine(dllDir, "Data", fileName),          // Data/ (capital D, source layout)
                Path.Combine(dllDir, "..", "data", fileName),    // ../data/ (parent)
                Path.Combine(dllDir, "..", "Data", fileName),    // ../Data/ (parent, capital)
                Path.Combine(dllDir, "..", "StingTools", "Data", fileName), // sibling project
            };

            foreach (string alt in alternativePaths)
            {
                try
                {
                    string resolved = Path.GetFullPath(alt);
                    if (File.Exists(resolved))
                    {
                        StingLog.Info($"FindDataFile '{fileName}' found at fallback: {resolved}");
                        return resolved;
                    }
                }
                catch { /* path resolution failed, skip */ }
            }

            return null;
        }

        /// <summary>Parse a CSV line respecting quoted fields.</summary>
        public static string[] ParseCsvLine(string line)
        {
            var result = new System.Collections.Generic.List<string>();
            bool inQuote = false;
            var current = new System.Text.StringBuilder();

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuote = !inQuote;
                }
                else if (c == ',' && !inQuote)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }

        // ── Ribbon helpers ───────────────────────────────────────────────

        private static void AddButton(RibbonPanel panel, string name,
            string text, string asmPath, string className,
            string tooltip)
        {
            var data = new PushButtonData(name, text, asmPath, className);
            data.ToolTip = tooltip;
            panel.AddItem(data);
        }

        private static void AddPulldownItem(PulldownButton pulldown,
            string name, string text, string asmPath, string className,
            string tooltip)
        {
            var data = new PushButtonData(name, text, asmPath, className);
            data.ToolTip = tooltip;
            pulldown.AddPushButton(data);
        }
    }

    /// <summary>
    /// Toggle the STING Tools dockable panel visibility.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleDockPanelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                DockablePane pane = ParameterHelpers.GetApp(commandData)
                    .GetDockablePane(StingDockPanelProvider.PaneId);

                if (pane == null)
                {
                    TaskDialog.Show("STING Panel",
                        "Dockable panel not found. Restart Revit to register it.");
                    return Result.Failed;
                }

                if (pane.IsShown())
                    pane.Hide();
                else
                    pane.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Toggle dockable panel failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
