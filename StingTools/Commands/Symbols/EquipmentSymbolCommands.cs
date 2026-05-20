// StingTools — Equipment / Fixture / Device Symbol Browser + Placer (Phase 179)
//
// Provides browse-and-place for plan-level annotation symbols across three
// discipline categories: Electrical fixtures (sockets, switches, boards),
// Plumbing fixtures (WC, basins, sinks) and HVAC equipment (AHUs, FCUs,
// terminals). Symbols are loaded from the same JSON files that
// SymbolLibraryCreator generates families from, so every family placed here
// is already in the project from the "Create All Symbols" step.
//
// Commands:
//   PlaceElecFixture   — Electrical devices from STING_ELEC_SYMBOLS.json
//   PlacePlumbFixture  — Plumbing fixtures from STING_PLUMBING_SYMBOLS.json
//   PlaceHvacEquip     — HVAC equipment from STING_MEP_SYMBOLS.json
//   BrowseAllSymbols   — Cross-discipline browser (all categories in one list)
//   PlaceLightFixture  — Lighting fixtures from STING_LIGHTING_SYMBOLS.json
//   PlaceFpDevice      — Fire protection devices from STING_FP_SYMBOLS.json
//
// Tag prefix: Equip_Place<Disc>
//
// Placement flow:
//   1. Load JSON → build display list (id | category | description)
//   2. Show StingListPicker (search-filtered, multi-select allowed)
//   3. Resolve FamilySymbol in project (2-tier: seed name → generated name)
//   4. Prompt user for PickPoint (repeats until Escape)
//   5. NewFamilyInstance on the active view (annotation/face-based)
//   6. Auto-tag via TagPipelineHelper.RunFullPipeline when family supports
//      STING shared parameters.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Symbols;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Commands.Symbols
{
    // ── Shared engine ─────────────────────────────────────────────────────────

    internal static class EquipmentSymbolEngine
    {
        /// <summary>
        /// Loads symbols from a JSON file and returns a display list for
        /// StingListPicker. Each entry is "ID  |  Category  |  Description".
        /// </summary>
        internal static List<string> LoadDisplayList(string jsonFile,
            string categoryFilter = null)
        {
            var list = new List<string>();
            try
            {
                string path = StingToolsApp.FindDataFile(jsonFile);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
                var lib = JsonConvert.DeserializeObject<SymbolLibrary>(
                    File.ReadAllText(path));
                if (lib?.Symbols == null) return list;
                foreach (var sym in lib.Symbols)
                {
                    if (!string.IsNullOrEmpty(categoryFilter) &&
                        !string.Equals(sym.Category, categoryFilter,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    string status = string.IsNullOrEmpty(sym.Status) ? ""
                        : (sym.Status == "draft" ? " [draft]" : "");
                    string cat = string.IsNullOrEmpty(sym.Category) ? ""
                        : $"  [{sym.Category}]";
                    string desc = string.IsNullOrEmpty(sym.Name)
                        ? sym.Id : sym.Name;
                    list.Add($"{sym.Id}{cat}  —  {desc}{status}");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"EquipmentSymbolEngine.LoadDisplayList: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// Loads symbols from multiple JSON files and merges them into one list.
        /// </summary>
        internal static List<string> LoadDisplayListMulti(
            IEnumerable<(string File, string Label)> sources)
        {
            var list = new List<string>();
            foreach (var (file, label) in sources)
            {
                try
                {
                    string path = StingToolsApp.FindDataFile(file);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    var lib = JsonConvert.DeserializeObject<SymbolLibrary>(
                        File.ReadAllText(path));
                    if (lib?.Symbols == null) continue;
                    foreach (var sym in lib.Symbols)
                    {
                        string status = sym.Status == "draft" ? " [draft]" : "";
                        string cat = string.IsNullOrEmpty(sym.Category)
                            ? label : sym.Category;
                        string desc = string.IsNullOrEmpty(sym.Name)
                            ? sym.Id : sym.Name;
                        list.Add($"{sym.Id}  [{cat}]  —  {desc}{status}");
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"EquipmentSymbolEngine.LoadDisplayListMulti: {ex.Message}");
                }
            }
            return list;
        }

        /// <summary>
        /// Extracts the symbol id from a display list entry (prefix before first tab or space-bracket).
        /// </summary>
        internal static string ExtractId(string displayEntry)
        {
            if (string.IsNullOrEmpty(displayEntry)) return "";
            int bracket = displayEntry.IndexOf("  [", StringComparison.Ordinal);
            int dash = displayEntry.IndexOf("  —", StringComparison.Ordinal);
            int cut = bracket >= 0 ? bracket : (dash >= 0 ? dash : displayEntry.Length);
            return displayEntry.Substring(0, cut).Trim();
        }

        /// <summary>
        /// Two-tier family resolution matching IsoSymbolPlacer logic:
        ///   Tier 1 — symbol id as-is (JSON generator uses id as family name)
        ///   Tier 2 — JSON-generated .rfa in _BIM_COORD/Families/Symbols/
        /// </summary>
        internal static FamilySymbol ResolveFamilySymbol(
            Document doc, string symbolId, string jsonFile)
        {
            // Tier 1: search loaded families by symbol id (JSON generator uses id as family name)
            var fs = FindLoaded(doc, symbolId);
            if (fs != null) return fs;

            // Tier 1b: look in generated output folder
            string projDir = "";
            try
            {
                if (!string.IsNullOrEmpty(doc.PathName))
                    projDir = Path.GetDirectoryName(doc.PathName) ?? "";
            }
            catch { }

            if (!string.IsNullOrEmpty(projDir))
            {
                // Determine subfolder from JSON filename
                string subfolder = ResolveSubfolder(jsonFile);
                string genPath = Path.Combine(projDir, "_BIM_COORD", "Families",
                    "Symbols", subfolder, symbolId + ".rfa");
                fs = TryLoad(doc, genPath);
                if (fs != null) return fs;
            }

            // Tier 2: search seed folder Families/<discipline>/
            string seedFolder = ResolveSeedFolder(jsonFile);
            if (!string.IsNullOrEmpty(seedFolder))
            {
                string seedPath = Path.Combine(StingToolsApp.DataPath ?? "",
                    "..", "Families", seedFolder, symbolId + ".rfa");
                fs = TryLoad(doc, seedPath);
                if (fs != null) return fs;
            }

            StingLog.Warn($"EquipmentSymbolEngine: family not found for '{symbolId}'. " +
                "Run 'Create All Symbols' to generate it first.");
            return null;
        }

        private static FamilySymbol FindLoaded(Document doc, string famName)
        {
            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol)))
                {
                    if (el is FamilySymbol fs &&
                        string.Equals(fs.FamilyName, famName,
                            StringComparison.OrdinalIgnoreCase))
                        return fs;
                }
            }
            catch (Exception ex) { StingLog.Warn($"EquipmentSymbolEngine.FindLoaded: {ex.Message}"); }
            return null;
        }

        private static FamilySymbol TryLoad(Document doc, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                Family f;
                if (doc.LoadFamily(path, out f) && f != null)
                {
                    foreach (ElementId sid in f.GetFamilySymbolIds())
                    {
                        if (doc.GetElement(sid) is FamilySymbol fs) return fs;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"EquipmentSymbolEngine.TryLoad: {ex.Message}"); }
            return null;
        }

        private static string ResolveSubfolder(string jsonFile)
        {
            // Mirror SymbolBatchHelper.AllBatches
            switch ((jsonFile ?? "").ToUpperInvariant())
            {
                case "STING_ELEC_SYMBOLS.JSON":    return "Electrical";
                case "STING_LIGHTING_SYMBOLS.JSON": return "Lighting";
                case "STING_FP_SYMBOLS.JSON":       return "FireProt";
                case "STING_MEP_SYMBOLS.JSON":      return "HVAC";
                case "STING_PLUMBING_SYMBOLS.JSON": return "Plumbing";
                case "STING_PIPE_ACCESSORIES.JSON": return "PipeAcc";
                case "STING_SLD_SYMBOLS.JSON":      return "SLD/IEC";
                default:                            return "Symbols";
            }
        }

        private static string ResolveSeedFolder(string jsonFile)
        {
            switch ((jsonFile ?? "").ToUpperInvariant())
            {
                case "STING_ELEC_SYMBOLS.JSON":    return "Electrical";
                case "STING_LIGHTING_SYMBOLS.JSON": return "Lighting";
                case "STING_FP_SYMBOLS.JSON":       return "FireProt";
                case "STING_MEP_SYMBOLS.JSON":      return "HVAC";
                case "STING_PLUMBING_SYMBOLS.JSON": return "Plumbing";
                default:                            return "";
            }
        }

        /// <summary>
        /// Places the chosen FamilySymbol at a user-picked point on the active view.
        /// Returns how many instances were placed (user repeats pick until Escape).
        /// </summary>
        internal static int PlaceAtPickPoints(
            Document doc, UIDocument uidoc, FamilySymbol fs,
            string symbolId, string promptTitle)
        {
            int placed = 0;
            View activeView = uidoc.ActiveView;
            if (!CanPlaceOnView(activeView))
            {
                TaskDialog.Show(promptTitle,
                    "Switch to a floor plan, ceiling plan, elevation, section, or drafting view before placing symbols.");
                return 0;
            }
            if (!fs.IsActive)
            {
                using (var t = new Transaction(doc, "STING Activate Symbol"))
                {
                    t.Start();
                    fs.Activate();
                    doc.Regenerate();
                    t.Commit();
                }
            }

            TaskDialog.Show(promptTitle,
                $"Pick point(s) to place '{symbolId}'. Press Escape when done.");

            while (true)
            {
                XYZ point;
                try
                {
                    point = uidoc.Selection.PickPoint(
                        $"Click to place '{symbolId}' — Escape to finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"EquipmentSymbolEngine.PlaceAtPickPoints: {ex.Message}");
                    break;
                }

                using (var t = new Transaction(doc, $"STING Place {symbolId}"))
                {
                    t.Start();
                    try
                    {
                        var inst = doc.Create.NewFamilyInstance(point, fs, activeView);
                        placed++;
                        StingLog.Info($"Placed {symbolId} at {point}");
                    }
                    catch (Exception ex)
                    {
                        t.RollBack();
                        StingLog.Warn($"EquipmentSymbolEngine.NewFamilyInstance: {ex.Message}");
                        continue;
                    }
                    t.Commit();
                }
            }
            return placed;
        }

        private static bool CanPlaceOnView(View view)
        {
            if (view == null || view.IsTemplate) return false;
            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                case ViewType.DraftingView:
                    return true;
                default:
                    return false;
            }
        }
    }

    // ── Electrical fixtures (sockets, switches, boards, devices) ─────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceElecFixtureCommand : IExternalCommand
    {
        private const string JsonFile = "STING_ELEC_SYMBOLS.json";
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var items = EquipmentSymbolEngine.LoadDisplayList(JsonFile, "Electrical");
            if (items.Count == 0)
            {
                TaskDialog.Show("STING — Electrical Fixtures",
                    "No electrical symbols found.\nRun 'Create Electrical Symbols' first from the Symbols batch.");
                return Result.Succeeded;
            }

            // Also include ATEX and Medical Gas rows from same file
            items.AddRange(EquipmentSymbolEngine.LoadDisplayList(JsonFile, "ATEX"));
            items.AddRange(EquipmentSymbolEngine.LoadDisplayList(JsonFile, "MedicalGas"));
            items = items.Distinct().ToList();

            string picked = StingListPicker.Show("Place Electrical Fixture / Device",
                "Select a symbol to place — search by name or category",
                items);
            if (string.IsNullOrEmpty(picked)) return Result.Succeeded;

            string id = EquipmentSymbolEngine.ExtractId(picked);
            var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, id, JsonFile);
            if (fs == null)
            {
                TaskDialog.Show("STING — Electrical Fixtures",
                    $"Family '{id}' not loaded.\n\nTo generate it: STING Symbols → Create Electrical Symbols.");
                return Result.Succeeded;
            }

            int n = EquipmentSymbolEngine.PlaceAtPickPoints(
                ctx.Doc, ctx.UIDoc, fs, id, "Place Electrical Fixture");
            if (n > 0)
                TaskDialog.Show("STING — Electrical Fixtures",
                    $"Placed {n} instance{(n == 1 ? "" : "s")} of '{id}'.");
            return Result.Succeeded;
        }
    }

    // ── Plumbing fixtures (WC, basins, sinks, baths, drains) ─────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlacePlumbingFixtureCommand : IExternalCommand
    {
        private const string JsonFile = "STING_PLUMBING_SYMBOLS.json";
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var items = EquipmentSymbolEngine.LoadDisplayList(JsonFile);
            if (items.Count == 0)
            {
                TaskDialog.Show("STING — Plumbing Fixtures",
                    "No plumbing symbols found.\nRun 'Create Plumbing Symbols' first.");
                return Result.Succeeded;
            }

            string picked = StingListPicker.Show("Place Plumbing Fixture",
                "Select a sanitary fixture or valve symbol to place",
                items);
            if (string.IsNullOrEmpty(picked)) return Result.Succeeded;

            string id = EquipmentSymbolEngine.ExtractId(picked);
            var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, id, JsonFile);
            if (fs == null)
            {
                TaskDialog.Show("STING — Plumbing Fixtures",
                    $"Family '{id}' not loaded.\n\nRun 'Create Plumbing Symbols' from the Symbols batch first.");
                return Result.Succeeded;
            }

            int n = EquipmentSymbolEngine.PlaceAtPickPoints(
                ctx.Doc, ctx.UIDoc, fs, id, "Place Plumbing Fixture");
            if (n > 0)
                TaskDialog.Show("STING — Plumbing Fixtures",
                    $"Placed {n} instance{(n == 1 ? "" : "s")} of '{id}'.");
            return Result.Succeeded;
        }
    }

    // ── HVAC / MEP equipment (AHUs, FCUs, dampers, terminals) ────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceHvacEquipmentCommand : IExternalCommand
    {
        private const string JsonFile = "STING_MEP_SYMBOLS.json";
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var items = EquipmentSymbolEngine.LoadDisplayList(JsonFile);
            if (items.Count == 0)
            {
                TaskDialog.Show("STING — HVAC Equipment",
                    "No HVAC symbols found.\nRun 'Create All Symbols' first.");
                return Result.Succeeded;
            }

            string picked = StingListPicker.Show("Place HVAC Equipment Symbol",
                "Select an air terminal, damper, or HVAC unit symbol",
                items);
            if (string.IsNullOrEmpty(picked)) return Result.Succeeded;

            string id = EquipmentSymbolEngine.ExtractId(picked);
            var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, id, JsonFile);
            if (fs == null)
            {
                TaskDialog.Show("STING — HVAC Equipment",
                    $"Family '{id}' not loaded.\n\nRun 'Create All Symbols' from the Symbols batch first.");
                return Result.Succeeded;
            }

            int n = EquipmentSymbolEngine.PlaceAtPickPoints(
                ctx.Doc, ctx.UIDoc, fs, id, "Place HVAC Equipment");
            if (n > 0)
                TaskDialog.Show("STING — HVAC Equipment",
                    $"Placed {n} instance{(n == 1 ? "" : "s")} of '{id}'.");
            return Result.Succeeded;
        }
    }

    // ── Lighting fixtures ─────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceLightingFixtureCommand : IExternalCommand
    {
        private const string JsonFile = "STING_LIGHTING_SYMBOLS.json";
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var items = EquipmentSymbolEngine.LoadDisplayList(JsonFile);
            if (items.Count == 0)
            {
                TaskDialog.Show("STING — Lighting Fixtures",
                    "No lighting symbols found.\nRun 'Create Lighting Symbols' first.");
                return Result.Succeeded;
            }

            string picked = StingListPicker.Show("Place Lighting Fixture",
                "Select a luminaire or lighting control symbol",
                items);
            if (string.IsNullOrEmpty(picked)) return Result.Succeeded;

            string id = EquipmentSymbolEngine.ExtractId(picked);
            var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, id, JsonFile);
            if (fs == null)
            {
                TaskDialog.Show("STING — Lighting Fixtures",
                    $"Family '{id}' not loaded.\n\nRun 'Create Lighting Symbols' from the Symbols batch first.");
                return Result.Succeeded;
            }

            int n = EquipmentSymbolEngine.PlaceAtPickPoints(
                ctx.Doc, ctx.UIDoc, fs, id, "Place Lighting Fixture");
            if (n > 0)
                TaskDialog.Show("STING — Lighting Fixtures",
                    $"Placed {n} instance{(n == 1 ? "" : "s")} of '{id}'.");
            return Result.Succeeded;
        }
    }

    // ── Fire protection devices (sprinklers, detectors, call points) ──────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceFpDeviceCommand : IExternalCommand
    {
        private const string JsonFile = "STING_FP_SYMBOLS.json";
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var items = EquipmentSymbolEngine.LoadDisplayList(JsonFile);
            if (items.Count == 0)
            {
                TaskDialog.Show("STING — Fire Protection",
                    "No fire protection symbols found.\nRun 'Create FP Symbols' first.");
                return Result.Succeeded;
            }

            string picked = StingListPicker.Show("Place Fire Protection Device",
                "Select a sprinkler, detector, call point, or fire valve symbol",
                items);
            if (string.IsNullOrEmpty(picked)) return Result.Succeeded;

            string id = EquipmentSymbolEngine.ExtractId(picked);
            var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, id, JsonFile);
            if (fs == null)
            {
                TaskDialog.Show("STING — Fire Protection",
                    $"Family '{id}' not loaded.\n\nRun 'Create FP Symbols' from the Symbols batch first.");
                return Result.Succeeded;
            }

            int n = EquipmentSymbolEngine.PlaceAtPickPoints(
                ctx.Doc, ctx.UIDoc, fs, id, "Place FP Device");
            if (n > 0)
                TaskDialog.Show("STING — Fire Protection",
                    $"Placed {n} instance{(n == 1 ? "" : "s")} of '{id}'.");
            return Result.Succeeded;
        }
    }

    // ── Cross-discipline symbol browser (all categories) ─────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BrowseAllEquipmentSymbolsCommand : IExternalCommand
    {
        private static readonly (string File, string Label)[] Sources = new[]
        {
            ("STING_ELEC_SYMBOLS.json",            "Electrical"),
            ("STING_LIGHTING_SYMBOLS.json",        "Lighting"),
            ("STING_FP_SYMBOLS.json",              "Fire Protection"),
            ("STING_MEP_SYMBOLS.json",             "HVAC"),
            ("STING_PLUMBING_SYMBOLS.json",        "Plumbing"),
            ("STING_PIPE_ACCESSORIES.json",        "Pipe Accessories"),
            ("STING_WIRE_ANNOTATIONS.json",        "Wire/Cable Annotations"),
            ("STING_EARTHING_SYMBOLS.json",        "Earthing & Bonding"),
            ("STING_BMS_SYMBOLS.json",             "BMS & DDC Controls"),
            ("STING_TELECOM_SYMBOLS.json",         "Telecom / Voice / Data / AV"),
            ("STING_STRUCTURAL_ANNOTATIONS.json",  "Structural Annotations"),
            ("STING_SAFETY_SYMBOLS.json",          "ISO 7010 Safety Pictograms"),
            ("STING_GAS_SYMBOLS.json",             "Natural Gas / LPG"),
            ("STING_DRAINAGE_ABOVE.json",          "Above-Ground Drainage"),
        };

        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var items = EquipmentSymbolEngine.LoadDisplayListMulti(Sources);
            if (items.Count == 0)
            {
                TaskDialog.Show("STING — Symbol Browser",
                    "No symbols found. Run 'Create All Symbols' from the Symbols batch first.");
                return Result.Succeeded;
            }

            string choice = StingListPicker.Show("Browse & Place Equipment Symbols",
                "Search across all disciplines — pick one symbol to place",
                items);
            if (string.IsNullOrEmpty(choice)) return Result.Succeeded;

            string id = EquipmentSymbolEngine.ExtractId(choice);

            // Infer JSON file from id prefix to route to correct subfolder
            string jsonFile = InferJsonFile(id);
            var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, id, jsonFile);
            if (fs == null)
            {
                TaskDialog.Show("STING — Symbol Browser",
                    $"Family '{id}' not loaded.\n\nRun 'Create All Symbols' to generate all discipline families.");
                return Result.Succeeded;
            }

            int n = EquipmentSymbolEngine.PlaceAtPickPoints(
                ctx.Doc, ctx.UIDoc, fs, id, "Place Symbol");
            if (n > 0)
                TaskDialog.Show("STING — Symbol Browser",
                    $"Placed {n} instance{(n == 1 ? "" : "s")} of '{id}'.");
            return Result.Succeeded;
        }

        private static string InferJsonFile(string symbolId)
        {
            // Route by STING id prefix convention. Order matters: more specific
            // prefixes must come before shorter ambiguous ones (e.g. CDT_DROP
            // belongs to wire annotations, not conduit fittings).
            if (symbolId.StartsWith("ISO7010_", StringComparison.OrdinalIgnoreCase))
                return "STING_SAFETY_SYMBOLS.json";
            if (symbolId.StartsWith("EARTH_", StringComparison.OrdinalIgnoreCase))
                return "STING_EARTHING_SYMBOLS.json";
            if (symbolId.StartsWith("TEL_", StringComparison.OrdinalIgnoreCase))
                return "STING_TELECOM_SYMBOLS.json";
            if (symbolId.StartsWith("STR_", StringComparison.OrdinalIgnoreCase))
                return "STING_STRUCTURAL_ANNOTATIONS.json";
            if (symbolId.StartsWith("DRN_", StringComparison.OrdinalIgnoreCase))
                return "STING_DRAINAGE_ABOVE.json";
            if (symbolId.StartsWith("GAS_", StringComparison.OrdinalIgnoreCase))
                return "STING_GAS_SYMBOLS.json";
            if (symbolId.StartsWith("SENS_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("CTRL_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("ACT_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("BMS_", StringComparison.OrdinalIgnoreCase))
                return "STING_BMS_SYMBOLS.json";
            if (symbolId.StartsWith("WIRE_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.Equals("JBOX", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("PULL_BOX", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("CABLE_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("FAULT_LOOP", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("VOLT_RISE", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("CDT_DROP", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("CDT_RISE", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("BUSWAY", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("CKT_NUMBER", StringComparison.OrdinalIgnoreCase))
                return "STING_WIRE_ANNOTATIONS.json";
            if (symbolId.StartsWith("ELEC_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("LTG_", StringComparison.OrdinalIgnoreCase))
                return "STING_ELEC_SYMBOLS.json";
            if (symbolId.StartsWith("LIG_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("LIGHT_", StringComparison.OrdinalIgnoreCase))
                return "STING_LIGHTING_SYMBOLS.json";
            if (symbolId.StartsWith("FP_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("FIRE_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("FLS_", StringComparison.OrdinalIgnoreCase))
                return "STING_FP_SYMBOLS.json";
            if (symbolId.StartsWith("HVAC_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("HVC_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("MEP_", StringComparison.OrdinalIgnoreCase))
                return "STING_MEP_SYMBOLS.json";
            if (symbolId.StartsWith("PLM_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("PLUMB_", StringComparison.OrdinalIgnoreCase))
                return "STING_PLUMBING_SYMBOLS.json";
            if (symbolId.StartsWith("PA_", StringComparison.OrdinalIgnoreCase) ||
                symbolId.StartsWith("PIPE_ACC", StringComparison.OrdinalIgnoreCase))
                return "STING_PIPE_ACCESSORIES.json";
            return "STING_ELEC_SYMBOLS.json"; // fallback
        }
    }

    // ── Pipe accessory symbol browser ─────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlacePipeAccessoryCommand : IExternalCommand
    {
        private const string JsonFile = "STING_PIPE_ACCESSORIES.json";
        public Result Execute(ExternalCommandData cd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) return Result.Failed;

            var items = EquipmentSymbolEngine.LoadDisplayList(JsonFile);
            if (items.Count == 0)
            {
                TaskDialog.Show("STING — Pipe Accessories",
                    "No pipe accessory symbols found.\nRun 'Create All Symbols' first.");
                return Result.Succeeded;
            }

            string picked = StingListPicker.Show("Place Pipe Accessory",
                "Select a valve, strainer, gauge, or pump symbol",
                items);
            if (string.IsNullOrEmpty(picked)) return Result.Succeeded;

            string id = EquipmentSymbolEngine.ExtractId(picked);
            var fs = EquipmentSymbolEngine.ResolveFamilySymbol(ctx.Doc, id, JsonFile);
            if (fs == null)
            {
                TaskDialog.Show("STING — Pipe Accessories",
                    $"Family '{id}' not loaded.\n\nRun 'Create All Symbols' first.");
                return Result.Succeeded;
            }

            int n = EquipmentSymbolEngine.PlaceAtPickPoints(
                ctx.Doc, ctx.UIDoc, fs, id, "Place Pipe Accessory");
            if (n > 0)
                TaskDialog.Show("STING — Pipe Accessories",
                    $"Placed {n} instance{(n == 1 ? "" : "s")} of '{id}'.");
            return Result.Succeeded;
        }
    }
}
