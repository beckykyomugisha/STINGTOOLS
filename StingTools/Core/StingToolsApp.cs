using System;
using System.IO;
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
                string dllDir = Path.GetDirectoryName(AssemblyPath) ?? string.Empty;

                // Search multiple candidate data directories (first found wins)
                DataPath = ResolveDataPath(dllDir);

                // Register the dockable panel — the single unified UI
                RegisterDockablePanel(application);

                // Register the real-time auto-tagger (IUpdater) — starts disabled
                StingAutoTagger.Register(application);

                StingLog.Info($"STING Tools loaded. DataPath={DataPath}");
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

        public Result OnShutdown(UIControlledApplication application)
        {
            StingLog.Info("STING Tools shutting down");
            StingAutoTagger.Unregister();
            StingLog.Shutdown();
            return Result.Succeeded;
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

        /// <summary>
        /// Resolve the data directory by checking multiple candidate locations.
        /// Priority: data/ alongside DLL → Data/ alongside DLL → StingTools/Data/ in repo →
        /// parent directories with data/ or Data/.
        /// </summary>
        private static string ResolveDataPath(string dllDir)
        {
            // Candidate paths in priority order
            string[] candidates = {
                Path.Combine(dllDir, "data"),                    // Standard: data/ alongside DLL
                Path.Combine(dllDir, "Data"),                    // Case variation
                Path.Combine(dllDir, "StingTools", "Data"),      // Dev layout: repo root
            };

            foreach (string path in candidates)
            {
                if (Directory.Exists(path))
                    return path;
            }

            // Walk up parent directories (max 4 levels) looking for Data/ or data/
            string dir = dllDir;
            for (int i = 0; i < 4; i++)
            {
                string parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || parent == dir) break;
                dir = parent;

                string dataLower = Path.Combine(dir, "data");
                if (Directory.Exists(dataLower)) return dataLower;

                // Check for StingTools/Data inside the parent (source repo layout)
                string stingData = Path.Combine(dir, "StingTools", "Data");
                if (Directory.Exists(stingData)) return stingData;
            }

            // Fallback: return standard path even if it doesn't exist yet
            return Path.Combine(dllDir, "data");
        }

        /// <summary>Find a data file by name, searching DataPath, subdirectories, and fallback locations.</summary>
        public static string FindDataFile(string fileName)
        {
            // Primary search: DataPath and subdirectories
            if (!string.IsNullOrEmpty(DataPath) && Directory.Exists(DataPath))
            {
                string direct = Path.Combine(DataPath, fileName);
                if (File.Exists(direct)) return direct;

                try
                {
                    foreach (string f in Directory.GetFiles(
                        DataPath, fileName, SearchOption.AllDirectories))
                    {
                        return f;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"FindDataFile '{fileName}': {ex.Message}");
                }
            }

            // Fallback: search alongside the DLL directly (flat layout)
            if (!string.IsNullOrEmpty(AssemblyPath))
            {
                string dllDir = Path.GetDirectoryName(AssemblyPath);
                if (!string.IsNullOrEmpty(dllDir))
                {
                    string flat = Path.Combine(dllDir, fileName);
                    if (File.Exists(flat)) return flat;
                }
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
                DockablePane pane = commandData.Application
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
