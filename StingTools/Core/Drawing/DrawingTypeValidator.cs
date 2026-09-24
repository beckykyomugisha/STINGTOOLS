// StingTools — Drawing Template Manager
//
// DrawingTypeValidator runs the pre-flight checks that stand between a
// user pressing "Generate" and the batch actually running. Its job is
// to catch the silent-fallback failures that cause rework later:
// missing title block family, missing view template, missing tag /
// dimension / section marker family, unloaded annotation families.
//
// Every check returns a ValidationIssue with a severity and a clear
// message; callers (batch commands, preflight dialog) decide whether
// to block, warn-and-proceed, or offer to auto-load the missing
// asset. Blocking vs warning is advisory here — callers own the UX.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.UI;
using StingTools.Core.Validation;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public enum ValidationSeverity { Info, Warning, Error }

    public sealed class ValidationIssue
    {
        public ValidationSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string DrawingTypeId { get; set; }
        public string SuggestedFix { get; set; }
    }

    public sealed class ValidationReport
    {
        public string DrawingTypeId { get; set; }
        public List<ValidationIssue> Issues { get; } = new List<ValidationIssue>();
        public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);
        public bool HasWarnings => Issues.Any(i => i.Severity == ValidationSeverity.Warning);

        public void Add(ValidationSeverity sev, string code, string msg, string fix = null)
            => Issues.Add(new ValidationIssue
            {
                Severity = sev, Code = code, Message = msg,
                DrawingTypeId = DrawingTypeId, SuggestedFix = fix,
            });
    }

    public static class DrawingTypeValidator
    {
        /// <summary>
        /// Validate a single DrawingType against the current project:
        /// title block loaded, view template present, section marker
        /// family present, tag families present for each category in
        /// the annotation pack, slots geometry sensible.
        /// </summary>
        public static ValidationReport Validate(Document doc, DrawingType dt)
        {
            var r = new ValidationReport { DrawingTypeId = dt?.Id };
            if (dt == null)
            {
                r.Add(ValidationSeverity.Error, "DT-000", "DrawingType is null.");
                return r;
            }

            if (string.IsNullOrWhiteSpace(dt.Id))
                r.Add(ValidationSeverity.Error, "DT-001", "DrawingType has no id.");

            // Title block -------------------------------------------------
            // P5 — validate against the CONCRETE built family the resolver maps
            // the profile's (possibly logical) title-block name to, not the raw
            // dangling name (STING_TB_SHEET_A1 etc.), which is never loaded and
            // used to false-positive every profile.
            string declaredFam = dt.TitleBlockFamily;
            try { declaredFam = DrawingDispatcher.ResolveTitleBlockVariant(dt).family; } catch (Exception ex) { StingTools.Core.StingLog.Warn($"Suppressed: {ex.Message}"); }
            if (string.IsNullOrWhiteSpace(declaredFam)) declaredFam = dt.TitleBlockFamily;
            string concreteFam = declaredFam;
            try
            {
                // T-6: surface the resolver's reasons (blank paper, unsupported
                // size, no presentation variant at this size, name/paper
                // mismatch) instead of only logging them.
                var res = TitleBlockResolver.Resolve(doc, dt, declaredFam);
                if (res.IsResolved) concreteFam = res.Family;
                foreach (var w in res.Warnings)
                    r.Add(ValidationSeverity.Warning, "DT-012", w,
                        "Set paperSize / orientation / titleBlockFamily so they name a family in STING_TITLE_BLOCKS.json.");
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Suppressed: {ex.Message}"); }
            string resolvedNote = string.Equals(concreteFam, declaredFam, StringComparison.OrdinalIgnoreCase)
                ? "" : $" (resolved from '{declaredFam}')";

            if (!string.IsNullOrWhiteSpace(concreteFam))
            {
                if (!HasTitleBlockFamily(doc, concreteFam))
                {
                    // Distinguish "not built" from "built but not loaded" (the
                    // producer lazy-loads a built .rfa on demand).
                    bool onDisk = false;
                    try { onDisk = TitleBlockResolver.BuiltRfaExists(doc, concreteFam); } catch (Exception ex) { StingTools.Core.StingLog.Warn($"Suppressed: {ex.Message}"); }
                    if (onDisk)
                        r.Add(ValidationSeverity.Info, "DT-010",
                            $"Title block family '{concreteFam}'{resolvedNote} not loaded but built on disk — the producer loads it on demand (or run TitleBlock_CreateAll + reopen to preload).");
                    else
                        r.Add(ValidationSeverity.Warning, "DT-010",
                            $"Title block family '{concreteFam}'{resolvedNote} is neither loaded nor built on disk.",
                            "Run TitleBlock_CreateAll to build the STING title-block families, or point the profile at a loaded family.");
                }

                // DT-011 (Phase 168): titleBlockSymbolType references a symbol the family doesn't have.
                if (!string.IsNullOrWhiteSpace(dt.TitleBlockSymbolType)
                    && HasTitleBlockFamily(doc, concreteFam)
                    && !HasTitleBlockSymbol(doc, concreteFam, dt.TitleBlockSymbolType))
                    r.Add(ValidationSeverity.Warning, "DT-011",
                        $"Title block symbol type '{dt.TitleBlockSymbolType}' not found within family '{concreteFam}'. Engine will fall back to first symbol.",
                        "Open the family in Family Editor, confirm the type name, or clear titleBlockSymbolType to accept first-symbol fallback.");
            }

            // View template ----------------------------------------------
            if (!string.IsNullOrWhiteSpace(dt.ViewTemplateName))
            {
                if (!HasViewTemplate(doc, dt.ViewTemplateName))
                    r.Add(ValidationSeverity.Warning, "DT-020",
                        $"View template '{dt.ViewTemplateName}' not found in project.",
                        "Create the template via Template Mgr > Template Setup Wizard, or clear the profile's viewTemplateName.");
            }

            // Viewport type ----------------------------------------------
            if (!string.IsNullOrWhiteSpace(dt.ViewportTypeName))
            {
                if (!HasViewportType(doc, dt.ViewportTypeName))
                {
                    if (StingViewportTypes.CanonicalFor(dt.ViewportTypeName) != null)
                        r.Add(ValidationSeverity.Info, "DT-021",
                            $"Viewport type '{dt.ViewportTypeName}' not in this project yet.",
                            "It is created on first placement by duplicating an existing viewport type; style it afterwards.");
                    else
                        r.Add(ValidationSeverity.Warning, "DT-021",
                            $"Viewport type '{dt.ViewportTypeName}' not found.",
                            "Duplicate an existing Viewport Type and name it to match, or clear the field.");
                }
            }

            // Section marker family --------------------------------------
            if (IsSectionLikePurpose(dt.Purpose) && !string.IsNullOrWhiteSpace(dt.SectionMarker?.Family))
            {
                if (!HasAnnotationFamily(doc, dt.SectionMarker.Family))
                    r.Add(ValidationSeverity.Warning, "DT-030",
                        $"Section/elevation marker family '{dt.SectionMarker.Family}' not loaded.",
                        "Load the marker family or set sectionMarker.family to null to use project default.");
            }

            // Tag families ------------------------------------------------
            if (dt.Annotation?.TagFamilies != null)
            {
                foreach (var kv in dt.Annotation.TagFamilies)
                {
                    if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                    if (!HasAnnotationFamily(doc, kv.Value))
                        r.Add(ValidationSeverity.Warning, "DT-040",
                            $"Tag family '{kv.Value}' (for {kv.Key}) not loaded.",
                            "Load the tag family or remove the mapping to use the project default tag for that category.");
                }
            }

            // Slot sanity -------------------------------------------------
            if (dt.Slots == null || dt.Slots.Count == 0)
                r.Add(ValidationSeverity.Info, "DT-050",
                    "DrawingType has no slots defined — generation will place views at sheet origin.");
            else
                ValidateSlotGeometry(dt, r);

            // Pattern sanity ---------------------------------------------
            if (string.IsNullOrWhiteSpace(dt.SheetNumberPattern))
                r.Add(ValidationSeverity.Warning, "DT-060",
                    "sheetNumberPattern is empty — generated sheets may collide in numbering.");
            if (string.IsNullOrWhiteSpace(dt.SheetNamePattern))
                r.Add(ValidationSeverity.Info, "DT-061",
                    "sheetNamePattern is empty — sheets will be named by Revit's default.");

            // DT-095: scale must be positive on every purpose where scale
            // actually applies. Assigning view.Scale = 0 throws, and the engine
            // logs + skips the assignment by design — so the warning only means
            // something where a scale was expected. 3D / Perspective never carry
            // one; neither do Schedule or Schematic, which are the other two
            // purposes the shipped catalogue authors as "scale": "NA" (a riser
            // or single-line diagram is drawn NTS, and a schedule is a table).
            if (dt.Scale <= 0)
            {
                bool scaleNotApplicable =
                       string.Equals(dt.Purpose, DrawingPurpose.ThreeD,     StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dt.Purpose, "Perspective",             StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dt.Purpose, DrawingPurpose.Schedule,   StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dt.Purpose, DrawingPurpose.Schematic,  StringComparison.OrdinalIgnoreCase);
                if (!scaleNotApplicable)
                    r.Add(ValidationSeverity.Warning, "DT-095",
                        $"Scale is {dt.Scale} — must be a positive integer for drawing types where scale applies. Set scale > 0, or use purpose '3D'/'Perspective'/'Schedule'/'Schematic' for views where it does not.");
            }

            // DT-096: ISO naming tokens in the sheet number pattern need an
            // isoNaming block, otherwise they resolve to empty strings.
            if (!string.IsNullOrEmpty(dt.SheetNumberPattern) && dt.IsoNaming == null)
            {
                bool referencesIso =
                    dt.SheetNumberPattern.IndexOf("{project}",    StringComparison.OrdinalIgnoreCase) >= 0
                 || dt.SheetNumberPattern.IndexOf("{originator}", StringComparison.OrdinalIgnoreCase) >= 0
                 || dt.SheetNumberPattern.IndexOf("{vol}",        StringComparison.OrdinalIgnoreCase) >= 0;
                if (referencesIso)
                    r.Add(ValidationSeverity.Warning, "DT-096",
                        "sheetNumberPattern references ISO naming tokens ({project}, {originator}, etc.) but isoNaming is null. These tokens will resolve to empty strings. Add an isoNaming block to this drawing type.");
            }

            // DT-097 (Phase 168): paperSize ↔ titleBlockFamily cross-check.
            // Heuristic — if the family name embeds a paper-size code (A0/A1/
            // A2/A3/A4) different from the profile's PaperSize, surface a
            // mismatch. Avoids the "A1 profile points at an A3 family"
            // silent-failure mode.
            if (!string.IsNullOrWhiteSpace(dt.PaperSize)
                && !string.IsNullOrWhiteSpace(concreteFam))
            {
                // P5 — cross-check the RESOLVED concrete family name (which
                // embeds the real paper-size code) rather than the logical one.
                var fam = concreteFam.ToUpperInvariant();
                var paper = dt.PaperSize.Trim().ToUpperInvariant();
                string foundCode = null;
                foreach (var code in new[] { "A0", "A1", "A2", "A3", "A4" })
                {
                    // Match boundary: surrounded by non-alphanumerics so "A10" wouldn't match "A1".
                    var idx = fam.IndexOf(code, StringComparison.Ordinal);
                    while (idx >= 0)
                    {
                        bool leftOk  = idx == 0 || !char.IsLetterOrDigit(fam[idx - 1]);
                        bool rightOk = idx + code.Length == fam.Length
                                    || !char.IsLetterOrDigit(fam[idx + code.Length]);
                        if (leftOk && rightOk) { foundCode = code; break; }
                        idx = fam.IndexOf(code, idx + 1, StringComparison.Ordinal);
                    }
                    if (foundCode != null) break;
                }
                if (foundCode != null && !string.Equals(foundCode, paper, StringComparison.Ordinal))
                    r.Add(ValidationSeverity.Warning, "DT-097",
                        $"PaperSize '{dt.PaperSize}' may not match resolved title-block family '{concreteFam}' (family name suggests {foundCode}).",
                        "Confirm the family is sized correctly or update PaperSize to match.");
            }

            // DT-098 (Phase 168): every {token} referenced by sheet patterns or
            // titleBlockParams should resolve to a known key. Unknown tokens
            // pass through as literal text — usually a typo. Built-in token
            // set mirrors DrawingTokenContext.Build(...).
            ValidateUnknownTokens(dt, r);

            // ── Phase 137 — annotation family + production rule + managed pack checks ──

            ValidatePhase137Annotation(doc, dt, r);
            ValidatePhase137ProductionRules(dt, r);
            ValidatePhase137ManagedPack(doc, dt, r);

            // ── ACC-03: crop strategy must be sensible for the view type ──
            ValidateCropForPurpose(dt, r);

            // ── ACC-04: every ${PRJ_ORG_xxx} referenced by TitleBlockParams
            //   must already be bound on ProjectInformation; otherwise the
            //   applier would silently substitute an empty string.
            ValidateProjectInfoBindings(doc, dt, r);

            // ── GAP-K: slot ViewType compatibility with profile.Purpose ──
            ValidateSlotPurposeAlignment(dt, r);

            // ── GAP-L: live cross-check of declared slot labels vs. the slots
            //    actually embedded in the title-block family loaded in the doc ──
            ValidateTitleBlockSlotsVsFamily(doc, dt, r);

            // ── GAP-M: detect overlapping slot bounding boxes ──
            ValidateSlotOverlaps(dt, r);

            return r;
        }

        private static void ValidateCropForPurpose(DrawingType dt, ValidationReport r)
        {
            if (dt?.Crop == null) return;
            var kind = (dt.Crop.Kind ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(kind)) return;
            // RoomBoundary only makes sense on plan-style purposes — section,
            // elevation, schedule, legend, and 3D have no rooms to bound.
            if (string.Equals(kind, "RoomBoundary", StringComparison.OrdinalIgnoreCase))
            {
                bool isPlanLike =
                    string.Equals(dt.Purpose, DrawingPurpose.Plan, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(dt.Purpose, DrawingPurpose.Rcp,  StringComparison.OrdinalIgnoreCase);
                if (!isPlanLike)
                    r.Add(ValidationSeverity.Warning, "DT-080",
                        $"Crop kind 'RoomBoundary' on a {dt.Purpose} profile will silently fall back to TightBbox at runtime.",
                        "Switch crop.kind to 'TightBbox' or 'ScopeBoxOrBbox' for non-plan profiles.");
            }
            // ScopeBox kind requires a name.
            if (string.Equals(kind, "ScopeBox", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(dt.Crop.ScopeBoxName))
            {
                r.Add(ValidationSeverity.Error, "DT-081",
                    "Crop kind 'ScopeBox' requires crop.scopeBoxName; switch to 'ScopeBoxOrBbox' to allow fallback.");
            }
        }

        private static void ValidateProjectInfoBindings(Document doc, DrawingType dt, ValidationReport r)
        {
            if (doc == null || dt?.TitleBlockParams == null) return;
            try
            {
                var missing = TitleBlockParamApplier.FindMissingProjectInfoParams(doc, dt);
                foreach (var name in missing)
                    r.Add(ValidationSeverity.Warning, "DT-090",
                        $"TitleBlockParams reference ${{ {name} }} but ProjectInformation has no parameter named '{name}'.",
                        "Run Tags > Setup > Load Params, or update the project_info parameter name in the profile.");
            }
            catch { /* validator must never throw */ }
        }

        // DT-098 (Phase 168). Built-in token set mirrors DrawingTokenContext.Build —
        // any {key} outside this set is most likely a typo and is reported.
        private static readonly System.Collections.Generic.HashSet<string> _knownTokens =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "spool","disc","discipline","sys","lvl","mark","purpose","phase",
                "project","originator","vol","type","role","suit","rev","seq",
            };
        private static readonly System.Text.RegularExpressions.Regex _tokenScan =
            new System.Text.RegularExpressions.Regex(@"\{([A-Za-z0-9_]+)(?::D\d+)?(?:\|[^}]*)?\}",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static void ValidateUnknownTokens(DrawingType dt, ValidationReport r)
        {
            if (dt == null) return;
            try
            {
                void Scan(string template, string source)
                {
                    if (string.IsNullOrEmpty(template)) return;
                    foreach (System.Text.RegularExpressions.Match m in _tokenScan.Matches(template))
                    {
                        var key = m.Groups[1].Value;
                        if (!_knownTokens.Contains(key))
                            r.Add(ValidationSeverity.Warning, "DT-098",
                                $"{source} references unknown token '{{{key}}}' — it will pass through as literal text.",
                                "Check spelling against {disc}/{lvl}/{seq:D4}/{spool}/{mark}/{vol}/{type}/{role}/{suit}/{rev}/{project}/{originator}, or remove if intentional.");
                    }
                }
                Scan(dt.SheetNumberPattern, "sheetNumberPattern");
                Scan(dt.SheetNamePattern,   "sheetNamePattern");
                if (dt.TitleBlockParams != null)
                    foreach (var kv in dt.TitleBlockParams)
                        Scan(kv.Value, $"titleBlockParams['{kv.Key}']");
            }
            catch { /* validator must never throw */ }
        }

        private static void ValidatePhase137Annotation(Document doc, DrawingType dt, ValidationReport r)
        {
            if (doc == null || dt?.Annotation == null) return;

            void CheckFamily(string family, string code, string label)
            {
                if (string.IsNullOrWhiteSpace(family)) return;
                if (FindAnnotationFamily(doc, family) == null)
                    r.Add(ValidationSeverity.Warning, code,
                        $"{label} family '{family}' not found in project.",
                        "Load the family or clear the field on the profile.");
            }

            // DT-142: print.colourScheme against a closed vocabulary. The
            // shipped catalogue used TWO spellings for one concept —
            // "Monochrome" (38 profiles) and "BlackAndWhite" (28) — and
            // nothing validated either, so a third could have appeared and
            // silently meant "no scheme".
            ValidatePrintBlock(dt, r);

            // DT-139: every annotation rule's ruleType must be a name
            // AnnotationRuleKinds declares — i.e. one a runner pass actually
            // handles. This is an ERROR, not a warning: an unknown ruleType
            // places nothing, and before this gate existed that was
            // indistinguishable from a rule that ran. 56 of the 334 rules in
            // the shipped catalogue were in exactly that state.
            //
            // Doc-independent, so it runs even when the caller has no model.
            ValidateAnnotationRuleTypes(dt, r);

            // DT-140: a rule whose category the resolved style pack hides.
            // The tag lands on an invisible host — the drawing shows neither.
            ValidateAnnotationAgainstHiddenCategories(doc, dt, r);

            CheckFamily(dt.Annotation.NorthArrowFamily, "DT-137-NA", "North arrow");
            CheckFamily(dt.Annotation.ScaleBarFamily,   "DT-137-SB", "Scale bar");
            CheckFamily(dt.Annotation.KeyPlanFamily,    "DT-137-KP", "Key plan");

            if (dt.Annotation.SpotElevationRules != null)
                foreach (var s in dt.Annotation.SpotElevationRules)
                    CheckFamily(s?.SymbolFamily, "DT-137-SE", $"Spot-elevation symbol ({s?.Category})");
            if (dt.Annotation.SpotCoordinateRules != null)
                foreach (var s in dt.Annotation.SpotCoordinateRules)
                    CheckFamily(s?.SymbolFamily, "DT-137-SC", $"Spot-coordinate symbol ({s?.Category})");
        }

        /// <summary>
        /// DT-139 — ruleType vocabulary check against AnnotationRuleKinds.
        /// Revit-free so it holds in every caller, including a headless
        /// data-file validation pass.
        /// </summary>
        internal static void ValidateAnnotationRuleTypes(DrawingType dt, ValidationReport r)
        {
            var rules = dt?.Annotation?.Rules;
            if (rules == null || rules.Count == 0) return;

            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
            {
                if (rule == null) continue;
                var rt = rule.RuleType;
                if (AnnotationRuleKinds.IsKnown(rt)) continue;
                var key = rt ?? "<null>";
                if (!reported.Add(key)) continue;
                r.Add(ValidationSeverity.Error, "DT-139",
                    $"Annotation ruleType '{key}' (category '{rule.Category}') is not in the implemented vocabulary — "
                    + "no runner pass claims it, so this rule places nothing.",
                    $"Use one of: {string.Join(", ", AnnotationRuleKinds.AllRuleTypes)}. "
                    + "If the behaviour is genuinely new, declare it in AnnotationRuleKinds and wire a handler in AnnotationRunner.");
            }

            // DT-139-FAM: a tagFamilies key that resolves to no category, or
            // that no rule in this profile will ever look up.
            //
            // ResolveTagTypeId does pack.TagFamilies.TryGetValue(catKey) with
            // the RULE's category string, so a key spelled any other way is
            // dead: the declared family is silently replaced by "first loaded
            // tag of that category". There was a warning for a family that is
            // not LOADED, but none for a key nothing looks up, which is why
            // seven PascalCase-without-spaces keys (StructuralColumns,
            // LightingFixtures, …) went unnoticed.
            ValidateTagFamilyKeys(dt, r);

            // DT-139-TAG: a tag rule on a category Revit cannot tag.
            // IndependentTag.Create requires a taggable MODEL category, so a
            // rule on an annotation or datum category throws once per element
            // and buries the run in warnings. RevitCategoryTree carries the
            // taggable flag, so this reads the same table the runner does.
            foreach (var rule in rules)
            {
                if (rule == null || !rule.Enabled) continue;
                if (!AnnotationRuleKinds.IsTagKind(rule.RuleType)) continue;
                var cat = AnnotationRuleKinds.EffectiveCategory(rule.RuleType, rule.Category);
                if (string.IsNullOrWhiteSpace(cat) || cat == "*") continue;
                var meta = cat.StartsWith("OST_", StringComparison.OrdinalIgnoreCase)
                    ? RevitCategoryTree.FindByBic(cat)
                    : RevitCategoryTree.FindByDisplayName(cat);
                if (meta != null && !meta.IsTaggable)
                    r.Add(ValidationSeverity.Warning, "DT-139-TAG",
                        $"Annotation rule '{rule.RuleType}' targets '{cat}', which Revit cannot tag "
                        + "(IndependentTag.Create needs a taggable model category).",
                        "Drop the rule; use the style pack's vgOverrides to control how that category reads.");
            }

            // Auto3DTag is a whole-view operation dispatched once; extra
            // per-category rows express nothing the runner can act on, so
            // flag the redundancy rather than leaving 8 rows implying 8
            // distinct behaviours.
            int threeD = rules.Count(x => x != null && x.Enabled && AnnotationRuleKinds.IsThreeDKind(x.RuleType));
            if (threeD > 1)
                r.Add(ValidationSeverity.Info, "DT-139-3D",
                    $"{threeD} Auto3DTag rules declared. The 3D pass tags the whole view once, so only the first has effect.",
                    "Collapse to a single Auto3DTag rule (category \"*\").");
        }

        /// <summary>
        /// DT-139-FAM — tagFamilies key hygiene. Revit-free.
        /// </summary>
        internal static void ValidateTagFamilyKeys(DrawingType dt, ValidationReport r)
        {
            var fam = dt?.Annotation?.TagFamilies;
            if (fam == null || fam.Count == 0) return;

            // Categories a rule in THIS profile could look up.
            var consulted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool wildcard = false;
            foreach (var rule in dt.Annotation.Rules ?? new List<AutoAnnotationRule>())
            {
                if (rule == null || !rule.Enabled) continue;
                if (!AnnotationRuleKinds.IsTagKind(rule.RuleType)) continue;
                var cat = AnnotationRuleKinds.EffectiveCategory(rule.RuleType, rule.Category);
                if (cat == "*") { wildcard = true; continue; }
                if (!string.IsNullOrWhiteSpace(cat)) consulted.Add(cat);
            }

            foreach (var kv in fam)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;

                bool resolves = kv.Key.StartsWith("OST_", StringComparison.OrdinalIgnoreCase)
                    ? RevitCategoryTree.FindByBic(kv.Key) != null
                    : RevitCategoryTree.FindByDisplayName(kv.Key) != null;

                if (!resolves)
                {
                    r.Add(ValidationSeverity.Warning, "DT-139-FAM",
                        $"tagFamilies key '{kv.Key}' (→ '{kv.Value}') resolves to no Revit category, so the "
                        + "lookup misses and the declared family is silently replaced by the first loaded tag.",
                        "Use the localised category display name, e.g. \"Structural Columns\", not \"StructuralColumns\".");
                    continue;
                }

                if (!wildcard && !consulted.Contains(kv.Key))
                    r.Add(ValidationSeverity.Info, "DT-139-FAM-UNUSED",
                        $"tagFamilies key '{kv.Key}' (→ '{kv.Value}') is never consulted — no enabled tag rule in "
                        + "this profile targets that category.",
                        "Add a tag rule for the category, or remove the key so the profile does not read as configured.");
            }
        }

        /// <summary>
        /// DT-140 — an annotation rule targeting a category the resolved view
        /// style pack sets visible:false. The annotation is created against a
        /// host the view does not draw, so neither appears: a silent
        /// double-negative that reads on the sheet as "the tagger did not run".
        /// </summary>
        internal static void ValidateAnnotationAgainstHiddenCategories(Document doc, DrawingType dt, ValidationReport r)
        {
            if (doc == null || dt?.Annotation == null || string.IsNullOrWhiteSpace(dt.ViewStylePackId)) return;

            ViewStylePack pack;
            try { pack = ViewStylePackRegistry.Get(doc, dt.ViewStylePackId); }
            catch { return; }
            if (pack?.VgOverrides == null || pack.VgOverrides.Count == 0) return;

            var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in pack.VgOverrides)
                if (kv.Value?.Visible == false && !string.IsNullOrWhiteSpace(kv.Key))
                    hidden.Add(kv.Key.Trim());
            if (hidden.Count == 0) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in dt.Annotation.Rules ?? new List<AutoAnnotationRule>())
            {
                if (rule == null || !rule.Enabled) continue;
                var cat = AnnotationRuleKinds.EffectiveCategory(rule.RuleType, rule.Category);
                if (string.IsNullOrWhiteSpace(cat)) continue;
                // Compare on the display name the pack uses as well as the
                // BIC form the rule may carry.
                foreach (var candidate in new[] { cat, DisplayNameForBic(doc, cat) })
                {
                    if (string.IsNullOrWhiteSpace(candidate) || !hidden.Contains(candidate)) continue;
                    if (!seen.Add(candidate)) break;
                    r.Add(ValidationSeverity.Warning, "DT-140",
                        $"Annotation rule '{rule.RuleType}' targets category '{candidate}', which style pack "
                        + $"'{dt.ViewStylePackId}' sets visible:false. The annotation is placed on a host the view does not draw.",
                        "Either show the category in the pack, or drop the annotation rule.");
                    break;
                }
            }
        }

        /// <summary>
        /// The closed set of print colour schemes. Kept here rather than in a
        /// data file because each value is only meaningful to code that
        /// branches on it (title-block variant conditions today, an export
        /// preset tomorrow) — a value nothing branches on is not a scheme.
        /// </summary>
        internal static readonly string[] PrintColourSchemes =
        {
            "Monochrome",        // single-colour line work — the production default
            "ByDiscipline",      // discipline-coloured line work
            "PresentationRich",  // full-colour client presentation
            "PresentationMono",  // single-colour presentation
            "ClarificationRed",  // red mark-up over halftone base
        };

        /// <summary>
        /// DT-142 — print block sanity. Revit-free.
        /// </summary>
        internal static void ValidatePrintBlock(DrawingType dt, ValidationReport r)
        {
            var pr = dt?.Print;
            if (pr == null) return;

            if (!string.IsNullOrWhiteSpace(pr.ColourScheme)
                && !PrintColourSchemes.Any(x => string.Equals(x, pr.ColourScheme, StringComparison.OrdinalIgnoreCase)))
            {
                r.Add(ValidationSeverity.Warning, "DT-142",
                    $"print.colourScheme '{pr.ColourScheme}' is not a known scheme, so nothing branches on it.",
                    $"Use one of: {string.Join(", ", PrintColourSchemes)}.");
            }

            if (pr.LineWeightScale.HasValue)
            {
                double v = pr.LineWeightScale.Value;
                if (v <= 0 || v > 4)
                    r.Add(ValidationSeverity.Warning, "DT-142-LW",
                        $"print.lineWeightScale {v} is outside a sensible 0.1..4 range.",
                        "A scale multiplies the pack's declared weights and is clamped to Revit's 1..16.");
            }
        }

        /// <summary>
        /// DT-143 — style-pack hygiene, reported once per library rather than
        /// per drawing type. Covers the three things that were invisible:
        /// a pack referenced by nothing, a duplicate filter rule inside one
        /// pack, and a drawing type that resolves to no pack at all.
        /// </summary>
        public static void ValidateStylePackLibrary(Document doc, ValidationReport r)
        {
            if (doc == null || r == null) return;
            IReadOnlyList<ViewStylePack> packs;
            IReadOnlyList<DrawingType> types;
            try
            {
                packs = ViewStylePackRegistry.ListAll(doc);
                types = DrawingTypeRegistry.ListAll(doc);
            }
            catch { return; }
            if (packs == null || packs.Count == 0) return;

            // ── DT-143-DUP: two rows for one filter inside a single pack.
            // The applier iterates in order and overlays, so the later row
            // silently wins and the earlier one is dead data that still reads
            // as authoritative in the editor.
            foreach (var p in packs)
            {
                if (p?.Filters == null) continue;
                var dupes = p.Filters
                    .Where(f => !string.IsNullOrWhiteSpace(f?.FilterName))
                    .GroupBy(f => f.FilterName, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"{g.Key} (x{g.Count()})")
                    .ToList();
                if (dupes.Count > 0)
                    r.Add(ValidationSeverity.Warning, "DT-143-DUP",
                        $"Style pack '{p.Id}' declares the same filter more than once: {string.Join("; ", dupes)}. "
                        + "The last row wins; the earlier ones are dead but still shown as owned.",
                        "Delete the earlier rows so the pack says what it means.");
            }

            // ── DT-143-ORPHAN: a pack nothing selects. Informational, not a
            // warning — the presentation palettes are deliberately opt-in.
            var referenced = new HashSet<string>(
                types.Where(t => !string.IsNullOrWhiteSpace(t?.ViewStylePackId))
                     .Select(t => t.ViewStylePackId), StringComparer.OrdinalIgnoreCase);
            foreach (var p in packs)
                if (!string.IsNullOrWhiteSpace(p?.Extends)) referenced.Add(p.Extends);
            try
            {
                foreach (var rule in ViewStylePackRegistry.ListRouting(doc))
                    if (!string.IsNullOrWhiteSpace(rule?.StylePackId)) referenced.Add(rule.StylePackId);
            }
            catch { /* routing is optional */ }

            var orphans = packs.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id)
                                        && !referenced.Contains(p.Id))
                               .Select(p => p.Id).ToList();
            if (orphans.Count > 0)
                r.Add(ValidationSeverity.Info, "DT-143-ORPHAN",
                    $"{orphans.Count} style pack(s) are selected by no drawing type, no extends and no routing rule: "
                    + string.Join(", ", orphans) + ".",
                    "Fine for an opt-in palette library — pick one on a project drawing type. Delete any that are genuinely unused.");

            // ── DT-143-NOPACK: a drawing type that resolves to nothing, so
            // its views get no category overrides and no filters whatsoever.
            // This is the condition that left 8 of 93 profiles unstyled and
            // silent before the routing fallback was wired.
            var unstyled = new List<string>();
            foreach (var t in types)
            {
                if (t == null) continue;
                try
                {
                    if (ViewStylePackRegistry.ResolveForDrawingType(doc, t) == null) unstyled.Add(t.Id);
                }
                catch { /* keep going */ }
            }
            if (unstyled.Count > 0)
                r.Add(ValidationSeverity.Warning, "DT-143-NOPACK",
                    $"{unstyled.Count} drawing type(s) resolve to NO style pack — no category overrides and no "
                    + $"filters will be applied to their views: {string.Join(", ", unstyled.Take(12))}"
                    + (unstyled.Count > 12 ? ", …" : "") + ".",
                    "Set viewStylePackId on the profile, or add a routing rule in STING_VIEW_STYLE_PACKS.json.");
        }

        /// <summary>Localised category name for a BIC string, or null.</summary>
        private static string DisplayNameForBic(Document doc, string key)
        {
            try
            {
                if (!Enum.TryParse<BuiltInCategory>(key, true, out var bic)) return null;
                return Category.GetCategory(doc, bic)?.Name;
            }
            catch { return null; }
        }

        /// <summary>
        /// DT-137-SLOTVT — slot viewType against the closed vocabulary
        /// SheetPlacementBridge actually discriminates on.
        /// </summary>
        internal static void ValidateSlotViewTypes(DrawingType dt, ValidationReport r)
        {
            foreach (var slot in dt?.Slots ?? new List<DrawingSlot>())
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.ViewType)) continue;
                if (SheetPlacementBridge.IsKnownSlotViewType(slot.ViewType)) continue;
                r.Add(ValidationSeverity.Warning, "DT-137-SLOTVT",
                    $"Slot '{slot.Label}' declares viewType '{slot.ViewType}', which the placement "
                    + "compatibility check does not recognise — the slot will accept ANY view, which is "
                    + "the opposite of what declaring a viewType implies.",
                    $"Use one of: {string.Join(", ", SheetPlacementBridge.KnownSlotViewTypes)}.");
            }
        }

        private static void ValidatePhase137ProductionRules(DrawingType dt, ValidationReport r)
        {
            ValidateSlotViewTypes(dt, r);

            if (dt?.ProductionRules == null) return;
            var rules = dt.ProductionRules;
            if (rules.Count > 0 && (dt.Slots?.Count ?? 0) > 0)
            {
                int maxSlot = rules.Max(p => p?.SlotIndex ?? -1);
                if (maxSlot >= dt.Slots.Count)
                    r.Add(ValidationSeverity.Warning, "DT-137-SLOT",
                        $"ProductionRule references slotIndex {maxSlot} but profile only has {dt.Slots.Count} slot(s).",
                        "Add slots or lower slotIndex.");
            }
            else if (rules.Count > 1 && (dt.Slots?.Count ?? 0) == 0)
            {
                r.Add(ValidationSeverity.Info, "DT-137-NOSLOTS",
                    $"{rules.Count} production rules declared but profile has no slots — produced views will fall back to sheet-centre placement.");
            }
        }

        private static void ValidatePhase137ManagedPack(Document doc, DrawingType dt, ValidationReport r)
        {
            if (doc == null || string.IsNullOrEmpty(dt?.ViewStylePackId)) return;
            ViewStylePack pack;
            try { pack = ViewStylePackRegistry.Get(doc, dt.ViewStylePackId); }
            catch { return; }
            if (pack == null || !pack.IsManaged) return;

            // PERF-05: prefer the per-batch snapshot when ValidateAll is the
            // caller; fall back to a fresh collector when Validate() is
            // invoked individually.
            try
            {
                bool? snap = SnapshotFor(doc)?.AnyStingSeedTemplate;
                bool anyStingSeed = snap ?? new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Any(v => v.IsTemplate && (v.Name ?? "").StartsWith("STING - ", StringComparison.Ordinal));
                if (!anyStingSeed)
                    r.Add(ValidationSeverity.Warning, "DT-137-MGD-SEED",
                        $"Pack '{pack.Id}' is managed but no 'STING - ' seed templates exist; the syncer may fall back to a non-STING seed view.",
                        "Create at least one STING- prefixed template to seed managed templates from.");
            }
            catch { }

            if (!string.IsNullOrEmpty(pack.PhaseFilter))
            {
                try
                {
                    bool exists;
                    var snapPf = SnapshotFor(doc);
                    if (snapPf != null)
                        exists = snapPf.KnownPhaseFilters.Contains(pack.PhaseFilter);
                    else
                        exists = new FilteredElementCollector(doc)
                            .OfClass(typeof(PhaseFilter))
                            .Cast<PhaseFilter>()
                            .Any(p => string.Equals(p.Name, pack.PhaseFilter, StringComparison.OrdinalIgnoreCase));
                    if (!exists)
                        r.Add(ValidationSeverity.Warning, "DT-137-MGD-PHASE",
                            $"Pack '{pack.Id}' references PhaseFilter '{pack.PhaseFilter}' which does not exist.",
                            "Create the phase filter or update the pack.");
                }
                catch { }
            }
        }

        private static FamilySymbol FindAnnotationFamily(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s =>
                        string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.FamilyName, name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        /// <summary>
        /// Validate every DrawingType in the library + routing-table
        /// coverage. Useful for a one-click "does my project have the
        /// assets to honour every corporate drawing type" audit.
        /// </summary>
        // PERF-05: a small shared snapshot built once per ValidateAll so the
        // 40+ profiles don't each re-run "any STING- seed?" or "is phase
        // filter X loaded?" via fresh FilteredElementCollectors.
        [ThreadStatic] private static ValidationSnapshot _snapshot;

        private static string SnapshotDocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"SnapshotDocKey: {ex.Message}"); return "__unknown__"; }
        }

        /// <summary>
        /// E-10: the snapshot for THIS document, or null. Previously the
        /// [ThreadStatic] field was read unconditionally and cleared outside
        /// any finally, so an exception mid-ValidateAll left doc A's asset
        /// inventory answering doc B's later single-profile validations —
        /// reporting title blocks and view templates that B does not have.
        /// </summary>
        private static ValidationSnapshot SnapshotFor(Document doc)
        {
            var snap = _snapshot;
            if (snap == null) return null;
            return string.Equals(snap.DocKey, SnapshotDocKey(doc), StringComparison.OrdinalIgnoreCase) ? snap : null;
        }

        private sealed class ValidationSnapshot
        {
            /// <summary>E-10: the document this snapshot describes. A
            /// snapshot is only consulted for its own document, so an
            /// abandoned one can never answer for a different model.</summary>
            public string DocKey;
            public bool? AnyStingSeedTemplate;
            public HashSet<string> KnownPhaseFilters    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // PERF: collected once in ValidateAll so the per-DrawingType Has*
            // helpers don't each spin up a fresh FilteredElementCollector
            // (90 types × ~5 lookups = hundreds of full-doc scans otherwise).
            public HashSet<string> TitleBlockFamilies   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> TitleBlockSymbols    = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // "family|symbol"
            public HashSet<string> ViewTemplates        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ViewportTypes        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> FamilyNames          = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public static List<ValidationReport> ValidateAll(Document doc)
        {
            // PERF-05: build the per-doc snapshot once.
            _snapshot = new ValidationSnapshot { DocKey = SnapshotDocKey(doc) };
            try
            {
                // Views — one pass for both the seed-template flag and the
                // template-name set consumed by HasViewTemplate.
                bool anySeed = false;
                foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                {
                    if (!v.IsTemplate) continue;
                    var vn = v.Name ?? "";
                    if (vn.Length > 0) _snapshot.ViewTemplates.Add(vn);
                    if (vn.StartsWith("STING - ", StringComparison.Ordinal)) anySeed = true;
                }
                _snapshot.AnyStingSeedTemplate = anySeed;

                foreach (var pf in new FilteredElementCollector(doc)
                    .OfClass(typeof(PhaseFilter)).Cast<PhaseFilter>())
                {
                    if (!string.IsNullOrEmpty(pf.Name))
                        _snapshot.KnownPhaseFilters.Add(pf.Name);
                }

                // Title blocks — family names + family|symbol pairs.
                foreach (var fs in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                {
                    if (string.IsNullOrEmpty(fs.FamilyName)) continue;
                    _snapshot.TitleBlockFamilies.Add(fs.FamilyName);
                    if (!string.IsNullOrEmpty(fs.Name))
                        _snapshot.TitleBlockSymbols.Add(fs.FamilyName + "|" + fs.Name);
                }

                // Viewport types (ElementType whose family name contains "Viewport").
                foreach (var t in new FilteredElementCollector(doc)
                    .OfClass(typeof(ElementType)).Cast<ElementType>())
                {
                    if (!string.IsNullOrEmpty(t.Name) && t.FamilyName != null
                        && t.FamilyName.IndexOf("Viewport", StringComparison.OrdinalIgnoreCase) >= 0)
                        _snapshot.ViewportTypes.Add(t.Name);
                }

                // Family names (HasAnnotationFamily / section-marker checks).
                foreach (var f in new FilteredElementCollector(doc)
                    .OfClass(typeof(Family)).Cast<Family>())
                {
                    if (!string.IsNullOrEmpty(f.Name)) _snapshot.FamilyNames.Add(f.Name);
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DrawingTypeValidator snapshot: {ex.Message}"); }

            // E-10: the Validate loop used to sit outside any finally, so a
            // throw here skipped the clear and leaked the snapshot.
            List<ValidationReport> reports;
            try { reports = DrawingTypeRegistry.ListAll(doc).Select(t => Validate(doc, t)).ToList(); }
            finally { _snapshot = null; }

            // DT-143 — style-pack library hygiene, once per library rather
            // than once per drawing type: duplicate filter rows inside a pack,
            // packs nothing selects, and drawing types that resolve to no pack
            // at all (which means no category overrides and no filters on
            // their views).
            try
            {
                var packReport = new ValidationReport { DrawingTypeId = "(style packs)" };
                ValidateStylePackLibrary(doc, packReport);
                if (packReport.Issues != null && packReport.Issues.Count > 0) reports.Add(packReport);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ValidateStylePackLibrary: {ex.Message}"); }

            // Routing coverage — flag routing rules pointing at
            // non-existent drawing types.
            var ids = new HashSet<string>(
                DrawingTypeRegistry.ListAll(doc).Select(t => t.Id ?? ""),
                StringComparer.OrdinalIgnoreCase);
            foreach (var rule in DrawingTypeRegistry.ListRouting(doc))
            {
                if (!ids.Contains(rule.DrawingTypeId ?? ""))
                {
                    var r = new ValidationReport { DrawingTypeId = "(routing)" };
                    r.Add(ValidationSeverity.Error, "DT-100",
                        $"Routing rule ({rule.Discipline}/{rule.Phase}/{rule.DocType}) references unknown drawing type '{rule.DrawingTypeId}'.",
                        "Fix the rule's drawingTypeId or add the missing DrawingType.");
                    reports.Add(r);
                }
            }

            // DT-101 — duplicate drawing-type ids in the shipped corporate JSON.
            // The loader collapses them first-wins (so the live library shows
            // none), but records what it dropped so this diagnostic can flag a
            // JSON that ships the same id twice (a merge that re-appended a
            // batch). The duplicate just bloats pickers and, on a project-
            // override merge, used to crash the by-id map.
            try
            {
                // Touch the library first so the loader has run + recorded.
                _ = DrawingTypeRegistry.GetLibrary(doc);
                foreach (var dupId in DrawingTypeRegistry.LastCorporateDuplicateIds)
                {
                    var r = new ValidationReport { DrawingTypeId = dupId };
                    r.Add(ValidationSeverity.Error, "DT-101",
                        $"Drawing-type id '{dupId}' is declared more than once in STING_DRAWING_TYPES.json — only the first is used; the rest were dropped at load.",
                        "Remove the duplicate entr(ies) from STING_DRAWING_TYPES.json.");
                    reports.Add(r);
                }
            }
            catch { /* validator never throws */ }

            // DT-102 — routing discipline value that no drawing type declares.
            // DrawingDispatcher matches discipline by exact case-insensitive
            // equality, so a rule using "Architecture" can never resolve a
            // drawing type that uses the short code "A". Catches the class of
            // bug Phase 184i fixed for "Plumbing"->"P".
            try
            {
                // Accept any discipline a drawing type declares, plus the
                // canonical ISO short codes (a rule may legitimately route a
                // discipline that has no drawing type of its own — routing
                // matches the CALLER's discipline, not a DT's). Only a value
                // outside both sets (a long-form name like "Architecture" or
                // "Plumbing") can never match what callers pass.
                var discInUse = new HashSet<string>(
                    DrawingTypeRegistry.ListAll(doc)
                        .Select(t => (t.Discipline ?? "").Trim())
                        .Where(d => d.Length > 0),
                    StringComparer.OrdinalIgnoreCase);
                discInUse.UnionWith(new[] { "A", "S", "M", "E", "P", "FP", "LV", "G", "H", "MG", "RP" });
                foreach (var rule in DrawingTypeRegistry.ListRouting(doc))
                {
                    var d = (rule.Discipline ?? "").Trim();
                    // "*" and predicate-driven rules are fine; only flag an
                    // explicit literal that no drawing type matches.
                    if (d.Length == 0 || d == "*") continue;
                    if (!string.IsNullOrEmpty(rule.DisciplineMatches)) continue;
                    if (!discInUse.Contains(d))
                    {
                        var r = new ValidationReport { DrawingTypeId = "(routing)" };
                        r.Add(ValidationSeverity.Error, "DT-102",
                            $"Routing rule discipline '{d}' (-> {rule.DrawingTypeId}) is used by no drawing type; the dispatcher matches discipline by exact string, so this rule can never resolve.",
                            "Use the short discipline code (A/S/M/E/P/H/MG/RP/FP/LV/G), or '*', to match the drawing types.");
                        reports.Add(r);
                    }
                }
            }
            catch { /* validator never throws */ }

            // DT-103 — a fully-wildcard routing rule (*/*/*) that precedes
            // other rules. First-match-wins means it shadows everything below
            // it, so the dispatcher only ever returns that one drawing type.
            // A catch-all is only ever valid as the LAST rule.
            try
            {
                var routing = DrawingTypeRegistry.ListRouting(doc).ToList();
                // An axis matches everything when it has no narrowing: either
                // the plain field is "*"/empty with no predicate, OR the
                // predicate is a match-all regex. Catches both a pure */*/*
                // rule and a `.*` regex catch-all — both shadow the rules after.
                bool MatchAllRegex(string p) =>
                    p == ".*" || p == "^.*$" || p == ".*?" || p == "^.+$" || p == ".+";
                bool AxisAny(string plain, string pred) =>
                    string.IsNullOrEmpty(pred)
                        ? (string.IsNullOrEmpty(plain) || plain == "*")
                        : MatchAllRegex(pred);
                bool IsWildcard(DrawingRoutingRule x) =>
                    AxisAny(x.Discipline, x.DisciplineMatches) &&
                    AxisAny(x.Phase,      x.PhaseMatches) &&
                    AxisAny(x.DocType,    x.DocTypeMatches) &&
                    string.IsNullOrEmpty(x.LevelMatches) &&
                    string.IsNullOrEmpty(x.ProjectCodeMatches);
                for (int i = 0; i < routing.Count - 1; i++)
                {
                    if (IsWildcard(routing[i]))
                    {
                        var r = new ValidationReport { DrawingTypeId = "(routing)" };
                        r.Add(ValidationSeverity.Error, "DT-103",
                            $"Catch-all routing rule (*/*/*) -> '{routing[i].DrawingTypeId}' at position {i} shadows the {routing.Count - 1 - i} rule(s) after it; the dispatcher will always return this one.",
                            "Move the catch-all to the end of the routing list, narrow it with discipline/phase/docType, or remove it.");
                        reports.Add(r);
                        break; // one report is enough to surface the problem
                    }
                }
            }
            catch { /* validator never throws */ }

            // DT-104 / DT-105 — the general form of DT-103, from the same
            // Revit-free audit the shipped catalogue is gated by in CI
            // (DrawingRoutingMatcher.Audit): any rule an earlier rule fully
            // covers can never fire, and a predicate regex that does not
            // compile never matches. Dangling ids stay DT-100 above.
            try
            {
                var audit = DrawingRoutingMatcher.Audit(
                    DrawingTypeRegistry.ListRouting(doc).ToList(), DrawingTypeRegistry.ListAll(doc));
                foreach (var s in audit.Shadowed)
                {
                    var r = new ValidationReport { DrawingTypeId = "(routing)" };
                    r.Add(ValidationSeverity.Warning, "DT-104",
                        $"Routing rule {s}; first-match-wins means it can never fire.",
                        "Delete the later rule, or narrow / reorder the earlier one. Project rules are prepended, so a broad project rule shadows corporate rules.");
                    reports.Add(r);
                }
                foreach (var s in audit.InvalidRegex)
                {
                    var r = new ValidationReport { DrawingTypeId = "(routing)" };
                    r.Add(ValidationSeverity.Error, "DT-105",
                        $"Routing rule predicate regex does not compile: {s}. The rule can never match.",
                        "Fix the pattern (patterns are .NET regex, unanchored, case-insensitive).");
                    reports.Add(r);
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Routing audit (DT-104/105): {ex.Message}"); }

            return reports;
        }

        // Revit lookups --------------------------------------------------

        private static bool HasTitleBlockFamily(Document doc, string familyName)
        {
            var snapTbf = SnapshotFor(doc);
            if (snapTbf != null) return snapTbf.TitleBlockFamilies.Contains(familyName ?? "");
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol));
                foreach (var el in col)
                    if (el is FamilySymbol fs
                        && string.Equals(fs.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* ignore */ }
            return false;
        }

        private static bool HasTitleBlockSymbol(Document doc, string familyName, string symbolName)
        {
            var snapTbs = SnapshotFor(doc);
            if (snapTbs != null)
                return snapTbs.TitleBlockSymbols.Contains((familyName ?? "") + "|" + (symbolName ?? ""));
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol));
                foreach (var el in col)
                    if (el is FamilySymbol fs
                        && string.Equals(fs.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(fs.Name, symbolName, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* ignore */ }
            return false;
        }

        private static bool HasViewTemplate(Document doc, string name)
        {
            var snapVt = SnapshotFor(doc);
            if (snapVt != null) return snapVt.ViewTemplates.Contains(name ?? "");
            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(View));
                foreach (var el in col)
                    if (el is View v && v.IsTemplate
                        && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* ignore */ }
            return false;
        }

        private static bool HasViewportType(Document doc, string name)
        {
            // Viewport naming: a STING name is satisfied by its canonical form
            // or any legacy alias (StingViewportTypes.Candidates).
            var candidates = StingViewportTypes.Candidates(name);
            var snapVp = SnapshotFor(doc);
            if (snapVp != null) return candidates.Any(c => snapVp.ViewportTypes.Contains(c));
            try
            {
                return ViewportTypeResolver.Exists(doc, name);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrawingTypeValidator.HasViewportType('{name}'): {ex.Message} -- reported as missing");
                return false;
            }
        }

        private static bool HasAnnotationFamily(Document doc, string familyName)
        {
            var snapFam = SnapshotFor(doc);
            if (snapFam != null) return snapFam.FamilyNames.Contains(familyName ?? "");
            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(Family));
                foreach (var el in col)
                    if (el is Family f
                        && string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* ignore */ }
            return false;
        }

        private static bool IsSectionLikePurpose(string purpose)
        {
            return string.Equals(purpose, DrawingPurpose.Section,   StringComparison.OrdinalIgnoreCase)
                || string.Equals(purpose, DrawingPurpose.Elevation, StringComparison.OrdinalIgnoreCase)
                || string.Equals(purpose, DrawingPurpose.Detail,    StringComparison.OrdinalIgnoreCase);
        }

        // DT-055 / DT-056 — per-slot geometry. The rules live in the
        // Revit-free DrawingSlotGeometry so the CI gate over the shipped
        // catalogue (StingTools.Tags.Tests) runs the same check.
        private static void ValidateSlotGeometry(DrawingType dt, ValidationReport r)
        {
            foreach (var issue in DrawingSlotGeometry.Check(dt.Slots))
            {
                if (issue.Kind == DrawingSlotGeometry.IssueKind.InvalidGeometry)
                    r.Add(ValidationSeverity.Error, issue.Code, issue.Message);
                else if (issue.Kind == DrawingSlotGeometry.IssueKind.OutOfBounds)
                    r.Add(ValidationSeverity.Warning, issue.Code, issue.Message);
            }
        }

        // GAP-K: profile.Purpose says "Plan" but a slot.ViewType is "Section",
        // or vice versa, indicates a bookkeeping mistake in the JSON. The
        // production engine would still produce the slotted view, but its
        // purpose tag wouldn't match the slot type, so downstream filters
        // (browser organizer, sheet packs) place it in surprising places.
        private static void ValidateSlotPurposeAlignment(DrawingType dt, ValidationReport r)
        {
            if (dt?.Slots == null || dt.Slots.Count == 0) return;
            var purpose = (dt.Purpose ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(purpose) ||
                purpose.Equals(DrawingPurpose.Coordination, StringComparison.OrdinalIgnoreCase) ||
                purpose.Equals(DrawingPurpose.Spool, StringComparison.OrdinalIgnoreCase))
                return; // multi-view profiles inherently mix slot types
            foreach (var s in dt.Slots)
            {
                if (s == null || string.IsNullOrEmpty(s.ViewType)) continue;
                if (!s.ViewType.Equals(purpose, StringComparison.OrdinalIgnoreCase)
                    && !IsCompatibleSlotViewType(purpose, s.ViewType))
                {
                    r.Add(ValidationSeverity.Info, "DT-057",
                        $"Slot '{s.Label}' has ViewType '{s.ViewType}' on a {purpose} profile — confirm this is intentional.");
                }
            }
        }

        private static bool IsCompatibleSlotViewType(string purpose, string slotType)
        {
            // A small whitelist of "Plan profile may host an inset Schedule",
            // "Section profile may host an inset Detail", etc. — common
            // multi-view layouts that don't deserve a warning.
            var p = purpose ?? string.Empty;
            var s = slotType ?? string.Empty;
            if (p.Equals(DrawingPurpose.Plan,      StringComparison.OrdinalIgnoreCase) &&
                (s.Equals("Schedule", StringComparison.OrdinalIgnoreCase) ||
                 s.Equals("Legend",   StringComparison.OrdinalIgnoreCase) ||
                 s.Equals("RCP",      StringComparison.OrdinalIgnoreCase)))
                return true;
            if (p.Equals(DrawingPurpose.Section,   StringComparison.OrdinalIgnoreCase) &&
                (s.Equals("Detail",   StringComparison.OrdinalIgnoreCase) ||
                 s.Equals("Plan",     StringComparison.OrdinalIgnoreCase)))
                return true;
            if (p.Equals(DrawingPurpose.ThreeD,    StringComparison.OrdinalIgnoreCase) &&
                (s.Equals("Plan",     StringComparison.OrdinalIgnoreCase) ||
                 s.Equals("ISO",      StringComparison.OrdinalIgnoreCase)))
                return true;
            return false;
        }

        /// <summary>
        /// GAP-L: cross-check DrawingType.Slots[].Label values against the
        /// slot definitions actually embedded in the declared title-block family
        /// (via TB_VIEWPORT_SLOTS_JSON_TXT). Missing labels surface as Warnings
        /// so authors fix the JSON before generation rather than getting a
        /// silent fall-back-to-sheet-origin.
        /// </summary>
        private static void ValidateTitleBlockSlotsVsFamily(Document doc, DrawingType dt, ValidationReport r)
        {
            if (doc == null || dt == null || dt.Slots == null || dt.Slots.Count == 0) return;
            if (string.IsNullOrWhiteSpace(dt.TitleBlockFamily)) return;
            try
            {
                // Filter to only slots declared on the matching family.
                var allSlots = TitleBlockSlotLoader.ReadAll(doc);
                var familySlots = allSlots
                    .Where(s => string.Equals(s.TitleBlockFamily, dt.TitleBlockFamily,
                                              StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (familySlots.Count == 0)
                {
                    // The family is loaded but carries no slot JSON — the cross-
                    // check cannot run. Surface as Info (not Warning) because
                    // many pre-STING title blocks legitimately have no slot data.
                    r.Add(ValidationSeverity.Info, "DT-SLT-01",
                        $"Title block family '{dt.TitleBlockFamily}' has no slot definitions embedded " +
                        "(TB_VIEWPORT_SLOTS_JSON_TXT not set) — slot-label cross-check skipped. " +
                        "Embed slot JSON in the family to enable.",
                        "Open the title-block family in Family Editor, add the shared parameter " +
                        "TB_VIEWPORT_SLOTS_JSON_TXT, and populate it with a JSON array of slot objects.");
                    return;
                }

                // Build a lookup set of live labels (case-insensitive).
                var liveLabels = new HashSet<string>(
                    familySlots.Select(s => s.Label ?? "").Where(l => l.Length > 0),
                    StringComparer.OrdinalIgnoreCase);
                var liveLabelList = string.Join(", ", liveLabels.OrderBy(l => l));

                foreach (var slot in dt.Slots)
                {
                    if (string.IsNullOrWhiteSpace(slot?.Label)) continue;
                    if (!liveLabels.Contains(slot.Label))
                    {
                        r.Add(ValidationSeverity.Warning, "DT-SLT-02",
                            $"DrawingType slot label '{slot.Label}' not found in loaded family " +
                            $"'{dt.TitleBlockFamily}' slots ({liveLabelList}). " +
                            "Viewport placement will fall back to sheet origin.",
                            $"Update the slot label to match one of: {liveLabelList}, or add the " +
                            $"missing slot to the title-block family's TB_VIEWPORT_SLOTS_JSON_TXT.");
                    }
                }
            }
            catch { /* validator must never throw */ }
        }

        private static void ValidateSlotOverlaps(DrawingType dt, ValidationReport r)
        {
            if (dt?.Slots == null || dt.Slots.Count < 2) return;
            try
            {
                // AABB overlap via the shared Revit-free helper (see
                // ValidateSlotGeometry). Edges shared within
                // DrawingSlotGeometry.Tolerance are not an overlap.
                foreach (var issue in DrawingSlotGeometry.Check(dt.Slots))
                {
                    if (issue.Kind != DrawingSlotGeometry.IssueKind.Overlap) continue;
                    r.Add(ValidationSeverity.Warning, issue.Code, issue.Message,
                        "Adjust normX/normY/normW/normH to eliminate overlap, or confirm intentional side-by-side layout (e.g. BOM strip adjacent to ISO view).");
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ValidateSlotOverlaps('{dt.Id}'): {ex.Message}"); }
        }
    }
}
