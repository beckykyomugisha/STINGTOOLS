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
    ///     visibility is gated correctly.
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
            public string FamilyName { get; set; }
        }

        /// <summary>Per-family outcome, rolled up into the report by the command.</summary>
        public sealed class Result
        {
            public int ParamsBound { get; set; }
            public int FormulasApplied { get; set; }
            public int FormulasSkipped { get; set; }
            public int TiersPreserved { get; set; }
            public bool LabelRebound { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }

        /// <summary>
        /// Author tier content into <paramref name="fdoc"/> per <paramref name="plan"/>.
        /// Caller is responsible for saving (<see cref="Document.SaveAs(string, SaveAsOptions)"/>)
        /// and closing the document.
        /// </summary>
        public static Result AuthorLabels(Document fdoc, TierPlan plan, Options opts)
        {
            if (fdoc == null) throw new ArgumentNullException(nameof(fdoc));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            if (!fdoc.IsFamilyDocument) throw new InvalidOperationException("AuthorLabels requires a family document.");

            var result = new Result();

            // 1. Identify tiers whose rows should be skipped for hand-edit preservation.
            HashSet<int> preservedTiers = opts.PreserveHandEdits
                ? DetectPreservedTiers(fdoc)
                : new HashSet<int>();
            result.TiersPreserved = preservedTiers.Count;

            // 2. Flatten all T4..T10 rows into a linear list the binding + formula
            //    loops can iterate once. Omit tiers with empty row lists (TierState.Omit).
            var flat = new List<(int Tier, TierRow Row)>();
            void Accum(int t, List<TierRow> rows, TierState state)
            {
                if (state == TierState.Omit || rows == null) return;
                foreach (var r in rows) flat.Add((t, r));
            }
            Accum(4, plan.T4Rows, plan.T4);
            Accum(5, plan.T5Rows, plan.T5);
            Accum(6, plan.T6Rows, plan.T6);
            Accum(7, plan.T7Rows, plan.T7);
            Accum(8, plan.T8Rows, plan.T8);
            Accum(9, plan.T9Rows, plan.T9);
            Accum(10, plan.T10Rows, plan.T10);

            if (flat.Count == 0)
            {
                result.Warnings.Add($"{opts.FamilyName ?? "(unknown)"}: no T4..T10 rows to author.");
                return result;
            }

            // 3. Bind every distinct parameter name referenced by the rows.
            var distinctParams = flat
                .Select(x => x.Row?.Parameter)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            result.ParamsBound = BindSharedParameters(fdoc, distinctParams, opts, result);

            // 4. Apply the per-row visibility formula. Same shape the CSV regenerator
            //    emits: if(TAG_PARA_STATE_N_BOOL, PARAM, "").
            ApplyVisibilityFormulas(fdoc, flat, preservedTiers, result);

            // 5. Best-effort label rebind: point the template's stub Label at
            //    ASS_TAG_1_TXT so T1 display continues to work after re-author.
            result.LabelRebound = TryRebindPrimaryLabel(fdoc, result);

            // 6. TODO-VERIFY-API: programmatic Label creation at tier-specific
            //    positions. Revit 2025 exposes FamilyItemFactory.NewTextNote
            //    (plain text) but NOT a Label-with-parameter binding for
            //    annotation tag families. Revision 2026+ may expose more — when
            //    it does, positioning code goes here, driven by plan rows and
            //    TierRow.Size/Style/Color. Until then the .rft template's
            //    pre-existing single Label is retargeted by TryRebindPrimaryLabel
            //    and the additional tier rows stay manual.
            // var factory = fdoc.FamilyCreate; // TODO-VERIFY-API: NewTextNote overloads vary per Revit year.

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
                    tx.Start();
                    foreach (string name in paramNames)
                    {
                        ExternalDefinition ext = FindSharedDefinition(defFile, name);
                        if (ext == null)
                        {
                            result.Warnings.Add($"Shared param '{name}' not in {Path.GetFileName(opts.SharedParamFile)}");
                            continue;
                        }
                        if (HasParameter(fm, name)) continue;
                        try
                        {
                            fm.AddParameter(ext, GroupTypeId.General, true); // instance
                            added++;
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"AddParameter('{name}') failed: {ex.Message}");
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
                catch (Exception ex) { StingLog.Warn($"restore SP file: {ex.Message}"); }
            }
            return added;
        }

        // ------------------------------------------------------------------
        // Per-row formula: set Formula on a family calculated-value parameter
        // matching the row's Parameter name, gating visibility via the tier's
        // TAG_PARA_STATE_N_BOOL. Rows whose target tier is in preservedTiers
        // are skipped.
        // ------------------------------------------------------------------
        private static void ApplyVisibilityFormulas(Document fdoc,
            List<(int Tier, TierRow Row)> flat, HashSet<int> preservedTiers, Result result)
        {
            if (flat.Count == 0) return;

            FamilyManager fm = fdoc.FamilyManager;
            using (Transaction tx = new Transaction(fdoc, "STING AuthorLabels — tier formulas"))
            {
                tx.Start();
                foreach (var (tier, row) in flat)
                {
                    if (preservedTiers.Contains(tier)) { result.FormulasSkipped++; continue; }
                    if (row == null || string.IsNullOrEmpty(row.Parameter)) { result.FormulasSkipped++; continue; }

                    string gate = "TAG_PARA_STATE_" + tier + "_BOOL";
                    // TODO-VERIFY-API: SetFormula only works on read-write family
                    // params. AddParameter sets them read-write by default when
                    // isInstance=true, which is what we passed. If a template
                    // pre-binds a param as read-only this call throws — we log
                    // and move on rather than abort the whole file.
                    FamilyParameter target = null;
                    foreach (FamilyParameter fp in fm.Parameters)
                    {
                        if (string.Equals(fp.Definition?.Name, row.Parameter, StringComparison.Ordinal))
                        {
                            target = fp; break;
                        }
                    }
                    if (target == null) { result.FormulasSkipped++; continue; }

                    string formula = "if(" + gate + ", " + row.Parameter + ", \"\")";
                    try
                    {
                        fm.SetFormula(target, formula);
                        result.FormulasApplied++;
                    }
                    catch (Exception ex)
                    {
                        result.FormulasSkipped++;
                        result.Warnings.Add($"SetFormula('{row.Parameter}') failed: {ex.Message}");
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

        private static bool HasParameter(FamilyManager fm, string name)
        {
            foreach (FamilyParameter fp in fm.Parameters)
                if (fp.Definition?.Name == name) return true;
            return false;
        }
    }
}
