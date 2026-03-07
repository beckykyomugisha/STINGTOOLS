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

                // Issue #22: Scan for known plugin conflicts at startup.
                // DEFERRED: ScanForConflicts() was calling GetReferencedAssemblies()
                // on all loaded assemblies, which can trigger assembly loading during
                // Revit's initialization phase and destabilize the CLR.
                // Moved to lazy scan on first command dispatch instead.
                // ScanForConflicts();

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

        // ── Assembly conflict detection ─────────────────────────────

        /// <summary>
        /// Known problematic addins that load conflicting RevitAPI versions or share
        /// namespace collisions. Detected at startup and logged as warnings so the
        /// user can diagnose "mysterious" crashes caused by the toxic combination of
        /// multiple plugins competing for the same assembly binding.
        ///
        /// Root causes documented in STINGTOOLS_CRASH_ANALYSIS.md:
        ///   1. DiRoots.One + pyRevitLoader load RevitAPI 25.4.20.0 vs Revit's 25.4.30.0
        ///   2. StingBIM.AI.Revit.dll v0.0.0.0 shares namespace, unsigned
        ///   3. Multiple Newtonsoft.Json versions (DiRoots ships v12, we ship v13)
        /// </summary>
        private static readonly string[] KnownConflictAssemblies =
        {
            "pyRevitLoader",
            "DiRoots.One",
            "DiRootsOne",
            "StingBIM.AI.Revit",
            "StingBIM.AI",
        };

        /// <summary>
        /// Scan all loaded assemblies at startup for known conflicts.
        /// Logs findings to StingTools.log for diagnostics — does NOT block loading.
        /// </summary>
        private static void ScanForConflicts()
        {
            try
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies();
                var revitApiVersions = new List<string>();
                var conflicts = new List<string>();
                int totalAddins = 0;

                foreach (Assembly asm in loaded)
                {
                    try
                    {
                        string name = asm.GetName().Name;
                        string version = asm.GetName().Version?.ToString() ?? "?";

                        // Track RevitAPI version conflicts
                        if (name == "RevitAPI" || name == "RevitAPIUI")
                        {
                            revitApiVersions.Add($"{name} {version} [{asm.Location}]");
                        }

                        // Detect known conflicting addins
                        foreach (string conflict in KnownConflictAssemblies)
                        {
                            if (name.Contains(conflict, StringComparison.OrdinalIgnoreCase))
                            {
                                conflicts.Add($"{name} v{version}");
                                break;
                            }
                        }

                        // Count Revit addins (heuristic: references RevitAPI)
                        if (name != "RevitAPI" && name != "RevitAPIUI" && name != "StingTools")
                        {
                            try
                            {
                                foreach (var refAsm in asm.GetReferencedAssemblies())
                                {
                                    if (refAsm.Name == "RevitAPI")
                                    {
                                        totalAddins++;
                                        // Check for version mismatch
                                        if (revitApiVersions.Count > 0)
                                        {
                                            string hostVer = revitApiVersions[0];
                                            string refVer = refAsm.Version?.ToString() ?? "?";
                                            if (!hostVer.Contains(refVer))
                                            {
                                                conflicts.Add(
                                                    $"{name} references RevitAPI {refVer} (host has different version)");
                                            }
                                        }
                                        break;
                                    }
                                }
                            }
                            catch { /* ReflectionTypeLoadException for some assemblies */ }
                        }
                    }
                    catch { /* Skip assemblies that can't be inspected */ }
                }

                // Log findings
                if (revitApiVersions.Count > 0)
                    StingLog.Info($"RevitAPI loaded: {string.Join("; ", revitApiVersions)}");

                if (conflicts.Count > 0)
                {
                    StingLog.Warn($"CONFLICT DETECTED: {conflicts.Count} known conflict(s) with co-loaded plugins:");
                    foreach (string c in conflicts)
                        StingLog.Warn($"  - {c}");
                    StingLog.Warn("If STING Tools crashes, try disabling conflicting addins. " +
                        "See STINGTOOLS_CRASH_ANALYSIS.md for details.");
                }
                else
                {
                    StingLog.Info($"No known assembly conflicts detected ({totalAddins} other addins loaded)");
                }

                // Check for duplicate Newtonsoft.Json versions (common conflict)
                var jsonVersions = loaded
                    .Where(a => a.GetName().Name == "Newtonsoft.Json")
                    .Select(a => a.GetName().Version?.ToString() ?? "?")
                    .Distinct().ToList();
                if (jsonVersions.Count > 1)
                {
                    StingLog.Warn($"Multiple Newtonsoft.Json versions loaded: {string.Join(", ", jsonVersions)}. " +
                        "This can cause TypeLoadException in JSON deserialization.");
                }
            }
            catch (Exception ex)
            {
                // Conflict scan is non-critical — never block startup
                StingLog.Warn($"Conflict scan failed (non-critical): {ex.Message}");
            }
        }

        // ── Assembly resolution ─────────────────────────────────────

        /// <summary>
        /// Resolve transitive NuGet dependencies (ClosedXML, DocumentFormat.OpenXml, etc.)
        /// from the same directory as the plugin DLL. Revit's default probing path doesn't
        /// include the plugin directory, so these assemblies would fail to load at runtime.
        ///
        /// WHITELIST approach: Only resolve assemblies that are known plugin dependencies.
        /// Everything else (framework, runtime, Revit, unknown) returns null so the CLR
        /// uses its normal resolution. A deny list (blocking all System.*) is too broad —
        /// it blocks NuGet packages like System.IO.Packaging that share the System. prefix.
        ///
        /// IMPORTANT: Never resolve RevitAPI/RevitAPIUI — these must come from Revit's
        /// own directory. Other plugins (DiRoots, pyRevit) may request different versions;
        /// returning null lets the CLR's binding policy handle version unification.
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

        /// <summary>
        /// Assemblies we must NEVER resolve — always let the CLR/Revit handle these.
        /// Prevents our resolver from hijacking Revit's own assemblies or other plugins'
        /// requests, which is the #1 cause of cross-plugin conflicts.
        /// </summary>
        private static readonly HashSet<string> BlockedAssemblies =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RevitAPI", "RevitAPIUI", "RevitAPIIFC", "RevitAPIMacros",
            "AdWindows", "UIFramework", "UIFrameworkServices",
            "WindowsBase", "PresentationCore", "PresentationFramework",
            "mscorlib", "netstandard",
        };

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string pluginDir = Path.GetDirectoryName(AssemblyPath);
            if (string.IsNullOrEmpty(pluginDir)) return null;

            var asmName = new AssemblyName(args.Name);
            string shortName = asmName.Name;

            // NEVER resolve Revit/framework assemblies — prevents cross-plugin conflicts
            if (BlockedAssemblies.Contains(shortName))
                return null;

            // WHITELIST: Only resolve known plugin dependencies from the plugin directory.
            // Everything else falls through to the CLR's default resolution.
            if (!AllowedAssemblies.Contains(shortName))
                return null;

            // Check if this assembly is already loaded (version-tolerant).
            // Prevents loading duplicate versions that conflict with DiRoots/pyRevit.
            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (loaded.GetName().Name == shortName)
                        return loaded; // Reuse existing — avoids version conflict
                }
                catch { }
            }

            string candidate = Path.Combine(pluginDir, shortName + ".dll");
            if (File.Exists(candidate))
            {
                try
                {
                    var loaded = Assembly.LoadFrom(candidate);
                    StingLog.Info($"AssemblyResolve: loaded {shortName} v{loaded.GetName().Version} from plugin dir");
                    return loaded;
                }
                catch (Exception ex) { StingLog.Warn($"AssemblyResolve failed for {shortName}: {ex.Message}"); }
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
