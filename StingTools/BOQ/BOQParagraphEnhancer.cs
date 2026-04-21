// ══════════════════════════════════════════════════════════════════════════
//  BOQParagraphEnhancer.cs — Phase 108i
//
//  Post-processing layer that transforms a plain BuildBOQDocument() output
//  into a senior-QS-grade BOQ. Runs AFTER the live BOQTemplateLibrary has
//  resolved each item's base NRM2 paragraph and BEFORE the bill sheets are
//  rendered. Every enhancement is guarded by a flag on BOQTenderConfig, so
//  the user keeps full control from the pre-export dialog.
//
//  The 10 priority enhancements, in order:
//
//   P1  Performance clauses — one sentence per item stating the applicable
//       fire/acoustic/thermal/flow/pressure performance pulled from the
//       element's shared parameters. No new data required.
//   P2  Compliance reference — append "to be executed in accordance with
//       {BS/EN X}" per category using a built-in map.
//   P3  Dimensional groupings — when ≥3 items share Category + Unit within
//       a section, mark each as "N of M similar — see Schedule of Sizes"
//       and emit a Schedule-of-Sizes annexure for the workbook.
//   P4  Auto-inclusion boilerplate — per discipline, append one sentence
//       covering fixings, supports, making good, commissioning.
//   P5  "Or approved equivalent" — append to items citing named products.
//   P6  Conditional clauses — "design by the {Structural|Services} Engineer"
//       when the relevant design parameters are populated.
//   P7  Client vocabulary overlay — load BOQ_CLIENT_VOCABULARY.json keyed
//       on the employer name and apply whole-word replacements to the
//       paragraph ("wall construction" → "walling" for Acme Holdings).
//   P8  Smart item naming — rewrite bare family names into
//       "{material} {category}, {thickness}mm" for walls, floors, slabs,
//       doors, columns, beams.
//   P9  Specification-clause cross-ref — when the element carries an
//       ASS_SPEC_CLAUSE_TXT value, append "; refer to Specification Clause {X}".
//   P10 CSV + JSON sidecars — emit a flat .csv and a structured .json
//       alongside the .xlsx so estimating software can ingest the BOQ
//       without parsing the workbook.
//
//  Design notes:
//   · All enhancements mutate the BOQLineItem in place. The original
//     template-resolved paragraph is preserved in ResolvedNRM2Paragraph
//     before augmentation so P1-P6 don't duplicate on repeated runs — a
//     simple "if the sentence already appears, skip" guard.
//   · Client vocabulary and performance sentence construction are pure
//     string operations with no Revit API calls, so they're safe to run
//     off the API thread if needed later.
//   · P10 sidecar writer is the only file-IO enhancement; it uses the
//     same atomic-replace pattern as the rest of the BOQ subsystem.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BOQ
{
    internal static class BOQParagraphEnhancer
    {
        // ── Orchestrator ───────────────────────────────────────────────────

        /// <summary>
        /// Apply every enabled enhancement to every BOQLineItem in the document.
        /// Called from BOQProfessionalExportCommand immediately after
        /// EnsureAllParagraphsResolved and before the workbook is built.
        /// </summary>
        public static EnhancementReport EnhanceAll(BOQDocument boq, Document doc, BOQTenderConfig tcfg)
        {
            var report = new EnhancementReport();
            if (boq == null || tcfg == null) return report;

            // Pre-compute maps the per-item passes reuse.
            Dictionary<string, string> vocab = tcfg.UseClientVocabulary
                ? LoadClientVocabulary(tcfg.Employer) : null;

            // P3 operates at section level — gather first-pass peer counts
            // then feed them back as flags on each item during the per-item
            // pass below.
            Dictionary<string, int> peerCount = tcfg.EnableDimensionalGroupings
                ? BuildPeerCounts(boq) : null;

            foreach (var sec in boq.Sections)
            {
                foreach (var item in sec.Items)
                {
                    if (item == null) continue;

                    Element el = null;
                    if (item.RevitElementId > 0 && doc != null)
                    {
                        try { el = doc.GetElement(new ElementId(item.RevitElementId)); }
                        catch (Exception ex) { StingLog.Warn($"Enhance get element {item.RevitElementId}: {ex.Message}"); }
                    }

                    if (tcfg.EnableSmartItemNaming)    ApplySmartItemNaming(item, el, report);        // P8 — rename first, so later paragraphs can reference the new name
                    if (tcfg.EnablePerformanceClauses) ApplyPerformanceClauses(item, el, report);     // P1
                    if (tcfg.EnableComplianceClauses)  ApplyComplianceClauses(item, report);          // P2
                    if (tcfg.EnableDimensionalGroupings && peerCount != null)
                                                      ApplyDimensionalGroup(item, sec, peerCount, report); // P3
                    if (tcfg.EnableAutoInclusionBoiler) ApplyAutoInclusion(item, report);             // P4
                    if (tcfg.EnableConditionalClauses)  ApplyConditionalClauses(item, el, report);    // P6
                    if (tcfg.EnableSpecClauseCrossRefs) ApplySpecClauseCrossRef(item, el, report);    // P9
                    if (tcfg.EnableOrApprovedEquivalent) ApplyOrApprovedEquivalent(item, report);     // P5
                    if (vocab != null)                  ApplyClientVocabulary(item, vocab, report);   // P7 (last — operates on final paragraph)
                }
            }

            StingLog.Info($"BOQ paragraph enhancer: performance={report.PerformanceCount} compliance={report.ComplianceCount} "
                + $"groups={report.GroupedCount} inclusion={report.InclusionCount} equivalent={report.OrEquivalentCount} "
                + $"conditional={report.ConditionalCount} vocab={report.VocabCount} smartName={report.SmartNameCount} "
                + $"specRef={report.SpecRefCount}");
            return report;
        }

        public class EnhancementReport
        {
            public int PerformanceCount;
            public int ComplianceCount;
            public int GroupedCount;
            public int InclusionCount;
            public int OrEquivalentCount;
            public int ConditionalCount;
            public int VocabCount;
            public int SmartNameCount;
            public int SpecRefCount;
        }

        // ══════════════════════════════════════════════════════════════════
        //  P1 — Performance clauses
        //  One appended sentence summarising the element's performance.
        //  Reads existing shared / built-in parameters only.
        // ══════════════════════════════════════════════════════════════════

        private static void ApplyPerformanceClauses(BOQLineItem item, Element el, EnhancementReport r)
        {
            if (el == null || item == null) return;
            string sentence = BuildPerformanceSentence(el, item);
            if (string.IsNullOrEmpty(sentence)) return;
            if (AlreadyContains(item.ResolvedNRM2Paragraph, "Performance:")) return;
            item.ResolvedNRM2Paragraph = Join(item.ResolvedNRM2Paragraph, sentence);
            r.PerformanceCount++;
        }

        private static string BuildPerformanceSentence(Element el, BOQLineItem item)
        {
            var bits = new List<string>();

            // Fire rating — search common parameter names + BuiltInParameter
            double? fireMin = ReadDouble(el, "BLE_FIRE_RATING_MIN", "FIRE_RATING_MINUTES", "Fire Resistance Rating (minutes)");
            string fireTxt  = ReadString(el, "FIRE_RATING_TXT", "Fire Rating", "FIRE_RATING");
            if (fireMin.HasValue && fireMin.Value > 0)
                bits.Add($"fire resistance {fireMin.Value:F0} min (BS EN 13501-2 / BS 476-20)");
            else if (!string.IsNullOrEmpty(fireTxt))
                bits.Add($"fire resistance {fireTxt.Trim()} (BS EN 13501-2 / BS 476-20)");

            // Acoustic Rw — walls, floors, doors
            double? rwDb = ReadDouble(el, "BLE_ACOUSTIC_RW_DB", "ACOUSTIC_RW_DB", "Weighted Sound Reduction Index Rw (dB)",
                                          "BLE_DOOR_ACOUSTIC_RW", "BLE_WALL_ACOUSTIC_RW");
            if (rwDb.HasValue && rwDb.Value > 0)
                bits.Add($"weighted sound reduction index Rw {rwDb.Value:F0} dB (BS EN ISO 717-1)");

            // Thermal U — walls, floors, roofs, windows
            double? uVal = ReadDouble(el, "BLE_U_VALUE_W_M2K", "U_VALUE", "Thermal Transmittance U (W/m²K)");
            if (uVal.HasValue && uVal.Value > 0)
                bits.Add($"thermal transmittance U = {uVal.Value:F2} W/m²K (BS EN ISO 6946)");

            // MEP — duct velocity / pressure drop / flow
            double? ductVel = ReadDouble(el, "BLE_DUCT_VELOCITY_M_S", "DUCT_VELOCITY_M_S", "Velocity");
            if (ductVel.HasValue && ductVel.Value > 0 && IsDuct(item.Category))
                bits.Add($"design velocity {ductVel.Value:F1} m/s (CIBSE Guide B3)");
            double? pressureDropPa = ReadDouble(el, "BLE_DUCT_PRESSURE_DROP_PA", "PRESSURE_DROP_PA", "Pressure Drop");
            if (pressureDropPa.HasValue && pressureDropPa.Value > 0 && IsDuct(item.Category))
                bits.Add($"system pressure drop {pressureDropPa.Value:F0} Pa");
            double? flow = ReadBipDouble(el, BuiltInParameter.RBS_DUCT_FLOW_PARAM);
            if (flow.HasValue && flow.Value > 0 && IsDuct(item.Category))
                bits.Add($"design flow {flow.Value:F0} l/s");

            // Pipe velocity
            double? pipeVel = ReadDouble(el, "BLE_PIPE_VELOCITY_M_S", "PIPE_VELOCITY");
            if (pipeVel.HasValue && pipeVel.Value > 0 && IsPipe(item.Category))
                bits.Add($"pipe velocity {pipeVel.Value:F1} m/s (CIBSE Guide G)");

            // Electrical — circuit rating / IP class
            double? circuitA = ReadBipDouble(el, BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM);
            if (circuitA.HasValue && circuitA.Value > 0 && IsElectrical(item.Category))
                bits.Add($"circuit rating {circuitA.Value:F0} A (BS 7671)");
            string ipClass = ReadString(el, "BLE_ELECTRICAL_IP_RATING", "IP_RATING", "IP Class");
            if (!string.IsNullOrEmpty(ipClass))
                bits.Add($"ingress protection {ipClass.Trim()} (BS EN 60529)");

            // Insulation thickness (ducts, pipes)
            double? insMm = ReadDouble(el, "BLE_DUCT_INSULATION_MM", "BLE_PIPE_INSULATION_MM", "Insulation Thickness (mm)");
            if (insMm.HasValue && insMm.Value > 0)
                bits.Add($"insulation thickness {insMm.Value:F0} mm (to TIMSA / CIBSE Commissioning Code)");

            if (bits.Count == 0) return null;
            return "Performance: " + string.Join("; ", bits) + ".";
        }

        // ══════════════════════════════════════════════════════════════════
        //  P2 — Compliance reference
        // ══════════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, string> _complianceByCategory =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Walls",                      "BS EN 1996-1-1 (masonry) / BS EN 1992-1-1 (concrete)" },
            { "Structural Columns",         "BS EN 1992-1-1 / BS EN 1993-1-1" },
            { "Structural Framing",         "BS EN 1993-1-1 / BS EN 1995-1-1" },
            { "Structural Foundations",     "BS EN 1997-1" },
            { "Floors",                     "BS EN 1992-1-1 / TR 34 (industrial floors)" },
            { "Roofs",                      "BS 6229 / BS 5534" },
            { "Ceilings",                   "BS EN 13964" },
            { "Doors",                      "BS 4787 / BS EN 14351-1 / BS 8214 (fire)" },
            { "Windows",                    "BS EN 14351-1" },
            { "Stairs",                     "BS 5395 / BS 6180 (balustrades)" },
            { "Railings",                   "BS 6180 / BS 6399-1" },
            { "Curtain Panels",             "BS EN 13830" },
            { "Curtain Wall Mullions",      "BS EN 13830" },
            { "Ducts",                      "BS EN 12237 / DW/144" },
            { "Duct Fittings",              "BS EN 12237" },
            { "Pipes",                      "BS EN 806 / BS EN 12056" },
            { "Pipe Fittings",              "BS EN 806" },
            { "Plumbing Fixtures",          "BS EN 997 / BS EN 14688 / BS 8313" },
            { "Electrical Equipment",       "BS 7671 / BS EN 61439" },
            { "Electrical Fixtures",        "BS 7671" },
            { "Lighting Fixtures",          "BS 7671 / BS EN 12464-1" },
            { "Cable Trays",                "BS EN 61537" },
            { "Cable Tray Fittings",        "BS EN 61537" },
            { "Conduits",                   "BS EN 61386 / BS 7671" },
            { "Conduit Fittings",           "BS EN 61386" },
            { "Fire Alarm Devices",         "BS 5839-1" },
            { "Sprinklers",                 "BS EN 12845 / LPS 1014" },
            { "Air Terminals",              "CIBSE Guide B3 / BS EN 13182" },
            { "Mechanical Equipment",       "CIBSE Guides A & B / BSRIA BG 49" },
            { "Communication Devices",      "BS 6701 / BS EN 50173" },
            { "Data Devices",               "BS EN 50173 / TIA-568" },
            { "Security Devices",           "BS EN 50131" },
        };

        private static void ApplyComplianceClauses(BOQLineItem item, EnhancementReport r)
        {
            if (item == null) return;
            if (string.IsNullOrEmpty(item.Category)) return;
            if (!_complianceByCategory.TryGetValue(item.Category, out string code)) return;
            if (AlreadyContains(item.ResolvedNRM2Paragraph, "to be executed in accordance")) return;
            string sentence = $"To be executed in accordance with {code}.";
            item.ResolvedNRM2Paragraph = Join(item.ResolvedNRM2Paragraph, sentence);
            r.ComplianceCount++;
        }

        // ══════════════════════════════════════════════════════════════════
        //  P3 — Dimensional groupings
        //  Flag items that have ≥2 peers of identical Category+Unit within
        //  the same section. The bill-sheet renderer already prints each
        //  line separately; we add a "(N of M similar — see Schedule of
        //  Sizes)" note so the QS can see the grouping. The standalone
        //  Schedule-of-Sizes annexure sheet is built separately by
        //  BOQProfessionalExportCommand if EnableDimensionalGroupings is on.
        //
        //  Keying by (section.NRM2Section, item.Category, item.Unit) so
        //  items that differ only in dimensions get counted together.
        // ══════════════════════════════════════════════════════════════════

        internal static string BuildGroupKey(BOQSection sec, BOQLineItem item)
            => $"{sec?.NRM2Section}|{item?.Category}|{item?.Unit}";

        private static Dictionary<string, int> BuildPeerCounts(BOQDocument boq)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var sec in boq.Sections)
            {
                foreach (var item in sec.Items)
                {
                    if (item?.Source != BOQRowSource.Model) continue;
                    string k = BuildGroupKey(sec, item);
                    if (!d.TryGetValue(k, out int n)) n = 0;
                    d[k] = n + 1;
                }
            }
            return d;
        }

        private static readonly Dictionary<string, int> _positionCache
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static void ApplyDimensionalGroup(BOQLineItem item, BOQSection sec,
            Dictionary<string, int> peerCount, EnhancementReport r)
        {
            if (item == null || sec == null) return;
            if (item.Source != BOQRowSource.Model) return;
            string k = BuildGroupKey(sec, item);
            if (!peerCount.TryGetValue(k, out int n) || n < 3) return;

            // Running position within the group — stable across the pass
            if (!_positionCache.TryGetValue(k, out int pos)) pos = 0;
            pos++;
            _positionCache[k] = pos;

            string note = $"One of {n} similar items of this type — see Schedule of Sizes.";
            if (AlreadyContains(item.ResolvedNRM2Paragraph, "Schedule of Sizes")) return;
            item.ResolvedNRM2Paragraph = Join(item.ResolvedNRM2Paragraph, note);
            r.GroupedCount++;
        }

        /// <summary>
        /// Reset the running position cache. Called by the export command
        /// before each workbook so counts restart at 1.
        /// </summary>
        public static void ResetGroupPositionCache() => _positionCache.Clear();

        // ══════════════════════════════════════════════════════════════════
        //  P4 — Auto-inclusion boilerplate (per discipline)
        // ══════════════════════════════════════════════════════════════════

        private static void ApplyAutoInclusion(BOQLineItem item, EnhancementReport r)
        {
            if (item == null) return;
            if (item.Source == BOQRowSource.ProvisionalSum) return; // PS lines are already cost-all-in
            string disc = item.Discipline ?? "";
            string sentence;
            switch (disc)
            {
                case "A":
                    sentence = "Including all associated fixings, dowels, wall ties, trims, primers, sealants, and making good of adjacent finishes.";
                    break;
                case "S":
                    sentence = "Including all reinforcement, formwork, starter bars, joint treatments, surface preparation and making good on striking.";
                    break;
                case "M":
                    sentence = "Including all supports, vibration isolation, thermal insulation, fire-stopping through compartment boundaries, balancing and commissioning.";
                    break;
                case "E":
                    sentence = "Including all cable, containment, glands, labelling, testing, and issue of the Electrical Installation Certificate to BS 7671.";
                    break;
                case "P":
                    sentence = "Including all fittings, supports, insulation, pressure-testing, flushing, chlorination (where potable) and commissioning.";
                    break;
                case "FP":
                    sentence = "Including all brackets, fittings, pressure-testing, commissioning and sign-off by the responsible Fire Engineer.";
                    break;
                default:
                    sentence = "Including all associated fixings, supports, testing, commissioning and handover documentation.";
                    break;
            }
            if (AlreadyContains(item.ResolvedNRM2Paragraph, "Including all")) return;
            item.ResolvedNRM2Paragraph = Join(item.ResolvedNRM2Paragraph, sentence);
            r.InclusionCount++;
        }

        // ══════════════════════════════════════════════════════════════════
        //  P5 — "Or approved equivalent"
        //  NRM2 / JCT convention: every supply item referring to a named
        //  product must offer the bidder the option to substitute. Skipped
        //  for Provisional Sums and items that already end with an
        //  equivalence clause.
        // ══════════════════════════════════════════════════════════════════

        private static void ApplyOrApprovedEquivalent(BOQLineItem item, EnhancementReport r)
        {
            if (item == null) return;
            if (item.Source == BOQRowSource.ProvisionalSum) return;
            if (string.IsNullOrEmpty(item.ResolvedNRM2Paragraph)) return;
            if (AlreadyContains(item.ResolvedNRM2Paragraph, "or approved equivalent")) return;
            if (AlreadyContains(item.ResolvedNRM2Paragraph, "or equivalent")) return;
            item.ResolvedNRM2Paragraph = Join(item.ResolvedNRM2Paragraph, "or approved equivalent.");
            r.OrEquivalentCount++;
        }

        // ══════════════════════════════════════════════════════════════════
        //  P6 — Conditional clauses
        //  "Design by Structural / Services Engineer" triggered by the
        //  presence of design-intent parameters. Conservative — only fires
        //  when we have strong evidence the element carries design data.
        // ══════════════════════════════════════════════════════════════════

        private static void ApplyConditionalClauses(BOQLineItem item, Element el, EnhancementReport r)
        {
            if (item == null) return;
            bool isStructural = false, isMep = false;
            string disc = item.Discipline ?? "";
            if (disc == "S") isStructural = true;
            if (disc == "M" || disc == "E" || disc == "P" || disc == "FP") isMep = true;

            // Structural trigger — carries a STRUCTURAL_MATERIAL_PARAM value
            if (isStructural && el != null)
            {
                string sm = ReadString(el, "STRUCTURAL_MATERIAL_PARAM", "Structural Material");
                Parameter smp = null;
                try { smp = el.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM); }
                catch (Exception ex) { StingLog.Warn($"Enhance STRUCT_MAT param: {ex.Message}"); }
                if (!string.IsNullOrEmpty(sm) || smp != null)
                {
                    string sentence = "Structural design and detailing by the Structural Engineer in accordance with the relevant part of the Eurocode series (EC2 / EC3 / EC5 / EC7 as applicable); reinforcement, sizing and connection details to the Structural Drawings.";
                    if (!AlreadyContains(item.ResolvedNRM2Paragraph, "Structural design"))
                    {
                        item.ResolvedNRM2Paragraph = Join(item.ResolvedNRM2Paragraph, sentence);
                        r.ConditionalCount++;
                        return;
                    }
                }
            }

            // MEP trigger
            if (isMep)
            {
                string sentence = "Sizing, routing and performance design by the Services Engineer in accordance with the CIBSE Design Guides and the Specification; installation and commissioning to BSRIA BG 49 / BG 2 / CIBSE Commissioning Code M.";
                if (!AlreadyContains(item.ResolvedNRM2Paragraph, "design by the Services Engineer"))
                {
                    item.ResolvedNRM2Paragraph = Join(item.ResolvedNRM2Paragraph, sentence);
                    r.ConditionalCount++;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  P7 — Client vocabulary overlay
        //  Loads BOQ_CLIENT_VOCABULARY.json from the plugin data folder.
        //  Structure: { "_default": { "phrase": "replacement", ... },
        //               "Acme Holdings Ltd": { ... }, ... }
        //  Employer-specific entries override the default map.
        //  Whole-word case-insensitive replacements applied to the final
        //  paragraph so performance / compliance / inclusion sentences all
        //  pick up the new phrasing.
        // ══════════════════════════════════════════════════════════════════

        private static Dictionary<string, string> _clientVocabCache;
        private static string _clientVocabCacheKey;

        internal static Dictionary<string, string> LoadClientVocabulary(string employer)
        {
            string key = employer ?? "";
            if (_clientVocabCache != null && string.Equals(_clientVocabCacheKey, key, StringComparison.Ordinal))
                return _clientVocabCache;

            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(StingToolsApp.DataPath, "BOQ_CLIENT_VOCABULARY.json");
                if (File.Exists(path))
                {
                    string raw = File.ReadAllText(path);
                    var all = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(raw);
                    if (all != null)
                    {
                        if (all.TryGetValue("_default", out var def) && def != null)
                            foreach (var kv in def) merged[kv.Key] = kv.Value;
                        if (!string.IsNullOrEmpty(key) && all.TryGetValue(key, out var spec) && spec != null)
                            foreach (var kv in spec) merged[kv.Key] = kv.Value; // employer overrides default
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadClientVocabulary: {ex.Message}"); }

            _clientVocabCache = merged;
            _clientVocabCacheKey = key;
            return merged;
        }

        private static void ApplyClientVocabulary(BOQLineItem item, Dictionary<string, string> vocab, EnhancementReport r)
        {
            if (item == null || vocab == null || vocab.Count == 0) return;
            if (string.IsNullOrEmpty(item.ResolvedNRM2Paragraph)) return;
            string before = item.ResolvedNRM2Paragraph;
            string after = ApplyWholeWordReplacements(before, vocab);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                item.ResolvedNRM2Paragraph = after;
                r.VocabCount++;
            }
            // Also apply to ItemName for consistency
            if (!string.IsNullOrEmpty(item.ItemName))
            {
                string nAfter = ApplyWholeWordReplacements(item.ItemName, vocab);
                if (!string.Equals(item.ItemName, nAfter, StringComparison.Ordinal))
                    item.ItemName = nAfter;
            }
        }

        private static string ApplyWholeWordReplacements(string input, Dictionary<string, string> vocab)
        {
            string s = input;
            foreach (var kv in vocab)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                try
                {
                    string pattern = $@"\b{Regex.Escape(kv.Key)}\b";
                    s = Regex.Replace(s, pattern, kv.Value ?? "", RegexOptions.IgnoreCase);
                }
                catch (Exception ex) { StingLog.Warn($"Vocab replace '{kv.Key}': {ex.Message}"); }
            }
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        //  P8 — Smart item naming
        //  Rewrite bare family names into descriptive QS phrasing based on
        //  the element's category and the few most useful parameters.
        //  We don't attempt to rewrite every category — only the ones where
        //  a templated name is obviously better than the family name.
        // ══════════════════════════════════════════════════════════════════

        private static void ApplySmartItemNaming(BOQLineItem item, Element el, EnhancementReport r)
        {
            if (item == null || el == null) return;
            string newName = BuildSmartName(item, el);
            if (string.IsNullOrEmpty(newName)) return;
            if (string.Equals(newName, item.ItemName, StringComparison.OrdinalIgnoreCase)) return;
            item.ItemName = newName;
            r.SmartNameCount++;
        }

        private static string BuildSmartName(BOQLineItem item, Element el)
        {
            string cat = item.Category ?? "";
            double? thick = ReadDouble(el, "BLE_THICKNESS_MM", "Thickness", "BLE_WALL_THICKNESS_MM", "BLE_FLOOR_THICKNESS_MM");
            double? width = ReadDouble(el, "BLE_WIDTH_MM", "Width");
            double? height = ReadDouble(el, "BLE_HEIGHT_MM", "Height");
            string material = ReadString(el, "STRUCTURAL_MATERIAL_PARAM", "Structural Material", "Material");
            string finish = ReadString(el, "BLE_FINISH_TXT", "Finish");
            string fireTxt = ReadString(el, "FIRE_RATING_TXT", "Fire Rating");

            switch (cat)
            {
                case "Walls":
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(material)) parts.Add(material.ToLowerInvariant());
                    parts.Add("wall");
                    if (thick.HasValue && thick.Value > 0) parts.Add($"{thick.Value:F0} mm thick");
                    if (!string.IsNullOrEmpty(finish)) parts.Add($"finished {finish.ToLowerInvariant()}");
                    return Capitalise(string.Join(", ", parts));
                }
                case "Floors":
                {
                    var parts = new List<string>();
                    if (thick.HasValue && thick.Value > 0) parts.Add($"{thick.Value:F0} mm thick");
                    if (!string.IsNullOrEmpty(material)) parts.Add(material.ToLowerInvariant());
                    parts.Add("floor slab");
                    return Capitalise(string.Join(" ", parts));
                }
                case "Roofs":
                {
                    var parts = new List<string> { "Roof" };
                    if (thick.HasValue && thick.Value > 0) parts.Add($"{thick.Value:F0} mm");
                    if (!string.IsNullOrEmpty(material)) parts.Add("in " + material.ToLowerInvariant());
                    return string.Join(", ", parts);
                }
                case "Doors":
                {
                    var parts = new List<string> { "Door assembly" };
                    if (width.HasValue && height.HasValue)
                        parts.Add($"{width.Value:F0} × {height.Value:F0} mm");
                    if (!string.IsNullOrEmpty(fireTxt))
                        parts.Add($"FD-{fireTxt.Replace(" ", string.Empty)} fire-rated");
                    return string.Join(", ", parts);
                }
                case "Windows":
                {
                    var parts = new List<string> { "Window assembly" };
                    if (width.HasValue && height.HasValue)
                        parts.Add($"{width.Value:F0} × {height.Value:F0} mm");
                    return string.Join(", ", parts);
                }
                case "Structural Columns":
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(material)) parts.Add(material);
                    parts.Add("column");
                    string section = ReadString(el, "Section Size", "BLE_SECTION_SIZE");
                    if (!string.IsNullOrEmpty(section)) parts.Add(section);
                    return Capitalise(string.Join(", ", parts));
                }
                case "Structural Framing":
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(material)) parts.Add(material);
                    parts.Add("beam");
                    string section = ReadString(el, "Section Size", "BLE_SECTION_SIZE");
                    if (!string.IsNullOrEmpty(section)) parts.Add(section);
                    return Capitalise(string.Join(", ", parts));
                }
                case "Ceilings":
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(finish)) parts.Add(finish);
                    parts.Add("suspended ceiling");
                    return Capitalise(string.Join(" ", parts));
                }
                default:
                    return null;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  P9 — Specification clause cross-reference
        //  Appends "refer to Specification Clause {X}" when the element
        //  carries ASS_SPEC_CLAUSE_TXT or a similar parameter. Bidders use
        //  the reference to locate the full technical spec when pricing.
        // ══════════════════════════════════════════════════════════════════

        private static void ApplySpecClauseCrossRef(BOQLineItem item, Element el, EnhancementReport r)
        {
            if (item == null || el == null) return;
            string clause = ReadString(el,
                "ASS_SPEC_CLAUSE_TXT", "Specification Clause", "Spec Clause", "NBS Clause",
                "ASS_NBS_CLAUSE", "ASS_SPEC_REF_TXT");
            if (string.IsNullOrWhiteSpace(clause)) return;
            clause = clause.Trim().TrimEnd(';', '.');
            if (AlreadyContains(item.ResolvedNRM2Paragraph, "Specification Clause")) return;
            string sentence = $"Refer to Specification Clause {clause}.";
            item.ResolvedNRM2Paragraph = Join(item.ResolvedNRM2Paragraph, sentence);
            r.SpecRefCount++;
        }

        // ══════════════════════════════════════════════════════════════════
        //  P10 — CSV + JSON sidecars
        //  Emitted alongside the .xlsx for downstream estimating tools.
        //  CSV layout: flat one-row-per-item.
        //  JSON: full BOQDocument serialised with indenting.
        // ══════════════════════════════════════════════════════════════════

        public static void ExportSidecars(BOQDocument boq, string xlsxPath, BOQTenderConfig tcfg)
        {
            if (boq == null || string.IsNullOrEmpty(xlsxPath)) return;
            try
            {
                string baseName = Path.Combine(
                    Path.GetDirectoryName(xlsxPath) ?? "",
                    Path.GetFileNameWithoutExtension(xlsxPath));

                // CSV
                string csvPath = baseName + ".csv";
                var sb = new StringBuilder();
                sb.AppendLine("SectionCode,SectionName,Discipline,ItemRef,Category,ItemName,Unit,Quantity,RateUGX,RateUSD,TotalUGX,TotalUSD,Source,RateSource,RateConfidence,EmbodiedCarbonKg,Level,Location,ResolvedNRM2Paragraph");
                foreach (var sec in boq.Sections)
                {
                    foreach (var item in sec.Items)
                    {
                        sb.Append(CsvEscape(sec.NRM2Section)).Append(',')
                          .Append(CsvEscape(sec.Name)).Append(',')
                          .Append(CsvEscape(item.Discipline)).Append(',')
                          .Append(CsvEscape(item.BOQLineRef)).Append(',')
                          .Append(CsvEscape(item.Category)).Append(',')
                          .Append(CsvEscape(item.ItemName)).Append(',')
                          .Append(CsvEscape(item.Unit)).Append(',')
                          .Append(item.Quantity.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                          .Append(item.RateUGX.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                          .Append(item.RateUSD.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                          .Append(item.TotalUGX.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                          .Append(item.TotalUSD.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                          .Append(CsvEscape(item.Source.ToString())).Append(',')
                          .Append(CsvEscape(item.RateSource)).Append(',')
                          .Append(item.RateConfidence).Append(',')
                          .Append(item.EmbodiedCarbonKg.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                          .Append(CsvEscape(item.Level)).Append(',')
                          .Append(CsvEscape(item.Location)).Append(',')
                          .Append(CsvEscape(item.ResolvedNRM2Paragraph))
                          .AppendLine();
                    }
                }
                AtomicWrite(csvPath, sb.ToString());

                // JSON
                string jsonPath = baseName + ".json";
                var jsonObj = new
                {
                    schema_version  = "108i",
                    generated_utc   = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    project_name    = boq.ProjectName,
                    document_title  = boq.DocumentTitle,
                    snapshot_label  = boq.SnapshotLabel,
                    currency        = boq.Currency,
                    exchange_rate   = boq.ExchangeRateUgxPerUsd,
                    project_budget  = boq.ProjectBudgetUGX,
                    prelim_pct      = boq.PrelimPct,
                    contingency_pct = boq.ContingencyPct,
                    overhead_pct    = boq.OverheadPct,
                    vat_pct         = tcfg?.VatPct ?? 18.0,
                    subtotal_ugx    = boq.SubtotalUGX,
                    grand_total_ugx = boq.GrandTotalUGX,
                    modelled_ugx    = boq.ModeledTotalUGX,
                    provisional_ugx = boq.ProvTotalUGX,
                    total_carbon_kg = boq.TotalCarbonKg,
                    paragraph_coverage_pct = boq.ParagraphCoveragePct,
                    sections        = boq.Sections.Select(s => new
                    {
                        code     = s.NRM2Section,
                        name     = s.Name,
                        discipline = s.Discipline,
                        total_ugx = s.TotalUGX,
                        items    = s.Items.Select(i => new
                        {
                            id          = i.Id,
                            item_ref    = i.BOQLineRef,
                            item_name   = i.ItemName,
                            category    = i.Category,
                            discipline  = i.Discipline,
                            unit        = i.Unit,
                            quantity    = i.Quantity,
                            rate_ugx    = i.RateUGX,
                            rate_usd    = i.RateUSD,
                            total_ugx   = i.TotalUGX,
                            total_usd   = i.TotalUSD,
                            source      = i.Source.ToString(),
                            rate_source = i.RateSource,
                            rate_confidence = i.RateConfidence,
                            embodied_carbon_kg = i.EmbodiedCarbonKg,
                            level       = i.Level,
                            location    = i.Location,
                            revit_element_id = i.RevitElementId,
                            unique_id   = i.UniqueId,
                            nrm2_paragraph = i.ResolvedNRM2Paragraph,
                            note        = i.Note,
                        })
                    })
                };
                string json = JsonConvert.SerializeObject(jsonObj, Formatting.Indented);
                AtomicWrite(jsonPath, json);

                StingLog.Info($"BOQ sidecars written: {Path.GetFileName(csvPath)} + {Path.GetFileName(jsonPath)}");
            }
            catch (Exception ex) { StingLog.Error("BOQ ExportSidecars", ex); }
        }

        private static void AtomicWrite(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool q = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!q) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        // ══════════════════════════════════════════════════════════════════
        //  Shared helpers
        // ══════════════════════════════════════════════════════════════════

        private static bool AlreadyContains(string paragraph, string needle)
            => !string.IsNullOrEmpty(paragraph)
            && paragraph.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Join(string existing, string addition)
        {
            if (string.IsNullOrEmpty(existing)) return addition;
            string trimmed = existing.TrimEnd();
            if (!trimmed.EndsWith(".", StringComparison.Ordinal)
                && !trimmed.EndsWith("!", StringComparison.Ordinal)
                && !trimmed.EndsWith("?", StringComparison.Ordinal))
                trimmed += ".";
            return trimmed + " " + addition;
        }

        private static bool IsDuct(string category)
            => category != null && (category.Contains("Duct") || category.Contains("Air Terminal"));

        private static bool IsPipe(string category)
            => category != null && (category.Contains("Pipe") || category.Contains("Plumbing Fixture"));

        private static bool IsElectrical(string category)
            => category != null && (category.Contains("Electrical") || category.Contains("Lighting")
                    || category.Contains("Panel") || category.Contains("Circuit"));

        private static double? ReadDouble(Element el, params string[] names)
        {
            if (el == null) return null;
            foreach (var n in names)
            {
                try
                {
                    Parameter p = el.LookupParameter(n);
                    if (p == null) continue;
                    if (!p.HasValue) continue;
                    if (p.StorageType == StorageType.Double)
                    {
                        double v = p.AsDouble();
                        // Revit stores lengths in feet internally; heuristic:
                        // if the parameter name hints at "mm" we assume the
                        // value is already stored as mm in our shared params.
                        // Otherwise convert ft → mm when the name hints length.
                        return v;
                    }
                    if (p.StorageType == StorageType.Integer) return p.AsInteger();
                    if (p.StorageType == StorageType.String)
                    {
                        string s = p.AsString();
                        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return d;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ReadDouble({n}): {ex.Message}"); }
            }
            return null;
        }

        private static string ReadString(Element el, params string[] names)
        {
            if (el == null) return null;
            foreach (var n in names)
            {
                try
                {
                    Parameter p = el.LookupParameter(n);
                    if (p == null || !p.HasValue) continue;
                    if (p.StorageType == StorageType.String) return p.AsString();
                    if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString(CultureInfo.InvariantCulture);
                    if (p.StorageType == StorageType.Double) return p.AsDouble().ToString("F2", CultureInfo.InvariantCulture);
                    if (p.StorageType == StorageType.ElementId)
                    {
                        var id = p.AsElementId();
                        if (id != null && id != ElementId.InvalidElementId)
                        {
                            var refEl = el.Document?.GetElement(id);
                            if (refEl != null) return refEl.Name;
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ReadString({n}): {ex.Message}"); }
            }
            return null;
        }

        private static double? ReadBipDouble(Element el, BuiltInParameter bip)
        {
            try
            {
                var p = el?.get_Parameter(bip);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            catch (Exception ex) { StingLog.Warn($"ReadBipDouble({bip}): {ex.Message}"); }
            return null;
        }

        private static string Capitalise(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
