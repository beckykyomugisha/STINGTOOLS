// StingTools — Drawing Template Manager · P5 — title-block family resolution
//
// The DrawingType.titleBlockFamily field historically carried *logical*
// (dangling) names — "STING_TB_SHEET_A1", "STING_TB_SHEET_A3",
// "STING_TB_SHEET_A1_PRESENTATION", "STING_TB_ASSEMBLY_PIPE" — that never
// matched the CONCRETE families the factory actually builds
// (STING_TB_A1_BIM_v2.0 / _NONBIM_v2.0 / STING_TB_ASSEMBLY_PIPE_v1.0 …).
// A producer that took the logical name literally never found a symbol and
// silently fell back to whatever title block happened to be first in the
// document.
//
// TitleBlockResolver is the single choke point that turns a logical name
// (or a blank field) into the real built family name, using:
//   * paperSize     → A0 / A1 / A3  (A2 / A4 out of scope)
//   * orientation   → Portrait inserts "_PORT"
//   * BIM mode      → STING_SHEET_BIM_MODE_TXT on ProjectInformation,
//                     default "BIM"
// so an A1 landscape MEP plan resolves to STING_TB_A1_BIM_v2.0, an A3
// portrait to STING_TB_A3_PORT_BIM_v2.0, a spool to STING_TB_ASSEMBLY_PIPE_v1.0.
//
// Names that are ALREADY concrete (declared in STING_TITLE_BLOCKS.json as a
// non-abstract family, or already loaded in the document) pass through
// unchanged, as do names outside the STING_TB_* logical vocabulary
// (healthcare "STING - …" families, legacy real families) — the resolver
// only rewrites the dangling STING_TB_SHEET_* / STING_TB_ASSEMBLY_* names it
// recognises.
//
// Delivery: EnsureFamilyLoaded lazily loads a built-but-not-loaded .rfa from
// Families/TitleBlocks/ on demand, so a project that ran TitleBlock_CreateAll
// once never hits a "family not found" during production.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public static class TitleBlockResolver
    {
        // ── Public API ──────────────────────────────────────────────────

        /// <summary>
        /// Map a (possibly logical / blank) title-block family name to the
        /// concrete built family for the given drawing type. Never throws;
        /// returns the input unchanged when it can't recognise the name.
        /// </summary>
        public static string ToConcreteFamily(Document doc, DrawingType dt, string declaredFamily)
        {
            try
            {
                var declared = (declaredFamily ?? "").Trim();

                // Already concrete (data-driven set) or already loaded → keep.
                if (declared.Length > 0
                    && (IsConcreteFamily(declared) || IsLoadedTitleBlock(doc, declared)))
                    return declared;

                string mode = ResolveMode(doc, dt);
                string orientation = dt?.Orientation;
                string paper = dt?.PaperSize;

                // Fabrication / assembly logical name → concrete _v1.0.
                if (declared.StartsWith("STING_TB_ASSEMBLY_", StringComparison.OrdinalIgnoreCase))
                    return EnsureVersionSuffix(declared);

                // Presentation logical name → the built presentation family.
                if (declared.IndexOf("PRESENT", StringComparison.OrdinalIgnoreCase) >= 0
                    && declared.StartsWith("STING_TB_SHEET", StringComparison.OrdinalIgnoreCase))
                    return "STING_TB_PRESENT_A1_v1.0";

                // Working-sheet logical name → derive by size / orientation / mode.
                if (declared.StartsWith("STING_TB_SHEET", StringComparison.OrdinalIgnoreCase))
                {
                    var size = ExtractSizeCode(declared) ?? NormalizePaper(paper);
                    var derived = DeriveSheetFamily(size, orientation, mode);
                    if (derived != null) return derived;
                }

                // Blank field → derive from paper / orientation / mode.
                if (declared.Length == 0)
                {
                    var derived = DeriveSheetFamily(NormalizePaper(paper), orientation, mode);
                    if (derived != null) return derived;
                }

                // Unknown vocabulary (healthcare / legacy real family) — leave as-is.
                return declared;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TitleBlockResolver.ToConcreteFamily('{declaredFamily}'): {ex.Message}");
                return declaredFamily;
            }
        }

        /// <summary>
        /// Resolve the BIM mode ("BIM" / "NONBIM") for a drawing type. Reads
        /// STING_SHEET_BIM_MODE_TXT from ProjectInformation when bound;
        /// defaults to "BIM".
        /// </summary>
        public static string ResolveMode(Document doc, DrawingType dt)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                var p = pi?.LookupParameter("STING_SHEET_BIM_MODE_TXT");
                var v = p?.StorageType == StorageType.String ? p.AsString() : null;
                if (!string.IsNullOrWhiteSpace(v))
                    return v.IndexOf("NON", StringComparison.OrdinalIgnoreCase) >= 0 ? "NONBIM" : "BIM";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return "BIM";
        }

        /// <summary>
        /// Lazily load a built title-block family (.rfa) from disk when it is
        /// not already present in the document. Searches the project's
        /// Families/TitleBlocks/ folder then the addin directory. Must run
        /// inside the caller's transaction. Returns true when the family is
        /// present afterwards (already-loaded or freshly loaded).
        /// </summary>
        public static bool EnsureFamilyLoaded(Document doc, string familyName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(familyName)) return false;
            if (IsLoadedTitleBlock(doc, familyName)) return true;
            foreach (var path in CandidateRfaPaths(doc, familyName))
            {
                if (!File.Exists(path)) continue;
                try
                {
                    if (doc.LoadFamily(path, new TbLoadOptions(), out Family fam) && fam != null)
                        return true;
                    if (fam != null) return true; // already-present short-circuit
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TitleBlockResolver.EnsureFamilyLoaded '{familyName}': {ex.Message}");
                }
            }
            return false;
        }

        // ── Internals ───────────────────────────────────────────────────

        private static string DeriveSheetFamily(string size, string orientation, string mode)
        {
            if (string.IsNullOrEmpty(size)) return null;
            size = size.ToUpperInvariant();
            // A2 / A4 are explicitly out of scope for the two-family architecture.
            if (size != "A0" && size != "A1" && size != "A3") return null;
            bool portrait = string.Equals((orientation ?? "").Trim(), "Portrait", StringComparison.OrdinalIgnoreCase);
            string port = portrait ? "_PORT" : "";
            string m = string.Equals(mode, "NONBIM", StringComparison.OrdinalIgnoreCase) ? "NONBIM" : "BIM";
            return $"STING_TB_{size}{port}_{m}_v2.0";
        }

        // "STING_TB_SHEET_A1" / "STING_TB_SHEET_A3_PRESENTATION" → "A1" / "A3".
        private static string ExtractSizeCode(string logical)
        {
            if (string.IsNullOrEmpty(logical)) return null;
            foreach (var code in new[] { "A0", "A1", "A3" })
            {
                var idx = logical.IndexOf(code, StringComparison.OrdinalIgnoreCase);
                while (idx >= 0)
                {
                    bool leftOk = idx == 0 || !char.IsLetterOrDigit(logical[idx - 1]);
                    bool rightOk = idx + code.Length == logical.Length
                                || !char.IsLetterOrDigit(logical[idx + code.Length]);
                    if (leftOk && rightOk) return code.ToUpperInvariant();
                    idx = logical.IndexOf(code, idx + 1, StringComparison.OrdinalIgnoreCase);
                }
            }
            return null;
        }

        private static string NormalizePaper(string paper)
        {
            paper = (paper ?? "").Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(paper) ? "A1" : paper;
        }

        // Fab logical names ("STING_TB_ASSEMBLY_PIPE") gain the _v1.0 suffix
        // unless they already carry a _vN.N version tag.
        private static string EnsureVersionSuffix(string name)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(
                    name ?? "", @"_v\d+\.\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return name;
            return name + "_v1.0";
        }

        // Data-driven set of concrete family ids from STING_TITLE_BLOCKS.json
        // (every non-abstract family). Cached once — the corporate baseline is
        // read-only at runtime.
        private static HashSet<string> _concreteFamilies;
        private static readonly object _lock = new object();

        private static HashSet<string> ConcreteFamilies()
        {
            if (_concreteFamilies != null) return _concreteFamilies;
            lock (_lock)
            {
                if (_concreteFamilies != null) return _concreteFamilies;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var lib = TitleBlockSpecRegistry.Load();
                    if (lib?.Families != null)
                        foreach (var f in lib.Families)
                            if (!f.Abstract && !string.IsNullOrWhiteSpace(f.Id))
                                set.Add(f.Id);
                }
                catch (Exception ex) { StingLog.Warn($"TitleBlockResolver.ConcreteFamilies: {ex.Message}"); }
                _concreteFamilies = set;
                return set;
            }
        }

        private static bool IsConcreteFamily(string name) => ConcreteFamilies().Contains(name ?? "");

        private static bool IsLoadedTitleBlock(Document doc, string familyName)
        {
            if (doc == null || string.IsNullOrWhiteSpace(familyName)) return false;
            try
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Any(fs => string.Equals(fs.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        private static IEnumerable<string> CandidateRfaPaths(Document doc, string familyName)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string rel = Path.Combine("Families", "TitleBlocks", familyName + ".rfa");
            foreach (var baseDir in BaseDirs(doc))
            {
                if (string.IsNullOrEmpty(baseDir)) continue;
                var p = Path.Combine(baseDir, rel);
                if (seen.Add(p)) yield return p;
            }
        }

        private static IEnumerable<string> BaseDirs(Document doc)
        {
            string prjDir = null, asmDir = null;
            try { if (!string.IsNullOrEmpty(doc?.PathName)) prjDir = Path.GetDirectoryName(doc.PathName); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try { var asm = StingToolsApp.AssemblyPath; if (!string.IsNullOrEmpty(asm)) asmDir = Path.GetDirectoryName(asm); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            if (!string.IsNullOrEmpty(prjDir)) yield return prjDir;
            if (!string.IsNullOrEmpty(asmDir)) yield return asmDir;
        }

        private sealed class TbLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            { overwriteParameterValues = false; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Family; overwriteParameterValues = false; return true; }
        }
    }
}
