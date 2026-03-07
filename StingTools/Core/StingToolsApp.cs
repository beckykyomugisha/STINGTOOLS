using System;
using System.Collections.Generic;
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
                DataPath = Path.Combine(
                    Path.GetDirectoryName(AssemblyPath) ?? string.Empty,
                    "data");

                // Issue #16: Resolve ClosedXML and other transitive dependencies
                // from the plugin directory. Without this, BOQ export and other
                // features that use ClosedXML will throw FileNotFoundException.
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

                // Issue #19: Log unhandled exceptions that crash Revit.
                // These bypass normal try-catch (StackOverflow, AccessViolation, etc.)
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    if (e.ExceptionObject is Exception ex)
                        StingLog.Error($"FATAL UNHANDLED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                    else
                        StingLog.Error($"FATAL UNHANDLED: {e.ExceptionObject}");
                };

                // Register the dockable panel — the single unified UI
                RegisterDockablePanel(application);

                // Register the real-time auto-tagger (IUpdater) — starts disabled
                StingAutoTagger.Register(application);

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

        public Result OnShutdown(UIControlledApplication application)
        {
            StingLog.Info("STING Tools shutting down");
            AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
            StingAutoTagger.Unregister();
            StingLog.Shutdown();
            return Result.Succeeded;
        }

        /// <summary>
        /// Resolve transitive NuGet dependencies (ClosedXML, DocumentFormat.OpenXml, etc.)
        /// from the same directory as the plugin DLL. Revit's default probing path doesn't
        /// include the plugin directory, so these assemblies would fail to load at runtime.
        ///
        /// WHITELIST approach: Only resolve assemblies that are known plugin dependencies.
        /// Everything else (framework, runtime, Revit, unknown) returns null so the CLR
        /// uses its normal resolution. A deny list (blocking all System.*) is too broad —
        /// it blocks NuGet packages like System.IO.Packaging that share the System. prefix.
        /// </summary>
        private static readonly HashSet<string> AllowedAssemblies =
            new HashSet<string>(StringComparer.Ordinal)
        {
            // Direct NuGet dependencies
            "Newtonsoft.Json",
            "ClosedXML",
            // ClosedXML transitive dependencies
            "ClosedXML.Parser",
            "DocumentFormat.OpenXml",
            "DocumentFormat.OpenXml.Framework",
            "ExcelNumberFormat",
            "RBush",
            "SixLabors.Fonts",
            // System.IO.Packaging is a NuGet package (not a framework assembly)
            // needed by DocumentFormat.OpenXml
            "System.IO.Packaging",
        };

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string pluginDir = Path.GetDirectoryName(AssemblyPath);
            if (string.IsNullOrEmpty(pluginDir)) return null;

            string assemblyName = new System.Reflection.AssemblyName(args.Name).Name;

            // WHITELIST: Only resolve known plugin dependencies from the plugin directory.
            // Everything else falls through to the CLR's default resolution.
            if (!AllowedAssemblies.Contains(assemblyName))
                return null;

            string candidate = Path.Combine(pluginDir, assemblyName + ".dll");
            if (File.Exists(candidate))
            {
                try { return Assembly.LoadFrom(candidate); }
                catch (Exception ex) { StingLog.Warn($"AssemblyResolve failed for {assemblyName}: {ex.Message}"); }
            }
            return null;
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

        /// <summary>Find a data file by name, searching DataPath and subdirectories.</summary>
        public static string FindDataFile(string fileName)
        {
            if (string.IsNullOrEmpty(DataPath))
                return null;

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
                var uiApp = ParameterHelpers.GetApp(commandData);

                // Store UIApplication reference so the command handler can use it
                // for dispatching panel button clicks on the Revit API thread
                UI.StingDockPanel.SetUIApplication(uiApp);

                DockablePane pane = uiApp
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
