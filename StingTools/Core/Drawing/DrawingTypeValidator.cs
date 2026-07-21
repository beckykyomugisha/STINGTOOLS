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
            try { concreteFam = TitleBlockResolver.ToConcreteFamily(doc, dt, declaredFam); } catch (Exception ex) { StingTools.Core.StingLog.Warn($"Suppressed: {ex.Message}"); }
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
                    r.Add(ValidationSeverity.Warning, "DT-021",
                        $"Viewport type '{dt.ViewportTypeName}' not found.",
                        "Duplicate an existing Viewport Type and name it to match, or clear the field.");
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
                foreach (var s in dt.Slots) ValidateSlot(s, r);

            // Pattern sanity ---------------------------------------------
            if (string.IsNullOrWhiteSpace(dt.SheetNumberPattern))
                r.Add(ValidationSeverity.Warning, "DT-060",
                    "sheetNumberPattern is empty — generated sheets may collide in numbering.");
            if (string.IsNullOrWhiteSpace(dt.SheetNamePattern))
                r.Add(ValidationSeverity.Info, "DT-061",
                    "sheetNamePattern is empty — sheets will be named by Revit's default.");

            // DT-095: scale must be positive on every purpose except 3D /
            // Perspective, where assigning view.Scale = 0 throws and the
            // engine logs + skips the assignment by design.
            if (dt.Scale <= 0)
            {
                bool isThreeD = string.Equals(dt.Purpose, DrawingPurpose.ThreeD, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(dt.Purpose, "Perspective", StringComparison.OrdinalIgnoreCase);
                if (!isThreeD)
                    r.Add(ValidationSeverity.Warning, "DT-095",
                        $"Scale is {dt.Scale} — must be a positive integer for non-3D drawing types. Set scale > 0 or use purpose '3D'/'Perspective' for views where scale is not applicable.");
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

        private static void ValidatePhase137ProductionRules(DrawingType dt, ValidationReport r)
        {
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
            var snapVp = SnapshotFor(doc);
            if (snapVp != null) return snapVp.ViewportTypes.Contains(name ?? "");
            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(ElementType));
                foreach (var el in col)
                    if (el is ElementType t
                        && t.FamilyName != null
                        && t.FamilyName.IndexOf("Viewport", StringComparison.OrdinalIgnoreCase) >= 0
                        && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* ignore */ }
            return false;
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

        private static void ValidateSlot(DrawingSlot s, ValidationReport r)
        {
            if (s.NormX < 0 || s.NormY < 0 || s.NormW <= 0 || s.NormH <= 0)
                r.Add(ValidationSeverity.Error, "DT-055",
                    $"Slot '{s.Label}' has invalid geometry (normX={s.NormX} normY={s.NormY} normW={s.NormW} normH={s.NormH}).");
            if (s.NormX + s.NormW > 1.0001 || s.NormY + s.NormH > 1.0001)
                r.Add(ValidationSeverity.Warning, "DT-056",
                    $"Slot '{s.Label}' extends beyond the drawable zone (normX+W={s.NormX + s.NormW:F2} normY+H={s.NormY + s.NormH:F2}).");
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
                for (int i = 0; i < dt.Slots.Count; i++)
                {
                    for (int j = i + 1; j < dt.Slots.Count; j++)
                    {
                        var a = dt.Slots[i];
                        var b = dt.Slots[j];
                        if (a == null || b == null) continue;
                        // AABB overlap test
                        bool overlapX = a.NormX < b.NormX + b.NormW && a.NormX + a.NormW > b.NormX;
                        bool overlapY = a.NormY < b.NormY + b.NormH && a.NormY + a.NormH > b.NormY;
                        if (overlapX && overlapY)
                        {
                            // Compute overlap area as a fraction of page
                            double ox = Math.Min(a.NormX + a.NormW, b.NormX + b.NormW) - Math.Max(a.NormX, b.NormX);
                            double oy = Math.Min(a.NormY + a.NormH, b.NormY + b.NormH) - Math.Max(a.NormY, b.NormY);
                            double area = Math.Round(ox * oy * 100, 1);
                            r.Add(ValidationSeverity.Warning, "DT-SLT-03",
                                $"Slots [{i}] '{a.Label ?? $"slot{i}"}' and [{j}] '{b.Label ?? $"slot{j}"}' overlap by {area}% of sheet area.",
                                "Adjust normX/normY/normW/normH to eliminate overlap, or confirm intentional side-by-side layout (e.g. BOM strip adjacent to ISO view).");
                        }
                    }
                }
            }
            catch { /* validator must never throw */ }
        }
    }
}
