// StingTools — MepSymbolEngine.
//
// General-purpose MEP schematic symbol placer for ANY Revit view type:
//   • ISO 6412   — piping/duct/conduit spool section views (1:25–1:50)
//   • MEP Plan   — engineering plan views (1:50–1:200)
//   • SLD        — single-line diagram drafting views (no spatial scale)
//   • Schematic  — RCP, section, detail annotation
//
// Scale handling
// ──────────────
// Every family in the STING symbol library exposes a "Symbol Scale"
// instance parameter (Integer). The engine sets this to view.Scale so
// linework formulas inside the family produce a constant paper-space size
// regardless of the view scale chosen:
//
//   paper size (mm) = model size (mm) / Symbol Scale
//   → model size = paper target × Symbol Scale
//
// Example: target 6 mm paper symbol at 1:50 → 300 mm model
//          same target at 1:100 → 600 mm model
// Both render at 6 mm on paper because the family formula scales correctly.
//
// For SLD drafting views (ViewType.DraftingView), scale is treated as 1
// (model = paper) so symbol geometry is authored at paper size directly.
//
// Colour control
// ──────────────
// Built-in colour schemes are applied to placed FamilyInstances via three
// instance parameters: STING_SYM_R / STING_SYM_G / STING_SYM_B (Integer,
// 0-255). The family's linework visibility must be driven by these params
// (formulaic or via shared-parameter binding) for colour to have effect.
//
// Built-in schemes and their sources:
//   Corporate   — STING standard palette (default)
//   BS1710      — British Standard 1710 pipe identification colours
//   CIBSE       — CIBSE Guide C / TM54 drawing conventions
//   IEC60617    — Monochrome (all black) for electrical schematics
//   ASHRAE      — ASHRAE Handbook colour conventions for HVAC
//   NBS         — NBS colour notation for systems
//
// Symbol catalogue
// ────────────────
// Reads STING_MEP_SYMBOLS_INDEX.csv from Data/MEP/:
//   symbol_code, family_filename, category, standard, view_types,
//   color_scheme, paper_size_mm, description
//
// view_types column: comma-separated list of applicable view types
//   Plan | Section | SLD | Fabrication | All
//
// The ISO 6412 catalogue (STING_ISO_SYMBOLS_INDEX.csv in Data/Fabrication/)
// is also loaded so a single PlaceSymbols call covers both catalogues
// without duplication.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Symbols
{
    // ── Colour scheme ───────────────────────────────────────────────────

    public enum SymbolColorScheme
    {
        Corporate,
        BS1710,
        CIBSE,
        IEC60617,
        ASHRAE,
        NBS,
        Monochrome,
    }

    public sealed class RgbColor
    {
        public int R { get; }
        public int G { get; }
        public int B { get; }
        public RgbColor(int r, int g, int b) { R = r; G = g; B = b; }
        public static readonly RgbColor Black = new RgbColor(0, 0, 0);
    }

    // ── Extended symbol catalogue entry ─────────────────────────────────

    public sealed class MepSymbolEntry
    {
        public string SymbolCode  { get; set; } = "";
        public string FamilyFile  { get; set; } = "";
        public string Category    { get; set; } = "";
        public string Standard    { get; set; } = "";   // ISO6412 | IEC60617 | ASHRAE | ANSI | BS1553
        public string ViewTypes   { get; set; } = "All"; // Plan | Section | SLD | Fabrication | All
        public string ColorScheme { get; set; } = "";   // preferred colour scheme key
        public double PaperSizeMm { get; set; } = 6.0;  // target paper-space size in mm
        public string Description { get; set; } = "";
        public string[] Tokens    { get; set; } = Array.Empty<string>();

        public bool AppliesToViewType(ViewType vt)
        {
            if (string.Equals(ViewTypes, "All", StringComparison.OrdinalIgnoreCase)) return true;
            string vtStr = vt.ToString();
            foreach (var part in ViewTypes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim();
                if (string.Equals(p, "Plan",        StringComparison.OrdinalIgnoreCase)
                    && (vt == ViewType.FloorPlan || vt == ViewType.CeilingPlan || vt == ViewType.EngineeringPlan))
                    return true;
                if (string.Equals(p, "Section",     StringComparison.OrdinalIgnoreCase)
                    && (vt == ViewType.Section || vt == ViewType.Elevation || vt == ViewType.Detail))
                    return true;
                if (string.Equals(p, "SLD",         StringComparison.OrdinalIgnoreCase)
                    && vt == ViewType.DraftingView)
                    return true;
                if (string.Equals(p, "Fabrication", StringComparison.OrdinalIgnoreCase)
                    && (vt == ViewType.Section || vt == ViewType.Detail))
                    return true;
                if (string.Equals(p, vtStr,         StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }

    // ── Placement options ────────────────────────────────────────────────

    public sealed class MepSymbolPlacementOptions
    {
        /// <summary>Which colour scheme to apply to placed instances.</summary>
        public SymbolColorScheme ColorScheme { get; set; } = SymbolColorScheme.Corporate;

        /// <summary>Override colour for all symbols (overrides scheme).</summary>
        public RgbColor ColorOverride { get; set; } = null;

        /// <summary>Skip members that already have a placer-stamped symbol.</summary>
        public bool NewOnly { get; set; } = true;

        /// <summary>Purge previously stamped symbols before re-placing.</summary>
        public bool Replace { get; set; } = false;

        /// <summary>Discipline filter (e.g. "Pipe", "Duct", "Electrical"). Null = all.</summary>
        public string DisciplineFilter { get; set; } = null;

        /// <summary>Standard filter (e.g. "ISO6412", "IEC60617"). Null = all.</summary>
        public string StandardFilter { get; set; } = null;

        /// <summary>Target paper-space symbol size override in mm. 0 = use catalogue default.</summary>
        public double PaperSizeMmOverride { get; set; } = 0.0;
    }

    // ── Placement result ─────────────────────────────────────────────────

    public sealed class MepSymbolPlacementResult
    {
        public int Placed          { get; set; }
        public int Skipped         { get; set; }
        public int Unmatched       { get; set; }
        public int MissingFamilies { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> MissingFamilyNames { get; } = new List<string>();
        public string Summary => $"Placed {Placed} symbols; {Unmatched} unmatched; {MissingFamilies} families missing.";
    }

    // ── Engine ──────────────────────────────────────────────────────────

    public static class MepSymbolEngine
    {
        // Combined index: MEP catalogue + ISO 6412 catalogue.
        private static List<MepSymbolEntry> _index;
        private static readonly object _indexLock = new object();

        // Stamp parameter names — same contract as IsoSymbolPlacer so
        // Replace mode can purge both old and new placer-stamped instances.
        public const string STAMP_BOOL   = "STING_PLACED_BY_SYMBOL_PLACER_BOOL";
        public const string STAMP_VIEW   = "STING_PLACER_VIEW_ID_TXT";
        public const string STAMP_CODE   = "STING_PLACER_SYMBOL_CODE_TXT";
        public const string STAMP_MEMBER = "STING_PLACER_MEMBER_ID_TXT";

        // Colour parameter names baked into every STING symbol family.
        private const string COL_R = "STING_SYM_R";
        private const string COL_G = "STING_SYM_G";
        private const string COL_B = "STING_SYM_B";

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Place MEP schematic symbols on <paramref name="view"/> for every
        /// element in <paramref name="elementIds"/>. Works on Plan, Section,
        /// Elevation, Detail, and DraftingView types.
        /// </summary>
        public static MepSymbolPlacementResult PlaceSymbols(
            Document doc,
            View view,
            ICollection<ElementId> elementIds,
            MepSymbolPlacementOptions opts = null)
        {
            var result = new MepSymbolPlacementResult();
            if (doc == null || view == null || elementIds == null || elementIds.Count == 0)
                return result;

            if (!IsoSymbolPlacer.CanPlaceOnView(view))
            {
                result.Warnings.Add($"View type {view.ViewType} is not supported for symbol placement.");
                return result;
            }

            if (opts == null) opts = new MepSymbolPlacementOptions();
            EnsureIndexLoaded();

            // Scale: SLD drafting views use 1 (paper = model), all others use view.Scale.
            int viewScale = view.ViewType == ViewType.DraftingView ? 1 : (view.Scale > 0 ? view.Scale : 50);

            // Pre-load all families before the placement transaction.
            var familyCache = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            var missingLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Existing-symbol index for idempotency.
            var existingByMember = new Dictionary<long, ElementId>();
            if (opts.NewOnly && !opts.Replace)
                existingByMember = CollectExistingPlaced(doc, view);

            // Resolve colour triplet once per call.
            RgbColor color = opts.ColorOverride
                ?? GetSchemeColor(opts.ColorScheme, "");

            using (var tx = new Transaction(doc, "STING MEP symbols"))
            {
                try { tx.Start(); }
                catch (Exception ex) { result.Warnings.Add($"MepSymbolEngine tx: {ex.Message}"); return result; }

                try
                {
                    if (opts.Replace)
                    {
                        PurgeStamped(doc, view, result);
                        existingByMember.Clear();
                    }

                    // 2D collision tracker (view-space UV).
                    var occupied = new List<UV>();
                    double stepFt = MmToFt(8.0 * viewScale * 0.5);

                    foreach (var eid in elementIds)
                    {
                        var el = doc.GetElement(eid);
                        if (el == null) continue;

                        // Idempotency check.
                        if (opts.NewOnly && !opts.Replace && existingByMember.ContainsKey(eid.Value))
                        { result.Skipped++; continue; }

                        // Find best symbol entry for this element.
                        var entry = ResolveEntry(el, view.ViewType, opts);
                        if (entry == null) { result.Unmatched++; continue; }

                        // Resolve (or lazy-load) family symbol.
                        if (!familyCache.TryGetValue(entry.FamilyFile, out var fs) || fs == null)
                        {
                            fs = ResolveFamilySymbol(doc, entry, result, missingLogged);
                            if (fs != null) familyCache[entry.FamilyFile] = fs;
                        }
                        if (fs == null) { result.MissingFamilies++; continue; }

                        // Compute placement point in view-space then back to world.
                        XYZ worldPt = ResolveWorldAnchor(el);
                        if (worldPt == null) { result.Unmatched++; continue; }

                        UV viewUv = view.ViewType == ViewType.DraftingView
                            ? new UV(worldPt.X, worldPt.Y)   // drafting view = paper space directly
                            : ProjectToViewPlane(view, worldPt);

                        UV pickedUv = Overlaps(occupied, viewUv, stepFt)
                            ? (TryFallbackPositions(viewUv, occupied, stepFt) ?? viewUv)
                            : viewUv;

                        XYZ placePt = view.ViewType == ViewType.DraftingView
                            ? new XYZ(pickedUv.U, pickedUv.V, 0)
                            : UvToXyz(view, pickedUv);

                        try
                        {
                            if (!fs.IsActive) { fs.Activate(); doc.Regenerate(); }
                            var inst = doc.Create.NewFamilyInstance(placePt, fs, view);
                            if (inst == null) { result.Unmatched++; continue; }

                            // Scale.
                            ApplyScale(inst, viewScale, entry.PaperSizeMm,
                                opts.PaperSizeMmOverride > 0 ? opts.PaperSizeMmOverride : 0);

                            // Colour.
                            RgbColor c = opts.ColorOverride
                                ?? GetSchemeColor(opts.ColorScheme, el.Category?.Name ?? "");
                            ApplyColor(inst, c);

                            // Stamp.
                            StampInstance(inst, eid, view, entry.SymbolCode);

                            occupied.Add(pickedUv);
                            result.Placed++;
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"MepSymbolEngine place {eid.Value}: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.Warnings.Add($"MepSymbolEngine fatal: {ex.Message}");
                }
            }
            return result;
        }

        // ── Colour schemes ───────────────────────────────────────────────

        /// <summary>
        /// Returns the standard colour for a given service category under
        /// the chosen scheme. Category is matched case-insensitively.
        ///
        /// Scheme sources:
        ///   BS 1710:2014  — Identification of pipelines and services
        ///   CIBSE Guide C — Reference data, Table C1 drawing colour conventions
        ///   IEC 60617     — Graphical symbols (monochrome — no colour mandated)
        ///   ASHRAE HoF    — HVAC drawing colour conventions
        /// </summary>
        public static RgbColor GetSchemeColor(SymbolColorScheme scheme, string categoryHint)
        {
            switch (scheme)
            {
                case SymbolColorScheme.IEC60617:
                case SymbolColorScheme.Monochrome:
                    return RgbColor.Black;

                case SymbolColorScheme.BS1710:
                    return GetBS1710Color(categoryHint);

                case SymbolColorScheme.CIBSE:
                    return GetCIBSEColor(categoryHint);

                case SymbolColorScheme.ASHRAE:
                    return GetASHRAEColor(categoryHint);

                case SymbolColorScheme.NBS:
                    return GetNBSColor(categoryHint);

                case SymbolColorScheme.Corporate:
                default:
                    return GetCorporateColor(categoryHint);
            }
        }

        // BS 1710:2014 Table 1 — basic identification colours.
        // Additional colour bands identify the specific content.
        private static RgbColor GetBS1710Color(string cat)
        {
            string up = (cat ?? "").ToUpperInvariant();
            // Water services
            if (up.Contains("COLD") || up.Contains("DCW") || up.Contains("CWS"))
                return new RgbColor(0, 128, 0);        // Green
            if (up.Contains("HOT") || up.Contains("HWS") || up.Contains("DHW"))
                return new RgbColor(198, 0, 0);        // Red (BS 1710 signal red)
            // Steam / condensate
            if (up.Contains("STEAM"))
                return new RgbColor(192, 192, 192);    // Silver/grey
            if (up.Contains("CONDENSATE"))
                return new RgbColor(150, 150, 150);    // Mid grey
            // Gas
            if (up.Contains("GAS") || up.Contains("LPG") || up.Contains("NG"))
                return new RgbColor(255, 204, 0);      // Yellow
            // Oil / fuel
            if (up.Contains("OIL") || up.Contains("FUEL"))
                return new RgbColor(130, 70, 20);      // Brown
            // Chemical
            if (up.Contains("CHEM") || up.Contains("ACID"))
                return new RgbColor(128, 0, 128);      // Violet
            // Fire / sprinkler
            if (up.Contains("FIRE") || up.Contains("SPRINKLER") || up.Contains("FLS"))
                return new RgbColor(198, 0, 0);        // Red
            // Drainage / sewer
            if (up.Contains("DRAIN") || up.Contains("SEWER") || up.Contains("SAN") || up.Contains("WASTE"))
                return new RgbColor(0, 0, 0);          // Black
            // Medical gas
            if (up.Contains("OXYGEN") || up.Contains("MGS"))
                return new RgbColor(0, 0, 255);        // Blue
            if (up.Contains("NITROUS"))
                return new RgbColor(0, 150, 200);      // Light blue
            if (up.Contains("AIR") && up.Contains("MEDICAL"))
                return new RgbColor(255, 255, 255);    // White
            if (up.Contains("VACUUM"))
                return new RgbColor(255, 165, 0);      // Yellow/orange
            // Default — neutral grey
            return new RgbColor(100, 100, 100);
        }

        // CIBSE Guide C / standard MEP drawing colours used in UK practice.
        private static RgbColor GetCIBSEColor(string cat)
        {
            string up = (cat ?? "").ToUpperInvariant();
            if (up.Contains("PIPE") || up.Contains("PLUMB") || up.Contains("DCW") || up.Contains("HWS"))
                return new RgbColor(0, 0, 255);        // Blue — all pipework
            if (up.Contains("DUCT") || up.Contains("HVAC") || up.Contains("AIR"))
                return new RgbColor(0, 150, 60);       // Green — ductwork
            if (up.Contains("ELEC") || up.Contains("CONDUIT") || up.Contains("CABLE"))
                return new RgbColor(255, 165, 0);      // Orange — electrical
            if (up.Contains("DRAIN") || up.Contains("SAN"))
                return new RgbColor(139, 69, 19);      // Brown — drainage
            if (up.Contains("FIRE") || up.Contains("FLS"))
                return new RgbColor(255, 0, 0);        // Red — fire services
            if (up.Contains("GAS"))
                return new RgbColor(255, 255, 0);      // Yellow — gas
            if (up.Contains("LOW") || up.Contains("LV") || up.Contains("COMM"))
                return new RgbColor(128, 0, 128);      // Purple — LV/comms
            return new RgbColor(80, 80, 80);           // Dark grey — unclassified
        }

        // ASHRAE Handbook — Fundamentals, Chapter 37 drawing conventions.
        private static RgbColor GetASHRAEColor(string cat)
        {
            string up = (cat ?? "").ToUpperInvariant();
            if (up.Contains("SUPPLY") && (up.Contains("AIR") || up.Contains("DUCT")))
                return new RgbColor(0, 128, 255);      // Blue — supply air
            if (up.Contains("RETURN") && (up.Contains("AIR") || up.Contains("DUCT")))
                return new RgbColor(255, 165, 0);      // Orange — return air
            if (up.Contains("EXHAUST"))
                return new RgbColor(180, 180, 180);    // Grey — exhaust
            if (up.Contains("CHILLED") || up.Contains("CHW"))
                return new RgbColor(0, 200, 255);      // Light blue — chilled water
            if (up.Contains("HEATING") || up.Contains("HHW"))
                return new RgbColor(255, 80, 0);       // Red-orange — hot water heating
            if (up.Contains("CONDENSER") || up.Contains("CW"))
                return new RgbColor(0, 180, 100);      // Green — condenser water
            if (up.Contains("STEAM"))
                return new RgbColor(200, 200, 200);    // Light grey — steam
            return new RgbColor(80, 80, 80);
        }

        // NBS colour notation (simplified — NBS uses full colour-coded layers).
        private static RgbColor GetNBSColor(string cat)
        {
            string up = (cat ?? "").ToUpperInvariant();
            if (up.Contains("PIPE") || up.Contains("PLUMB"))
                return new RgbColor(0, 112, 192);      // NBS Blue
            if (up.Contains("DUCT") || up.Contains("HVAC"))
                return new RgbColor(0, 176, 80);       // NBS Green
            if (up.Contains("ELEC"))
                return new RgbColor(255, 192, 0);      // NBS Amber
            if (up.Contains("FIRE"))
                return new RgbColor(255, 0, 0);        // NBS Red
            if (up.Contains("STRUCT"))
                return new RgbColor(192, 0, 0);        // NBS Dark red
            return RgbColor.Black;
        }

        // STING corporate palette — matches the discipline colors in
        // TagStyleEngine and DrawingTypeRegistry.
        private static RgbColor GetCorporateColor(string cat)
        {
            string up = (cat ?? "").ToUpperInvariant();
            if (up.Contains("PIPE") || up.Contains("PLUMB") || up.Contains("PH"))
                return new RgbColor(0, 112, 192);      // Blue
            if (up.Contains("DUCT") || up.Contains("HVAC") || up.Contains("MECH"))
                return new RgbColor(0, 176, 80);       // Green
            if (up.Contains("ELEC") || up.Contains("LTG") || up.Contains("CONDUIT"))
                return new RgbColor(255, 192, 0);      // Amber
            if (up.Contains("FIRE") || up.Contains("FLS"))
                return new RgbColor(255, 0, 0);        // Red
            if (up.Contains("DRAIN") || up.Contains("SAN") || up.Contains("WASTE"))
                return new RgbColor(139, 90, 43);      // Brown
            if (up.Contains("GAS"))
                return new RgbColor(255, 220, 0);      // Yellow
            if (up.Contains("COMM") || up.Contains("ICT") || up.Contains("LV"))
                return new RgbColor(112, 48, 160);     // Purple
            if (up.Contains("MED") || up.Contains("MGS"))
                return new RgbColor(0, 176, 240);      // Light blue
            return new RgbColor(64, 64, 64);           // Default grey
        }

        // ── Index management ─────────────────────────────────────────────

        private static void EnsureIndexLoaded()
        {
            lock (_indexLock)
            {
                if (_index != null) return;
                var combined = new List<MepSymbolEntry>();

                // 1. STING_MEP_SYMBOLS_INDEX.csv (general MEP + SLD).
                LoadCatalogueFile(
                    StingToolsApp.FindDataFile("STING_MEP_SYMBOLS_INDEX.csv"),
                    "All", combined);

                // 2. STING_ISO_SYMBOLS_INDEX.csv (ISO 6412 / fabrication).
                //    Tagged as Fabrication|Section so the discipline filter
                //    gates them to fabrication views by default.
                LoadCatalogueFile(
                    StingToolsApp.FindDataFile("STING_ISO_SYMBOLS_INDEX.csv"),
                    "Fabrication|Section", combined);

                // Longest code first to prevent shorter prefix matches winning.
                _index = combined
                    .OrderByDescending(e => (e.SymbolCode ?? "").Length)
                    .ThenByDescending(e => e.Tokens?.Length ?? 0)
                    .ToList();
            }
        }

        private static void LoadCatalogueFile(string path, string defaultViewTypes,
            List<MepSymbolEntry> target)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                bool first = true;
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    if (first) { first = false; continue; }
                    var cols = StingToolsApp.ParseCsvLine(line);
                    if (cols == null || cols.Length < 4) continue;

                    var e = new MepSymbolEntry
                    {
                        SymbolCode  = cols[0],
                        FamilyFile  = cols[1],
                        Category    = cols[2],
                        Description = cols[3],
                        // Extended columns (MEP catalogue only).
                        Standard    = cols.Length > 4 ? cols[4] : "",
                        ViewTypes   = cols.Length > 5 && !string.IsNullOrWhiteSpace(cols[5])
                                        ? cols[5] : defaultViewTypes,
                        ColorScheme = cols.Length > 6 ? cols[6] : "",
                        PaperSizeMm = cols.Length > 7 && double.TryParse(cols[7], out double ps)
                                        ? ps : 6.0,
                    };
                    e.Tokens = (e.SymbolCode ?? "")
                        .ToUpperInvariant()
                        .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
                    target.Add(e);
                }
            }
            catch (Exception ex) { StingLog.Warn($"MepSymbolEngine: catalogue load '{path}': {ex.Message}"); }
        }

        // ── Symbol resolution ────────────────────────────────────────────

        private static MepSymbolEntry ResolveEntry(Element el, ViewType vt, MepSymbolPlacementOptions opts)
        {
            if (_index == null) return null;
            string memberName = (el?.Name ?? "").ToUpperInvariant();
            string famName    = "";
            string symName    = "";
            try
            {
                var fi = el as FamilyInstance;
                famName = (fi?.Symbol?.FamilyName ?? "").ToUpperInvariant();
                symName = (fi?.Symbol?.Name ?? "").ToUpperInvariant();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            string haystack  = Normalise($"{memberName} {famName} {symName}");
            string catName   = (el?.Category?.Name ?? "").ToUpperInvariant();

            foreach (var entry in _index)
            {
                // View type gate.
                if (!entry.AppliesToViewType(vt)) continue;
                // Standard filter.
                if (!string.IsNullOrEmpty(opts.StandardFilter) &&
                    !string.IsNullOrEmpty(entry.Standard) &&
                    !string.Equals(entry.Standard, opts.StandardFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                // Discipline/category filter.
                if (!string.IsNullOrEmpty(opts.DisciplineFilter) &&
                    !entry.Category.IndexOf(opts.DisciplineFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                // Token match.
                if (entry.Tokens.Length == 0) continue;
                bool allMatch = true;
                foreach (var tok in entry.Tokens)
                    if (!HasTokenBoundary(haystack, tok)) { allMatch = false; break; }
                if (allMatch) return entry;
            }
            // Category fallback.
            foreach (var entry in _index)
            {
                if (!entry.AppliesToViewType(vt)) continue;
                if (string.IsNullOrEmpty(entry.Category)) continue;
                if (catName.Contains(entry.Category.ToUpperInvariant())) return entry;
            }
            return null;
        }

        // ── Family loading ───────────────────────────────────────────────

        private static FamilySymbol ResolveFamilySymbol(Document doc, MepSymbolEntry entry,
            MepSymbolPlacementResult result, HashSet<string> missingLogged)
        {
            string famName = Path.GetFileNameWithoutExtension(entry.FamilyFile);
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)))
                    if (el is FamilySymbol fs &&
                        string.Equals(fs.FamilyName, famName, StringComparison.OrdinalIgnoreCase))
                        return fs;

                // Try MEP symbols folder, then ISO6412 folder as fallback.
                string[] candidatePaths = new[]
                {
                    Path.Combine(StingToolsApp.DataPath ?? "", "..", "Families", "MEP", entry.FamilyFile),
                    Path.Combine(StingToolsApp.DataPath ?? "", "..", "Families", "SLD", entry.FamilyFile),
                    Path.Combine(StingToolsApp.DataPath ?? "", "..", "Families", "ISO6412", entry.FamilyFile),
                };
                foreach (var fp in candidatePaths)
                {
                    if (!File.Exists(fp)) continue;
                    Family f;
                    if (doc.LoadFamily(fp, out f) && f != null)
                        foreach (ElementId sid in f.GetFamilySymbolIds())
                            if (doc.GetElement(sid) is FamilySymbol fs) return fs;
                }
            }
            catch (Exception ex) { result.Warnings.Add($"MepSymbolEngine.Resolve {famName}: {ex.Message}"); }

            if (missingLogged.Add(famName))
                result.MissingFamilyNames.Add(famName);
            return null;
        }

        // ── Instance configuration ───────────────────────────────────────

        private static void ApplyScale(FamilyInstance inst, int viewScale,
            double paperSizeMm, double paperOverrideMm)
        {
            try
            {
                double target = paperOverrideMm > 0 ? paperOverrideMm : paperSizeMm;
                // Symbol Scale = view scale denominator; family formulas
                // derive model size from: model_mm = target_mm * Symbol Scale
                var p = inst.LookupParameter("Symbol Scale")
                     ?? inst.LookupParameter("STING_SYMBOL_SCALE");
                if (p != null && !p.IsReadOnly)
                {
                    if (p.StorageType == StorageType.Integer) p.Set(viewScale);
                    else if (p.StorageType == StorageType.Double) p.Set((double)viewScale);
                }
                // STING-namespaced redundant param for cross-family consistency.
                var pAss = inst.LookupParameter("STING_ISO_SYMBOL_SCALE_IN")
                        ?? inst.LookupParameter("STING_SYM_SCALE_IN");
                if (pAss != null && !pAss.IsReadOnly && pAss.StorageType == StorageType.Double)
                    pAss.Set((double)viewScale);
            }
            catch (Exception ex) { StingLog.Warn($"MepSymbolEngine.ApplyScale: {ex.Message}"); }
        }

        private static void ApplyColor(FamilyInstance inst, RgbColor color)
        {
            if (color == null) return;
            try
            {
                SetInt(inst, COL_R, color.R);
                SetInt(inst, COL_G, color.G);
                SetInt(inst, COL_B, color.B);
            }
            catch (Exception ex) { StingLog.Warn($"MepSymbolEngine.ApplyColor: {ex.Message}"); }
        }

        private static void SetInt(Element el, string paramName, int val)
        {
            var p = el.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return;
            if (p.StorageType == StorageType.Integer) p.Set(val);
            else if (p.StorageType == StorageType.Double) p.Set((double)val);
        }

        private static void StampInstance(FamilyInstance inst, ElementId memberId,
            View view, string symbolCode)
        {
            try
            {
                var pb = inst.LookupParameter(STAMP_BOOL);
                if (pb != null && !pb.IsReadOnly && pb.StorageType == StorageType.Integer) pb.Set(1);
                var pm = inst.LookupParameter(STAMP_MEMBER);
                if (pm != null && !pm.IsReadOnly && pm.StorageType == StorageType.String)
                    pm.Set(memberId.Value.ToString());
                var pv = inst.LookupParameter(STAMP_VIEW);
                if (pv != null && !pv.IsReadOnly && pv.StorageType == StorageType.String)
                    pv.Set(view.Id.Value.ToString());
                var pc = inst.LookupParameter(STAMP_CODE);
                if (pc != null && !pc.IsReadOnly && pc.StorageType == StorageType.String)
                    pc.Set(symbolCode ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"MepSymbolEngine.Stamp: {ex.Message}"); }
        }

        // ── Idempotency / purge ──────────────────────────────────────────

        private static Dictionary<long, ElementId> CollectExistingPlaced(Document doc, View view)
        {
            var map = new Dictionary<long, ElementId>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc, view.Id).OfClass(typeof(FamilyInstance)))
                {
                    if (!(el is FamilyInstance fi)) continue;
                    var pb = fi.LookupParameter(STAMP_BOOL);
                    if (pb == null || pb.StorageType != StorageType.Integer || pb.AsInteger() != 1) continue;
                    var pm = fi.LookupParameter(STAMP_MEMBER);
                    if (pm == null || pm.StorageType != StorageType.String) continue;
                    if (long.TryParse(pm.AsString(), out long key)) map[key] = fi.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"MepSymbolEngine.CollectExisting: {ex.Message}"); }
            return map;
        }

        private static void PurgeStamped(Document doc, View view, MepSymbolPlacementResult result)
        {
            try
            {
                var toDelete = new List<ElementId>();
                foreach (var el in new FilteredElementCollector(doc, view.Id))
                {
                    if (el == null) continue;
                    var pb = el.LookupParameter(STAMP_BOOL);
                    if (pb != null && pb.StorageType == StorageType.Integer && pb.AsInteger() == 1)
                        toDelete.Add(el.Id);
                }
                foreach (var id in toDelete)
                    try { doc.Delete(id); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            catch (Exception ex) { result.Warnings.Add($"MepSymbolEngine.Purge: {ex.Message}"); }
        }

        // ── Geometry helpers ─────────────────────────────────────────────

        private static XYZ ResolveWorldAnchor(Element el)
        {
            try
            {
                var lp = el.Location as LocationPoint;
                if (lp != null) return lp.Point;
                var lc = el.Location as LocationCurve;
                if (lc?.Curve != null)
                    try { return lc.Curve.Evaluate(0.5, true); }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return lc.Curve.GetEndPoint(0); }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return null;
        }

        private static UV ProjectToViewPlane(View view, XYZ world)
        {
            try
            {
                XYZ origin = view.Origin ?? XYZ.Zero;
                XYZ right  = view.RightDirection ?? XYZ.BasisX;
                XYZ up     = view.UpDirection    ?? XYZ.BasisY;
                XYZ delta  = world - origin;
                return new UV(delta.DotProduct(right), delta.DotProduct(up));
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return new UV(0, 0); }
        }

        private static XYZ UvToXyz(View view, UV uv)
        {
            XYZ origin = view.Origin ?? XYZ.Zero;
            XYZ right  = view.RightDirection ?? XYZ.BasisX;
            XYZ up     = view.UpDirection    ?? XYZ.BasisY;
            return origin + right.Multiply(uv.U) + up.Multiply(uv.V);
        }

        private static bool Overlaps(List<UV> occ, UV c, double step)
        {
            double r2 = step * step;
            foreach (var p in occ) { double du = p.U - c.U, dv = p.V - c.V; if (du * du + dv * dv < r2) return true; }
            return false;
        }

        private static UV TryFallbackPositions(UV anchor, List<UV> occ, double step)
        {
            for (int r = 1; r <= 2; r++)
            {
                double s = step * r;
                UV[] cands = { new UV(anchor.U, anchor.V + s), new UV(anchor.U + s, anchor.V + s),
                               new UV(anchor.U + s, anchor.V), new UV(anchor.U + s, anchor.V - s),
                               new UV(anchor.U, anchor.V - s), new UV(anchor.U - s, anchor.V - s),
                               new UV(anchor.U - s, anchor.V), new UV(anchor.U - s, anchor.V + s) };
                foreach (var c in cands) if (!Overlaps(occ, c, step)) return c;
            }
            return null;
        }

        private static string Normalise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            return sb.ToString();
        }

        private static bool HasTokenBoundary(string norm, string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            int idx = 0;
            while ((idx = norm.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
            {
                bool lo = idx == 0 || !char.IsLetterOrDigit(norm[idx - 1]);
                bool ro = idx + token.Length >= norm.Length || !char.IsLetterOrDigit(norm[idx + token.Length]);
                if (lo && ro) return true;
                idx += token.Length;
            }
            return false;
        }

        private static double MmToFt(double mm) => mm / 304.8;
    }
}
