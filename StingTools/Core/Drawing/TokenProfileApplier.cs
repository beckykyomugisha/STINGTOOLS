// StingTools — Drawing Template Manager · Phase 135
//
// TokenProfileApplier translates an AnnotationTokenProfile (per
// DrawingType) and the resolved ViewStylePack's tag-appearance
// defaults into Revit parameter writes on the view, the elements
// in the view, and the corresponding family types.
//
// Pipeline ordering: this runs as Step 7.5 in
// DrawingTypePresentation.Apply — between the ViewStylePack apply
// (step 7) and the AnnotationRunner (step 8). Running before the
// annotation pass means any auto-tags it creates inherit the
// already-active style preset and depth tier.
//
// Step layout:
//   A. STING_VIEW_TAG_STYLE — view-level colour scheme route
//   B. PARA_STATE_1..10 + per-category depth — paragraph tiers
//   C. TAG_7_SECTION_VISIBLE_A..F — TAG7 sub-section visibility
//   D. TAG_{size}{style}_{color}_BOOL — tag style preset matrix
//   E. TAG_SEG_MASK_TXT — 8-segment mask
//   F. STING_DISPLAY_MODE — display mode integer
//   G. Presentation-mode preset — global PARA_STATE_* + WARN_VISIBLE
//
// Inheritance: profile fields on the DrawingType always win over
// pack fields, and pack fields win over the no-op default.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Tags;

namespace StingTools.Core.Drawing
{
    public static class TokenProfileApplier
    {
        public sealed class ApplyResult
        {
            public int  ViewParamWrites    { get; set; }
            public int  ElementWrites      { get; set; }
            public int  TypeWrites         { get; set; }
            public bool PresentationApplied { get; set; }
            public List<string> Warnings    { get; } = new List<string>();
        }

        /// <summary>
        /// Apply the merged AnnotationTokenProfile (DrawingType) +
        /// ViewStylePack tag-appearance defaults to the given view.
        /// All Revit writes happen inside the caller's transaction.
        /// Returns the number of writes per scope.
        /// </summary>
        public static ApplyResult Apply(Document doc, View view, DrawingType dt, ViewStylePack pack)
        {
            var r = new ApplyResult();
            if (doc == null || view == null || dt == null) return r;
            if (view.IsTemplate) return r;

            // Resolve effective values: profile > pack > null
            var profile  = dt.TokenProfile;
            string scheme   = profile?.ColorScheme ?? pack?.TagColorScheme;
            string size     = profile?.TagSize;
            string style    = profile?.TagStyle;
            string colour   = profile?.TagColor;
            int?   depth    = profile?.ParaDepth;
            string preset   = profile?.PresentationMode;
            string segMask  = profile?.SegmentMask;
            int?   dispMode = profile?.DisplayMode;
            var    sectVis  = profile?.SectionVisibility;
            var    catDeps  = profile?.CategoryDepths;
            // Phase 165 — T4-T10 payload pattern mode (HANDOVER / DC / CUSTOM).
            string patternMode = profile?.PatternMode;

            try
            {
                // ── Step A. View-level colour scheme route ─────────────
                if (!string.IsNullOrWhiteSpace(scheme))
                {
                    if (TryWriteViewParam(view, ParamRegistry.VIEW_TAG_STYLE, scheme))
                        r.ViewParamWrites++;
                }

                // ── Step E. Segment mask (view-level) ──────────────────
                if (!string.IsNullOrWhiteSpace(segMask))
                {
                    if (segMask.Length == 8 && segMask.All(c => c == '0' || c == '1'))
                    {
                        if (TryWriteViewParam(view, ParamRegistry.TAG_SEG_MASK, segMask))
                            r.ViewParamWrites++;
                    }
                    else
                    {
                        r.Warnings.Add($"SegmentMask '{segMask}' must be 8 chars of 0/1; ignored.");
                    }
                }

                // ── Steps F + C: per-element writes in a SINGLE pass ──
                // PERF-03: previously the display-mode write, the section-
                // visibility write, and the per-category depth write each
                // ran their own FilteredElementCollector. Merge them into
                // one collector pass so a 500-element view doesn't pay the
                // 3× scan cost.
                bool needDispMode = dispMode.HasValue && dispMode.Value >= 1 && dispMode.Value <= 5;
                bool needSectVis  = sectVis != null && sectVis.Count > 0;
                if (needDispMode || needSectVis)
                {
                    var canonicalSectVis = needSectVis
                        ? CanonicaliseSectionVisibility(sectVis)
                        : null;
                    var ids = new FilteredElementCollector(doc, view.Id)
                        .WhereElementIsNotElementType()
                        .ToElementIds();
                    foreach (var id in ids)
                    {
                        var el = doc.GetElement(id);
                        if (el == null) continue;
                        if (needDispMode)
                        {
                            try
                            {
                                Parameter p = el.LookupParameter(ParamRegistry.DISPLAY_MODE);
                                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Integer
                                    && p.AsInteger() != dispMode.Value)
                                {
                                    p.Set(dispMode.Value); r.ElementWrites++;
                                }
                            }
                            catch { /* element-level failure — keep going */ }
                        }
                        if (needSectVis && canonicalSectVis != null)
                        {
                            foreach (var kv in canonicalSectVis)
                            {
                                string pname = $"TAG_7_SECTION_VISIBLE_{kv.Key}_BOOL";
                                if (el.LookupParameter(pname) == null) continue;
                                if (ParameterHelpers.SetYesNo(el, pname, kv.Value, overwrite: true))
                                    r.ElementWrites++;
                            }
                        }
                    }
                }

                // ── Step G. Presentation-mode preset (global tier set) ─
                if (!string.IsNullOrWhiteSpace(preset))
                {
                    bool ok = ApplyPresentationPreset(doc, preset);
                    if (ok)
                    {
                        r.PresentationApplied = true;
                        r.TypeWrites += 1;
                    }
                    else r.Warnings.Add($"Unknown presentation mode '{preset}'.");
                }

                // ── Step B. Global paragraph depth (project types) ─────
                // Only fire when no preset specified — preset already
                // sets PARA_STATE_*, so doing both would race.
                if (string.IsNullOrWhiteSpace(preset) && depth.HasValue
                    && depth.Value >= 1 && depth.Value <= 10)
                {
                    int n = TagStyleEngine.SetParagraphDepth(doc, depth.Value, warnVisible: true);
                    r.TypeWrites += n;
                }

                // ── Step B. Per-category depth override ────────────────
                if (catDeps != null && catDeps.Count > 0)
                    r.TypeWrites += WriteCategoryDepths(doc, view, catDeps);

                // ── Step D. Tag style preset (size/style/colour) ───────
                // Profile triple wins; otherwise fall back to pack
                // DefaultTagStyle. CategoryTagStyles loop runs after
                // the global preset so per-category values win.
                StylePresetTriple effective = ResolveStyleTriple(size, style, colour, pack?.DefaultTagStyle);
                if (effective != null)
                    r.TypeWrites += ApplyTagStylePreset(doc, effective);

                if (pack?.CategoryTagStyles != null && pack.CategoryTagStyles.Count > 0)
                    r.TypeWrites += ApplyCategoryTagStyles(doc, view, pack.CategoryTagStyles);

                // ── Step H. T4-T10 tier visibility (PARA_STATE_4..10) ──────
                // SectionVisibility entries with keys "T4".."T10" mirror the
                // A-F per-section flags but write to TAG_PARA_STATE_N_BOOL on
                // the element types in scope. WriteTag7All gates each tier's
                // append on the same flag — flipping these here forces the
                // T4-T10 payload to render on the next tagging cycle.
                if (sectVis != null && sectVis.Count > 0)
                {
                    int tierWrites = ApplyTierVisibility(doc, view, sectVis);
                    if (tierWrites > 0) r.TypeWrites += tierWrites;
                }

                // ── Step I. T4-T10 pattern mode (HANDOVER / DC / CUSTOM) ───
                // Sets the HANDOVER_MODE_*_BOOL trio mutually exclusively on
                // the element types in scope. WriteTag7All reads this trio via
                // TagConfig.ResolveActivePatternMode and prefixes the appended
                // tier payload with [<MODE>] so the rendered tag advertises
                // which T4-T10 pack is active.
                if (!string.IsNullOrWhiteSpace(patternMode))
                {
                    int modeWrites = ApplyPatternMode(doc, view, patternMode);
                    if (modeWrites > 0) r.TypeWrites += modeWrites;
                }
            }
            catch (Exception ex)
            {
                r.Warnings.Add($"TokenProfileApplier: {ex.Message}");
                StingLog.Warn($"TokenProfileApplier.Apply: {ex.Message}");
            }

            StingLog.Info(
                $"TokenProfileApplier: view='{view.Name}' viewWrites={r.ViewParamWrites} " +
                $"elemWrites={r.ElementWrites} typeWrites={r.TypeWrites} " +
                $"warnings={r.Warnings.Count}");
            return r;
        }

        // ── View-level parameter writer (string only) ───────────────────

        private static bool TryWriteViewParam(View view, string paramName, string value)
        {
            try
            {
                Parameter p = view.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType != StorageType.String) return false;
                string cur = p.AsString() ?? "";
                if (string.Equals(cur, value, StringComparison.Ordinal)) return false;
                p.Set(value ?? "");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TokenProfileApplier view param '{paramName}': {ex.Message}");
                return false;
            }
        }

        // ── Per-element integer write (e.g. STING_DISPLAY_MODE) ─────────

        private static int WriteElementInts(Document doc, View view, string paramName, int value)
        {
            int n = 0;
            try
            {
                var ids = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds();
                foreach (var id in ids)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    Parameter p = el.LookupParameter(paramName);
                    if (p == null || p.IsReadOnly) continue;
                    if (p.StorageType != StorageType.Integer) continue;
                    if (p.AsInteger() == value) continue;
                    try { p.Set(value); n++; }
                    catch { /* element-specific failure — keep going */ }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WriteElementInts({paramName}): {ex.Message}");
            }
            return n;
        }

        // PERF-03: helper used by the merged single-pass loop above.
        private static Dictionary<string, bool> CanonicaliseSectionVisibility(Dictionary<string, bool> map)
        {
            var canonical = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (map == null) return canonical;
            foreach (var kv in map)
            {
                string k = (kv.Key ?? string.Empty).Trim().ToUpperInvariant();
                if (k.Length == 1 && k[0] >= 'A' && k[0] <= 'F') canonical[k] = kv.Value;
            }
            return canonical;
        }

        // ── TAG7 section visibility (per element) ───────────────────────

        private static int WriteSectionVisibility(Document doc, View view, Dictionary<string, bool> map)
        {
            int n = 0;
            // Only A..F are valid keys.
            var canonical = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in map)
            {
                string k = (kv.Key ?? "").Trim().ToUpperInvariant();
                if (k.Length == 1 && k[0] >= 'A' && k[0] <= 'F')
                    canonical[k] = kv.Value;
            }
            if (canonical.Count == 0) return 0;

            var ids = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                foreach (var kv in canonical)
                {
                    string pname = $"TAG_7_SECTION_VISIBLE_{kv.Key}_BOOL";
                    Parameter p = el.LookupParameter(pname);
                    if (p == null) continue;   // family doesn't carry this section flag
                    if (ParameterHelpers.SetYesNo(el, pname, kv.Value, overwrite: true)) n++;
                }
            }
            return n;
        }

        // ── Per-category depth override ─────────────────────────────────

        private static int WriteCategoryDepths(Document doc, View view, Dictionary<string, int> map)
        {
            int n = 0;
            var byCat = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);
            var ids = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                if (el?.Category == null) continue;
                string cat = el.Category.Name ?? "";
                if (string.IsNullOrEmpty(cat)) continue;
                if (!byCat.TryGetValue(cat, out var list))
                    byCat[cat] = list = new List<ElementId>();
                list.Add(id);
            }

            string[] states = ParamRegistry.AllParaStates;
            foreach (var kv in map)
            {
                int d = Math.Max(1, Math.Min(10, kv.Value));
                if (!byCat.TryGetValue(kv.Key ?? "", out var list)) continue;

                // Apply depth on the TYPE of each instance; one type
                // serves many instances so dedupe.
                var typeIds = new HashSet<ElementId>();
                foreach (var id in list)
                {
                    var el = doc.GetElement(id);
                    var tid = el?.GetTypeId();
                    if (tid != null && tid != ElementId.InvalidElementId)
                        typeIds.Add(tid);
                }

                foreach (var tid in typeIds)
                {
                    var typeEl = doc.GetElement(tid);
                    if (typeEl == null) continue;
                    bool any = false;
                    for (int i = 0; i < states.Length; i++)
                    {
                        bool want = (i + 1) <= d;
                        if (ParameterHelpers.SetYesNo(typeEl, states[i], want, overwrite: true))
                            any = true;
                    }
                    if (any) n++;
                }
            }
            return n;
        }

        // ── Style preset matrix ─────────────────────────────────────────

        private sealed class StylePresetTriple
        {
            public string Size;
            public string Style;
            public string Color;
            public string ParamName => $"TAG_{Size}{Style}_{Color}_BOOL";
        }

        private static StylePresetTriple ResolveStyleTriple(
            string size, string style, string colour, string packDefault)
        {
            // Profile triple — accept partial values, fill from pack default.
            if (string.IsNullOrWhiteSpace(size) && string.IsNullOrWhiteSpace(style)
                && string.IsNullOrWhiteSpace(colour))
            {
                if (string.IsNullOrWhiteSpace(packDefault)) return null;
                return ParseCanonicalStyleName(packDefault);
            }

            var fallback = string.IsNullOrWhiteSpace(packDefault)
                ? null : ParseCanonicalStyleName(packDefault);

            return new StylePresetTriple
            {
                Size  = !string.IsNullOrWhiteSpace(size)   ? size.Trim()
                          : fallback?.Size  ?? "2.5",
                Style = !string.IsNullOrWhiteSpace(style)  ? style.Trim().ToUpperInvariant()
                          : fallback?.Style ?? "NOM",
                Color = !string.IsNullOrWhiteSpace(colour) ? colour.Trim().ToUpperInvariant()
                          : fallback?.Color ?? "BLACK",
            };
        }

        /// <summary>
        /// Canonical style name format is "{size}{style}_{color}", e.g.
        /// "2.5BOLD_RED" or "2NOM_BLACK". Returns null on unparsable
        /// input.
        /// </summary>
        private static StylePresetTriple ParseCanonicalStyleName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            int u = s.IndexOf('_');
            if (u <= 0 || u >= s.Length - 1) return null;
            string head = s.Substring(0, u);
            string color = s.Substring(u + 1).ToUpperInvariant();

            // Pull style suffix off head — match longest known style.
            string[] styles = { "BOLDITALIC", "BOLD", "ITALIC", "NOM" };
            foreach (var st in styles)
            {
                if (head.EndsWith(st, StringComparison.OrdinalIgnoreCase))
                {
                    string size = head.Substring(0, head.Length - st.Length);
                    return new StylePresetTriple { Size = size, Style = st, Color = color };
                }
            }
            return null;
        }

        private static int ApplyTagStylePreset(Document doc, StylePresetTriple t)
        {
            string activeParam = t.ParamName;
            string[] all = ParamRegistry.AllTagStyleParams;
            if (all == null || all.Length == 0) return 0;

            // GAP-C: previously walked every ElementType in the document
            // (10,000+ in a typical project) probing for TAG_*_BOOL params.
            // The TAG_*_BOOL parameters only exist on tag-family types, so
            // pre-filter by category before LookupParameter probing —
            // typical project drops to ~50 tag types instead of 10,000+.
            var probeParam = all[0];
            int updated = 0;
            var typeIter = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Where(IsTagFamilyType);
            foreach (Element typeEl in typeIter)
            {
                // Cheap reject: if the canonical TAG_*_BOOL probe is
                // missing, this type doesn't carry the style matrix.
                if (typeEl.LookupParameter(probeParam) == null) continue;
                bool any = false;
                foreach (string pname in all)
                {
                    Parameter p = typeEl.LookupParameter(pname);
                    if (p == null || p.IsReadOnly) continue;
                    bool want = string.Equals(pname, activeParam, StringComparison.OrdinalIgnoreCase);
                    if (ParameterHelpers.SetYesNo(typeEl, pname, want, overwrite: true))
                        any = true;
                }
                if (any) updated++;
            }
            return updated;
        }

        private static bool IsTagFamilyType(Element el)
        {
            try
            {
                if (el is FamilySymbol fs)
                {
                    var cat = fs.Category;
                    if (cat == null) return false;
                    var bic = (BuiltInCategory)cat.Id.Value;
                    switch (bic)
                    {
                        case BuiltInCategory.OST_DoorTags:
                        case BuiltInCategory.OST_WindowTags:
                        case BuiltInCategory.OST_RoomTags:
                        case BuiltInCategory.OST_WallTags:
                        case BuiltInCategory.OST_FloorTags:
                        case BuiltInCategory.OST_CeilingTags:
                        case BuiltInCategory.OST_RoofTags:
                        case BuiltInCategory.OST_StairsTags:
                        case BuiltInCategory.OST_StructuralColumnTags:
                        case BuiltInCategory.OST_StructuralFramingTags:
                        case BuiltInCategory.OST_StructuralFoundationTags:
                        case BuiltInCategory.OST_FurnitureTags:
                        case BuiltInCategory.OST_LightingFixtureTags:
                        case BuiltInCategory.OST_MechanicalEquipmentTags:
                        case BuiltInCategory.OST_PlumbingFixtureTags:
                        case BuiltInCategory.OST_DuctTags:
                        case BuiltInCategory.OST_PipeTags:
                        case BuiltInCategory.OST_ConduitTags:
                        case BuiltInCategory.OST_CableTrayTags:
                        case BuiltInCategory.OST_ElectricalEquipmentTags:
                        case BuiltInCategory.OST_ElectricalFixtureTags:
                        case BuiltInCategory.OST_GenericModelTags:
                        case BuiltInCategory.OST_KeynoteTags:
                        case BuiltInCategory.OST_MultiCategoryTags:
                        case BuiltInCategory.OST_AreaTags:
                        case BuiltInCategory.OST_MEPSpaceTags:
                        case BuiltInCategory.OST_MaterialTags:
                            return true;
                    }
                }
            }
            catch { /* defensive — fall through */ }
            return false;
        }

        private static int ApplyCategoryTagStyles(Document doc, View view, Dictionary<string, string> map)
        {
            int updated = 0;
            // Collect type ids per category from instances in view.
            var byCat = new Dictionary<string, HashSet<ElementId>>(StringComparer.OrdinalIgnoreCase);
            var ids = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                var cat = el?.Category?.Name;
                if (string.IsNullOrEmpty(cat)) continue;
                var tid = el.GetTypeId();
                if (tid == null || tid == ElementId.InvalidElementId) continue;
                if (!byCat.TryGetValue(cat, out var set))
                    byCat[cat] = set = new HashSet<ElementId>();
                set.Add(tid);
            }

            string[] all = ParamRegistry.AllTagStyleParams;
            foreach (var kv in map)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                var triple = ParseCanonicalStyleName(kv.Value);
                if (triple == null) continue;
                if (!byCat.TryGetValue(kv.Key ?? "", out var typeIds)) continue;

                string activeParam = triple.ParamName;
                foreach (var tid in typeIds)
                {
                    var typeEl = doc.GetElement(tid);
                    if (typeEl == null) continue;
                    bool any = false;
                    foreach (string pname in all)
                    {
                        Parameter p = typeEl.LookupParameter(pname);
                        if (p == null || p.IsReadOnly) continue;
                        bool want = string.Equals(pname, activeParam, StringComparison.OrdinalIgnoreCase);
                        if (ParameterHelpers.SetYesNo(typeEl, pname, want, overwrite: true))
                            any = true;
                    }
                    if (any) updated++;
                }
            }
            return updated;
        }

        // ── Phase 165 — T4-T10 tier visibility (PARA_STATE_4..10) ───────
        // Reads keys "T4".."T10" out of the SectionVisibility map (case-
        // insensitive) and writes the matching PARA_STATE_N_BOOL on the type
        // of every element in the view. Idempotent — only counts writes that
        // actually changed a parameter.
        private static int ApplyTierVisibility(Document doc, View view,
            Dictionary<string, bool> map)
        {
            int n = 0;
            // Build canonical T4..T10 → bool dictionary.
            var canonical = new Dictionary<int, bool>();
            foreach (var kv in map)
            {
                string raw = (kv.Key ?? "").Trim().ToUpperInvariant();
                if (!raw.StartsWith("T")) continue;
                if (!int.TryParse(raw.Substring(1), out int tier)) continue;
                if (tier < 4 || tier > 10) continue;
                canonical[tier] = kv.Value;
            }
            if (canonical.Count == 0) return 0;

            string[] paraNames = new[]
            {
                ParamRegistry.PARA_STATE_4, ParamRegistry.PARA_STATE_5, ParamRegistry.PARA_STATE_6,
                ParamRegistry.PARA_STATE_7, ParamRegistry.PARA_STATE_8, ParamRegistry.PARA_STATE_9,
                ParamRegistry.PARA_STATE_10,
            };

            // Collect TYPE ids of every instance in the view (PARA_STATE lives
            // on the type per SetParagraphDepthCommand semantics).
            var typeIds = new HashSet<ElementId>();
            var ids = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                var tid = el?.GetTypeId();
                if (tid != null && tid != ElementId.InvalidElementId) typeIds.Add(tid);
            }

            foreach (var tid in typeIds)
            {
                var typeEl = doc.GetElement(tid);
                if (typeEl == null) continue;
                foreach (var kv in canonical)
                {
                    int idx = kv.Key - 4; // 0..6 into paraNames
                    if (idx < 0 || idx >= paraNames.Length) continue;
                    if (ParameterHelpers.SetYesNo(typeEl, paraNames[idx], kv.Value, overwrite: true))
                        n++;
                }
            }
            return n;
        }

        // ── Phase 165 — T4-T10 pattern mode (HANDOVER / DC / CUSTOM) ────
        // Writes the trio of HANDOVER_MODE_*_BOOL parameters mutually
        // exclusively on every element type in the view. Empty / unknown
        // mode => no-op (caller already gates on whitespace).
        private static int ApplyPatternMode(Document doc, View view, string mode)
        {
            string M = (mode ?? "").Trim().ToUpperInvariant();
            if (M != "HANDOVER" && M != "DC" && M != "CUSTOM") return 0;

            var typeIds = new HashSet<ElementId>();
            var ids = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                var tid = el?.GetTypeId();
                if (tid != null && tid != ElementId.InvalidElementId) typeIds.Add(tid);
            }

            int n = 0;
            foreach (var tid in typeIds)
            {
                var typeEl = doc.GetElement(tid);
                if (typeEl == null) continue;
                bool a = ParameterHelpers.SetYesNo(typeEl, ParamRegistry.MODE_HANDOVER, M == "HANDOVER", overwrite: true);
                bool b = ParameterHelpers.SetYesNo(typeEl, ParamRegistry.MODE_DC,       M == "DC",       overwrite: true);
                bool c = ParameterHelpers.SetYesNo(typeEl, ParamRegistry.MODE_CUSTOM,   M == "CUSTOM",   overwrite: true);
                if (a || b || c) n++;
            }
            return n;
        }

        // ── Presentation-mode preset (matches the existing modes) ───────

        private static bool ApplyPresentationPreset(Document doc, string mode)
        {
            bool s1, s2, s3, warn;
            switch ((mode ?? "").Trim().ToUpperInvariant())
            {
                case "COMPACT":
                    s1 = true;  s2 = false; s3 = false; warn = false; break;
                case "TECHNICAL":
                    s1 = true;  s2 = true;  s3 = false; warn = true;  break;
                case "FULLSPEC":
                case "FULL_SPECIFICATION":
                case "FULL SPECIFICATION":
                    s1 = true;  s2 = true;  s3 = true;  warn = true;  break;
                case "PRESENTATION":
                    s1 = true;  s2 = true;  s3 = false; warn = false; break;
                case "BOQ":
                    s1 = true;  s2 = true;  s3 = false; warn = false; break;
                default:
                    return false;
            }

            var allTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToList();
            foreach (Element typeEl in allTypes)
            {
                ParameterHelpers.SetYesNo(typeEl, ParamRegistry.PARA_STATE_1, s1, overwrite: true);
                ParameterHelpers.SetYesNo(typeEl, ParamRegistry.PARA_STATE_2, s2, overwrite: true);
                ParameterHelpers.SetYesNo(typeEl, ParamRegistry.PARA_STATE_3, s3, overwrite: true);
                ParameterHelpers.SetYesNo(typeEl, ParamRegistry.WARN_VISIBLE, warn, overwrite: true);
            }
            return true;
        }
    }
}
