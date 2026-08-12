using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Wave-1 commit 2: Revit-API wrapper that takes a parsed <see cref="TierPlan"/>
    /// (from <see cref="TagConfigCsvReader"/>) and authors the corresponding
    /// tier-row content inside a tag <see cref="Document"/> (FamilyDocument).
    /// </summary>
    /// <remarks>
    /// Scope of work that DOES land here:
    ///   • Bind every shared parameter referenced in the plan's T4..T10 rows.
    ///   • Apply the CSV-derived <c>if(TAG_PARA_STATE_N_BOOL, PARAM, "")</c>
    ///     calculated-value formula on each bound family parameter so tier
    ///     visibility is gated correctly. The gate is tested BARE because STING
    ///     stores the BOOL gates as YESNO (v5.4+) — YESNO is Revit's native
    ///     <c>if()</c> condition type. <see cref="TagConfig.GateToken"/> still
    ///     picks the form per storage type (YESNO ⇒ bare, legacy TEXT ⇒
    ///     <c>= "Yes"</c>) so it self-heals any family still holding a gate as
    ///     TEXT.
    ///   • Honour <c>preserveHandEdits</c>: when any Dimension/TextNote in the
    ///     family has a non-default (non-origin) position we skip formula
    ///     re-writes for the rows that map to that tier, leaving a user's hand
    ///     layout alone.
    ///   • Save the family via <see cref="Document.SaveAs(string, SaveAsOptions)"/>.
    ///
    /// Scope that stays manual (documented Revit API limitation, same reason
    /// the existing <c>TryRebindLabel</c> in TagFamilyCreatorCommand is
    /// best-effort): creating NEW annotation Label elements at specific X/Y
    /// positions in a tag .rft template. Where that is attempted below it is
    /// marked with <c>// TODO-VERIFY-API</c>. If the plugin ever gains the
    /// ability to create labels programmatically, the positioning block is
    /// the only place that needs to change — the binding + formula work done
    /// here is already exercised by existing commands (AddSharedParameters in
    /// TagFamilyCreatorCommand:1538) and is stable.
    ///
    /// Called from <see cref="CreateTagFamiliesCommand"/> via
    /// <see cref="HandoverModeHelper.GetAllTagConfigCsvs"/> → CSV read →
    /// one call to <see cref="AuthorLabels"/> per family document.
    /// </remarks>
    internal static class FamilyLabelAuthor
    {
        /// <summary>Options passed from the outer command.</summary>
        public sealed class Options
        {
            public Application App { get; set; }
            public string SharedParamFile { get; set; }
            public bool PreserveHandEdits { get; set; }
            /// <summary>
            /// When true, warning-tier rows (tier 8+) are also subject to
            /// hand-edit preservation; when false only T4-T7 tiers are
            /// protected by <see cref="PreserveHandEdits"/>.
            /// </summary>
            public bool PreserveHandWarnings { get; set; }
            public string FamilyName { get; set; }
        }

        /// <summary>
        /// A single pattern (Handover or DesignConstruction) paired with the
        /// project-level YESNO selector BOOL that gates its T4-T10 rows when
        /// the family is dual-wired. <see cref="GateParam"/> may be null/empty
        /// for the single-mode back-compat path, in which case tier visibility
        /// is gated on <c>TAG_PARA_STATE_N_BOOL</c> alone.
        /// </summary>
        public sealed class ModePlan
        {
            public string Mode { get; set; }
            public string GateParam { get; set; }
            public TierPlan Plan { get; set; }
        }

        /// <summary>Per-family outcome, rolled up into the report by the command.</summary>
        public sealed class Result
        {
            public int ParamsBound { get; set; }
            public int FormulasApplied { get; set; }
            public int FormulasSkipped { get; set; }
            public int TiersPreserved { get; set; }
            public bool LabelRebound { get; set; }
            /// <summary>Number of warning-row tier formulas applied.</summary>
            public int WarningsApplied { get; set; }
            /// <summary>Number of warning-row tier formulas skipped (hand-edit preserved).</summary>
            public int WarningsSkipped { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }

        /// <summary>
        /// Single-mode entry point (back-compat). Delegates to
        /// <see cref="AuthorLabelsMulti"/> with a no-gate plan so existing
        /// callers that only know about one mode keep working unchanged.
        /// </summary>
        public static Result AuthorLabels(Document fdoc, TierPlan plan, Options opts)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var one = new List<ModePlan> { new ModePlan { Mode = "", GateParam = null, Plan = plan } };
            return AuthorLabelsMulti(fdoc, one, opts);
        }

        /// <summary>
        /// Dual-wire entry point: stamps every <paramref name="modePlans"/>
        /// entry into the same family document, AND-gating each row's
        /// visibility formula with the entry's <see cref="ModePlan.GateParam"/>.
        /// When a source parameter appears in more than one mode it gets a
        /// single OR-merged formula of the shape
        /// <c>if(or(and(stateN, gateA), and(stateM, gateB), …), PARAM, "")</c>
        /// — each gate carries its storage-type-correct condition form (bare for
        /// the YESNO gates STING ships; see <see cref="TagConfig.GateToken"/>).
        /// </summary>
        public static Result AuthorLabelsMulti(Document fdoc,
            IEnumerable<ModePlan> modePlans, Options opts)
        {
            if (fdoc == null) throw new ArgumentNullException(nameof(fdoc));
            if (modePlans == null) throw new ArgumentNullException(nameof(modePlans));
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            if (!fdoc.IsFamilyDocument) throw new InvalidOperationException("AuthorLabels requires a family document.");

            var result = new Result();

            HashSet<int> preservedTiers = opts.PreserveHandEdits
                ? DetectPreservedTiers(fdoc)
                : new HashSet<int>();
            result.TiersPreserved = preservedTiers.Count;

            // Flatten every (tier, row, gate) triple across every mode plan.
            // One entry per row so same-parameter-across-modes is handled by
            // the gate accumulator below, not by dedup here.
            var flat = new List<(int Tier, TierRow Row, string Gate)>();
            foreach (ModePlan mp in modePlans)
            {
                if (mp?.Plan == null) continue;
                void Accum(int t, List<TierRow> rows, TierState state)
                {
                    if (state == TierState.Omit || rows == null) return;
                    foreach (var r in rows) flat.Add((t, r, mp.GateParam));
                }
                // T3 = the per-family engineering block. Excluded while the
                // universal master was the only authoring route; it is exactly
                // what a per-family author is for.
                Accum(3,  mp.Plan.T3Rows,  mp.Plan.T3);
                Accum(4,  mp.Plan.T4Rows,  mp.Plan.T4);
                Accum(5,  mp.Plan.T5Rows,  mp.Plan.T5);
                Accum(6,  mp.Plan.T6Rows,  mp.Plan.T6);
                Accum(7,  mp.Plan.T7Rows,  mp.Plan.T7);
                Accum(8,  mp.Plan.T8Rows,  mp.Plan.T8);
                Accum(9,  mp.Plan.T9Rows,  mp.Plan.T9);
                Accum(10, mp.Plan.T10Rows, mp.Plan.T10);
            }

            if (flat.Count == 0)
            {
                result.Warnings.Add($"{opts.FamilyName ?? "(unknown)"}: no T3..T10 rows to author.");
                return result;
            }

            // Bind every parameter referenced by any row PLUS each distinct
            // mode gate BOOL, so a later SetFormula(gate=…) resolves.
            var distinctParams = flat
                .Select(x => x.Row?.Parameter)
                .Where(s => !string.IsNullOrEmpty(s))
                .Concat(flat.Select(x => x.Gate).Where(s => !string.IsNullOrEmpty(s)))
                // The TIER gates too. Every formula tests TAG_PARA_STATE_n_BOOL,
                // and a formula referencing a parameter the family does not carry
                // does not fail loudly — SetFormula throws and the row is left
                // ungated, which looks exactly like the master's current state.
                .Concat(flat.Select(x => "TAG_PARA_STATE_" + x.Tier + "_BOOL"))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            result.ParamsBound = BindSharedParameters(fdoc, distinctParams, opts, result);

            ApplyVisibilityFormulas(fdoc, flat, preservedTiers, result);

            result.LabelRebound = TryRebindPrimaryLabel(fdoc, result);
            return result;
        }

        // ------------------------------------------------------------------
        // Hand-edit detection
        // ------------------------------------------------------------------
        private static HashSet<int> DetectPreservedTiers(Document fdoc)
        {
            // Heuristic: if the family has ANY Dimension or TextNote whose
            // reported position is not at the family origin, we treat the
            // family as hand-edited and preserve all tier formulas. The reader
            // + author can still bind shared params idempotently (AddParameter
            // skips existing), so hand-edit families keep up-to-date bindings
            // without losing manual layout.
            //
            // TODO-VERIFY-API: Dimension.Origin is the canonical readable
            // position in Revit 2025; TextNote.Coord is what the API exposes.
            // Confirm both fields survive a re-load cycle on 2026/2027.
            var preserved = new HashSet<int>();
            try
            {
                bool anyMoved = false;

                var texts = new FilteredElementCollector(fdoc)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>()
                    .ToList();
                foreach (var tn in texts)
                {
                    try
                    {
                        XYZ p = tn.Coord;           // TODO-VERIFY-API: .Coord is the 2025 accessor.
                        if (p != null && !p.IsAlmostEqualTo(XYZ.Zero)) { anyMoved = true; break; }
                    }
                    catch { /* tolerate API surface drift between Revit years */ }
                }

                if (!anyMoved)
                {
                    var dims = new FilteredElementCollector(fdoc)
                        .OfClass(typeof(Dimension))
                        .Cast<Dimension>()
                        .ToList();
                    foreach (var d in dims)
                    {
                        try
                        {
                            XYZ p = d.Origin;
                            if (p != null && !p.IsAlmostEqualTo(XYZ.Zero)) { anyMoved = true; break; }
                        }
                        catch { /* tolerate API surface drift */ }
                    }
                }

                if (anyMoved)
                    for (int t = 4; t <= 10; t++) preserved.Add(t);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FamilyLabelAuthor.DetectPreservedTiers: {ex.Message}");
            }
            return preserved;
        }

        // ------------------------------------------------------------------
        // Shared-parameter binding — mirrors the proven pattern from
        // TagFamilyCreatorCommand.AddSharedParameters (line 1538) but scoped
        // to the row set supplied by the reader. Must run in its own
        // Transaction because FamilyManager.AddParameter is a write.
        // ------------------------------------------------------------------
        private static int BindSharedParameters(Document fdoc,
            List<string> paramNames, Options opts, Result result)
        {
            if (paramNames == null || paramNames.Count == 0) return 0;
            if (opts.App == null || string.IsNullOrEmpty(opts.SharedParamFile))
            {
                result.Warnings.Add("Cannot bind shared params: App or SharedParamFile not supplied.");
                return 0;
            }
            if (!File.Exists(opts.SharedParamFile))
            {
                result.Warnings.Add($"Shared parameter file missing: {opts.SharedParamFile}");
                return 0;
            }

            string originalSpFile = opts.App.SharedParametersFilename;
            int added = 0;
            try
            {
                opts.App.SharedParametersFilename = opts.SharedParamFile;
                DefinitionFile defFile = opts.App.OpenSharedParameterFile();
                if (defFile == null)
                {
                    result.Warnings.Add($"OpenSharedParameterFile returned null for {opts.SharedParamFile}");
                    return 0;
                }

                FamilyManager fm = fdoc.FamilyManager;
                using (Transaction tx = new Transaction(fdoc, "STING AuthorLabels — bind tier params"))
                {
                    TagParamInjector.InstallSwallower(tx); // Phase 196 — before Start
                    tx.Start();
                    var idx = TagParamInjector.BuildIndex(fdoc);
                    foreach (string name in paramNames)
                    {
                        ExternalDefinition ext = FindSharedDefinition(defFile, name);
                        if (ext == null)
                        {
                            result.Warnings.Add($"Shared param '{name}' not in {Path.GetFileName(opts.SharedParamFile)}");
                            continue;
                        }
                        // Phase 196: pre-skip TEXT↔YESNO conflicts so a stale MR file
                        // can't raise the unrecoverable "cannot be added" modal here.
                        switch (TagParamInjector.EnsureFamilyParam(fm, ext, idx, GroupTypeId.General, true))
                        {
                            case TagParamInjector.InjectResult.Added:
                                added++;
                                break;
                            case TagParamInjector.InjectResult.SkippedConflict:
                                result.Warnings.Add($"'{name}': type conflict with the family's existing definition — kept existing (TEXT↔YESNO drift)");
                                break;
                            // SkippedExists / Failed: no-op (Failed already logged by the injector).
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"BindSharedParameters: {ex.Message}");
                StingLog.Error("FamilyLabelAuthor.BindSharedParameters", ex);
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrEmpty(originalSpFile))
                        opts.App.SharedParametersFilename = originalSpFile;
                }
                catch (Exception ex2) { StingLog.Warn($"restore SP file: {ex2.Message}"); }
            }
            return added;
        }

        // ------------------------------------------------------------------
        // Per-row formula.
        //
        // THE TARGET. Each row is TWO parameters: TierRow.Name is the calculated
        // value that sits in the label and HOLDS the formula, TierRow.Parameter
        // is the shared parameter the formula READS. This method used to key on
        // Parameter and emit if(gate, COMM_STATE_TXT, "") onto COMM_STATE_TXT
        // itself — self-referential, so Revit rejects it as a circular chain, or
        // accepts it and the source value is destroyed. Name was never used. That
        // is the likeliest reason this file had no callers, and it is fixed here:
        // the formula goes on Name, and Name is created as an INSTANCE text
        // parameter when the family does not already carry it (instance, because
        // the value varies per tagged element; a type parameter also could not
        // reference the instance source it reads).
        //
        // THE GATE FORM. TagConfig.GateToken resolves each gate's condition form
        // from its storage type so the emitted formula never trips Revit's
        // "Inconsistent Units": YESNO gates stay bare, a legacy TEXT gate becomes
        // `gate = "Yes"`.
        //
        // MULTI-MODE. When Handover and Design & Construction both list the same
        // calculated value, the gates OR-merge into
        // if(or(and(stateN, gateA), and(stateM, gateB)), SOURCE, "").
        //
        // Rows whose target tier is in preservedTiers are skipped.
        // ------------------------------------------------------------------
        private sealed class RowAuthoring
        {
            public string Source;               // the parameter the formula reads
            public string DeclaredFormula;      // the CSV's Formula column, if any
            public List<string> Gates = new List<string>();
        }

        private static void ApplyVisibilityFormulas(Document fdoc,
            List<(int Tier, TierRow Row, string Gate)> flat,
            HashSet<int> preservedTiers, Result result)
        {
            if (flat.Count == 0) return;

            FamilyManager fm = fdoc.FamilyManager;

            var byCalcValue = new Dictionary<string, RowAuthoring>(StringComparer.Ordinal);
            int skippedRows = 0;
            foreach (var (tier, row, modeGate) in flat)
            {
                if (preservedTiers.Contains(tier)) { skippedRows++; continue; }
                if (row == null || string.IsNullOrEmpty(row.Parameter)) { skippedRows++; continue; }
                if (string.IsNullOrEmpty(row.Name))
                {
                    // No calculated-value name means there is nothing to put the
                    // formula ON. Authoring it onto the source is what this fix
                    // exists to stop, so the row is reported rather than bodged.
                    skippedRows++;
                    result.Warnings.Add($"T{tier} row '{row.Parameter}': no calculated-value name in the CSV — cannot author a gated formula.");
                    continue;
                }

                string stateTok = TagConfig.GateToken(fm, "TAG_PARA_STATE_" + tier + "_BOOL");
                string gateExpr = string.IsNullOrEmpty(modeGate)
                    ? stateTok
                    : "and(" + stateTok + ", " + TagConfig.GateToken(fm, modeGate) + ")";

                RowAuthoring ra;
                if (!byCalcValue.TryGetValue(row.Name, out ra))
                {
                    ra = new RowAuthoring { Source = row.Parameter, DeclaredFormula = row.Formula };
                    byCalcValue[row.Name] = ra;
                }
                else if (!string.Equals(ra.Source, row.Parameter, StringComparison.Ordinal))
                {
                    // Same calculated value fed by two different sources across
                    // modes. Only one formula can win, so say which.
                    result.Warnings.Add($"'{row.Name}' is declared against both {ra.Source} and {row.Parameter}; keeping {ra.Source}.");
                }
                if (!ra.Gates.Contains(gateExpr, StringComparer.Ordinal)) ra.Gates.Add(gateExpr);
            }

            using (Transaction tx = new Transaction(fdoc, "STING AuthorLabels — tier formulas"))
            {
                tx.Start();
                result.FormulasSkipped += skippedRows;

                foreach (var kv in byCalcValue)
                {
                    string calcName = kv.Key;
                    RowAuthoring ra = kv.Value;

                    FamilyParameter target = FindParameter(fm, calcName);
                    if (target == null)
                    {
                        try
                        {
                            target = fm.AddParameter(calcName, GroupTypeId.General,
                                                     SpecTypeId.String.Text, true /* instance */);
                            result.ParamsBound++;
                        }
                        catch (Exception ex)
                        {
                            result.FormulasSkipped++;
                            result.Warnings.Add($"could not create calculated value '{calcName}': {ex.Message}");
                            continue;
                        }
                    }

                    // Prefer the formula the CSV DECLARES — it is the reviewed
                    // text, and re-deriving it is what went wrong before. It only
                    // applies when this row has a single, unmodified tier gate;
                    // a mode gate or an OR-merge has to be composed here.
                    string formula;
                    bool singlePlainGate = ra.Gates.Count == 1;
                    if (singlePlainGate && !string.IsNullOrEmpty(ra.DeclaredFormula))
                    {
                        formula = ra.DeclaredFormula;
                    }
                    else
                    {
                        string combined = ra.Gates.Count == 1
                            ? ra.Gates[0]
                            : "or(" + string.Join(", ", ra.Gates) + ")";
                        formula = "if(" + combined + ", " + ra.Source + ", \"\")";
                    }

                    if (string.Equals(calcName, ra.Source, StringComparison.Ordinal))
                    {
                        // Belt and braces: if a CSV ever names a row after its own
                        // source, refuse rather than write the circular formula.
                        result.FormulasSkipped++;
                        result.Warnings.Add($"'{calcName}' names its own source parameter — refusing to write a self-referential formula.");
                        continue;
                    }

                    try
                    {
                        fm.SetFormula(target, formula);
                        result.FormulasApplied++;
                    }
                    catch (Exception ex)
                    {
                        result.FormulasSkipped++;
                        result.Warnings.Add($"SetFormula('{calcName}') failed: {ex.Message}");
                    }
                }
                tx.Commit();
            }
        }

        // ------------------------------------------------------------------
        // Best-effort label rebind: same approach TagFamilyCreatorCommand
        // (line 1652) uses today. Kept here so the author presents a single
        // entry point to the command — TryRebindLabel is private in the
        // existing command so we duplicate rather than expose it.
        // ------------------------------------------------------------------
        private static bool TryRebindPrimaryLabel(Document fdoc, Result result)
        {
            try
            {
                FamilyManager fm = fdoc.FamilyManager;
                FamilyParameter tagParam = null;
                foreach (FamilyParameter fp in fm.Parameters)
                {
                    if (fp.Definition?.Name == ParamRegistry.TAG1) { tagParam = fp; break; }
                }
                if (tagParam == null) return false;

                using (Transaction tx = new Transaction(fdoc, "STING AuthorLabels — rebind primary label"))
                {
                    tx.Start();
                    var dims = new FilteredElementCollector(fdoc)
                        .OfClass(typeof(Dimension))
                        .Cast<Dimension>()
                        .ToList();
                    foreach (Dimension d in dims)
                    {
                        try
                        {
                            // TODO-VERIFY-API: FamilyLabel setter throws when the
                            // dimension is not label-capable; behaviour matches
                            // the existing TryRebindLabel.
                            d.FamilyLabel = tagParam;
                            tx.Commit();
                            return true;
                        }
                        catch { /* try next dimension */ }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"TryRebindPrimaryLabel: {ex.Message}");
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Small helpers shared with the outer command's style.
        // ------------------------------------------------------------------
        private static ExternalDefinition FindSharedDefinition(DefinitionFile defFile, string paramName)
        {
            foreach (DefinitionGroup g in defFile.Groups)
                foreach (Definition d in g.Definitions)
                    if (d.Name == paramName && d is ExternalDefinition ext) return ext;
            return null;
        }

        /// <summary>
        /// Locate an already-bound family parameter by name, or null. Exposed
        /// <c>internal</c> so the title-block seed-augment path
        /// (<see cref="StingTools.Core.Drawing.TitleBlockFactory"/>) can reuse
        /// the same idempotency check when opening a pre-authored seed .rfa that
        /// already carries its shared parameters — see the shared-param binding
        /// idiom in <see cref="BindSharedParameters"/>.
        /// </summary>
        internal static FamilyParameter FindParameter(FamilyManager fm, string name)
        {
            foreach (FamilyParameter fp in fm.Parameters)
                if (fp.Definition?.Name == name) return fp;
            return null;
        }

        internal static bool HasParameter(FamilyManager fm, string name)
            => FindParameter(fm, name) != null;
    }
}
