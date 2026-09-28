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
//   * paperSize     → any size STING_TITLE_BLOCKS.json declares (A0-A3);
//                     blank / unsupported sizes are REPORTED, never guessed
//   * orientation   → Portrait inserts "_PORT"
//   * BIM mode      → PRJ_SHEET_BIM_MODE_TXT on ProjectInformation,
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
// T-6: the decision itself lives in the Revit-free TitleBlockFamilyNaming
// (table-tested); this class only supplies the catalogue, the loaded-family
// check and the BIM mode, and reports the warnings.
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
        /// returns the input unchanged when it can't resolve the name, and
        /// logs why. Callers that can show the operator a message should use
        /// <see cref="Resolve"/> and surface its warnings.
        /// </summary>
        public static string ToConcreteFamily(Document doc, DrawingType dt, string declaredFamily)
        {
            var res = Resolve(doc, dt, declaredFamily);
            foreach (var w in res.Warnings)
                StingLog.Warn($"TitleBlockResolver [{dt?.Id}]: {w}");
            return res.IsResolved ? res.Family : declaredFamily;
        }

        /// <summary>
        /// Full resolution: concrete family (null when unresolved) plus the
        /// warnings an operator needs to see (paper blank, size unsupported,
        /// presentation variant missing, name/paper mismatch).
        /// </summary>
        public static TitleBlockResolution Resolve(Document doc, DrawingType dt, string declaredFamily)
        {
            try
            {
                return TitleBlockFamilyNaming.Resolve(
                    declaredFamily, dt?.PaperSize, dt?.Orientation, ResolveMode(doc, dt),
                    ConcreteFamilies(), n => IsLoadedTitleBlock(doc, n));
            }
            catch (Exception ex)
            {
                var r = new TitleBlockResolution();
                r.Warnings.Add($"TitleBlockResolver.Resolve('{declaredFamily}'): {ex.Message}");
                return r;
            }
        }

        /// <summary>
        /// Resolve the BIM mode ("BIM" / "NONBIM") for a drawing type. Reads
        /// PRJ_SHEET_BIM_MODE_TXT from ProjectInformation when bound;
        /// defaults to "BIM".
        /// </summary>
        public static string ResolveMode(Document doc, DrawingType dt)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                var p = pi?.LookupParameter("PRJ_SHEET_BIM_MODE_TXT");
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
                    if (doc.LoadFamily(path, new TitleBlockLoadOptions(), out Family fam) && fam != null)
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

        /// <summary>Read-only provisioning check (no transaction needed): is the
        /// concrete family present — either already loaded in the document, or a
        /// built .rfa on disk under Families/TitleBlocks/ that the producer would
        /// lazy-load on demand? Used by DrawingTypeValidator to distinguish
        /// "not built" (needs TitleBlock_CreateAll) from "built but not loaded".</summary>
        public static bool IsProvisioned(Document doc, string familyName)
            => IsLoadedTitleBlock(doc, familyName) || BuiltRfaExists(doc, familyName);

        /// <summary>True when a built .rfa for the family exists on disk.</summary>
        public static bool BuiltRfaExists(Document doc, string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return false;
            foreach (var p in CandidateRfaPaths(doc, familyName))
            {
                try { if (File.Exists(p)) return true; }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            return false;
        }

        // ── Internals ───────────────────────────────────────────────────

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
    }

    /// <summary>
    /// T-6: the ONE IFamilyLoadOptions for STING title blocks. There were two —
    /// TbLoadOptions here (overwriteParameterValues = false) and
    /// TitleBlockFamilyLoadOptions in TitleBlockSlotCommands (true) — so the
    /// same family reload kept or reset type values depending on which
    /// command happened to load it.
    /// <para>
    /// false is the correct semantics. Every caller loads a title block only
    /// when it is NOT already loaded (provisioning), so OnFamilyFound is
    /// reached only on a race or a same-name family from another path. In
    /// that case the family definition (geometry, labels, new parameters)
    /// still updates, but the project's existing TYPE parameter values are
    /// kept: a title block is on issued sheets, and silently resetting values
    /// a project set on its types is a change nobody asked for. Instance
    /// cells are never affected either way — TitleBlockParamApplier owns
    /// those. A deliberate "push corporate defaults onto types" is a separate,
    /// explicit operation, not a side effect of loading.
    /// </para>
    /// </summary>
    public sealed class TitleBlockLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        { overwriteParameterValues = false; return true; }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
            out FamilySource source, out bool overwriteParameterValues)
        { source = FamilySource.Family; overwriteParameterValues = false; return true; }
    }
}
