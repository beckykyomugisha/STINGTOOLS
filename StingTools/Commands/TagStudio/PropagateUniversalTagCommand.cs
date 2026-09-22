// ============================================================================
// PropagateUniversalTagCommand.cs — Universal-tag propagation conveyor.
//
//   *** Phase 195 — Universal Tag pivot ***
//
// The original goal was "build one master, auto-propagate a BESPOKE tiered label
// to all 206 tag families". That is IMPOSSIBLE (proven by data + live Revit):
//   • the Revit API cannot author label rows (Family Editor UI only);
//   • cross-category label paste is blocked ("Can't paste Labels across Families
//     of different Categories");
//   • all 62 MEP family sheets have unique row structures, so no single master
//     + ReplaceParameter swap works and a superset master is unbuildable.
//
// The settled design: a human builds ONE UNIVERSAL, discipline-agnostic label
// (see UNIVERSAL_TAG_LABEL_BUILD_SHEET.md — 62 identical rows, tiers T1/T2/T4-T10)
// once, by hand. This command then CLONES that master to every target STING tag
// family via:
//     doc.EditFamily(master) → famDoc
//     famDoc.OwnerFamily.FamilyCategory = <target tag category>   (net-new API call)
//     TagTypeVariantWriter.CreateStandardVariants(...)            (re-create the
//                                                                   data-driven
//                                                                   depth/style
//                                                                   type variants)
//     SaveAs(<tempdir>\<target>.rfa) → LoadFamily(overwrite) → File.Replace(→ canonical)
//
// NAMING (hard Revit rule): a loaded family's project name IS its .rfa FILE
// name — renaming famDoc.OwnerFamily does NOT survive SaveAs. The clone must
// therefore be saved under the target's EXACT file name, inside a throwaway
// temp SUBFOLDER for atomicity. Saving under a temp-suffixed file name (the
// pre-fix behaviour) made LoadFamily mint a junk duplicate named
// "<target>.rfa.sting-propagate-<guid>" and left the real target untouched.
// Execute() purges any such leftovers from earlier runs before propagating.
//
// MULTI-CATEGORY IS OUT OF REACH (measured 2026-09-21, 'STING - LPS SPD Tag').
// Setting a clone of the single-category master to OST_MultiCategoryTags throws
// "the input category id cannot be assigned as the new category for this
// family". Revit can CREATE a family in Multi-Category but will not MOVE one
// into it. So a Multi-Category tag can never receive the universal label from
// this master, and the nine LPS families — deliberately re-born Multi-Category
// so they could tag several categories at once — are precisely the ones this
// conveyor cannot serve. Those two decisions are incompatible; reconciling them
// needs a SECOND, hand-built Multi-Category master, or the families back in a
// single tag category. Targets in that category are now named in the
// confirmation dialog and skipped without the 80-second round trip.
//
// Recategorising a family PRESERVES its label rows (proven live: Air Terminal →
// Duct Tags, every row survived). The label is IDENTICAL for all families, so no
// per-family row swapping is needed. Discipline-specific engineering data lives in
// per-category schedules instead (Schedule_DisciplineTagExpander, Task 3).
//
// Re-sync mode: re-running this command re-pushes master edits to all targets —
// that is the maintainability win, and it is the SAME code path (overwrite is
// idempotent), so re-sync needs no separate mode flag.
//
// SMOKE-TEST FIRST: the scope picker lets you target ONE family (e.g. Duct). Run
// it on Duct, eyeball the result in Revit (rows present, tier toggle works, type
// variants correct, nested badges survive) BEFORE scaling to all 206.
//
// Atomic save-then-publish is reused verbatim from
// MigrateTagLabelReferencesCommand: SaveAs to a temp .rfa, LoadFamily the temp,
// only then File.Replace it over the canonical .rfa. Any failure leaves the target's
// existing family untouched.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;
using StingTools.Tags;
using StingTools.UI;

namespace StingTools.Commands.TagStudio
{
    /// <summary>
    /// What to do about a target whose own category disagrees with the one declared
    /// for it in STING_TAG_CONFIG_v5_0_*.csv.
    ///
    /// This is not a preference - it decides whether the load can work at all.
    /// Revit matches a reloaded family by NAME, and will not change a loaded
    /// family's category on reload. Every propagation run recorded on this machine
    /// (19:58, 21:02, 21:23 on 2026-09-17) recategorised the clone and then had its
    /// load refused with no failure message, which is exactly that rule's signature.
    /// </summary>
    internal enum RecategoriseMode
    {
        /// <summary>Set the clone to the DECLARED category. Correct, and refused by
        /// Revit when a family of that name is already loaded under another one.</summary>
        EnforceDeclared,

        /// <summary>Keep whatever category the target already carries, so the load is
        /// a same-name-same-category overwrite - the path the conveyor was built for.
        /// The label propagates; the category stays wrong until the family is
        /// reloaded from disk or deleted and re-loaded.</summary>
        KeepExisting
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PropagateUniversalTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Logged FIRST, before any work. On 2026-09-22 this command froze
            // Revit and wrote NOTHING - no entry, no phase, no error - so there
            // was no way to tell a hang from a button that never fired, and the
            // only honest answer was "I cannot tell from the log". Every phase
            // below is announced for the same reason: the next freeze names
            // itself instead of needing a guess.
            StingLog.Info("PropagateUniversalTag: ENTER");

            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var app = ctx.App.Application;

            string sharedParamFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(sharedParamFile) || !File.Exists(sharedParamFile))
            {
                TaskDialog.Show("Propagate Universal Tag",
                    "MR_PARAMETERS.txt not found in data directory. Run 'Check Data' first.");
                return Result.Failed;
            }

            StingLog.Info("PropagateUniversalTag: collecting STING annotation families…");

            // ── Collect loaded STING-prefixed annotation (tag) families ──
            var stingFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.Name != null &&
                            f.Name.StartsWith(TagFamilyConfig.FamilyPrefix, StringComparison.OrdinalIgnoreCase) &&
                            f.FamilyCategory != null &&
                            f.FamilyCategory.CategoryType == CategoryType.Annotation)
                .OrderBy(f => f.Name)
                .ToList();

            // ── Identify duplicates minted by pre-fix runs ──
            // Before the temp-subfolder fix, the clone loaded under its temp FILE
            // name ("<target>.rfa.sting-propagate-<guid>"), creating a junk
            // duplicate and leaving the real target untouched. Partition them out
            // of the master/target pickers now, but DEFER the destructive delete
            // until AFTER the confirmation gate — deleting here means a user who
            // cancels still loses the families (and their placed tag instances).
            // Partition reads f.Name while every reference is still valid: a
            // deleted Element throws InvalidObjectException on any property access.
            var junk = new List<Family>();
            var keep = new List<Family>();
            foreach (Family f in stingFamilies)
            {
                if (IsTempNamed(f.Name)) junk.Add(f); else keep.Add(f);
            }
            stingFamilies = keep;
            int junkDeleted = 0;

            if (stingFamilies.Count < 2)
            {
                TaskDialog.Show("Propagate Universal Tag",
                    "Need the universal master plus at least one target family loaded.\n" +
                    "Load the universal master (built from UNIVERSAL_TAG_LABEL_BUILD_SHEET.md)\n" +
                    "and the STING tag families you want to propagate to, then re-run.");
                StingLog.Info($"PropagateUniversalTag: only {stingFamilies.Count} STING annotation " +
                              "family(ies) loaded, need 2 - nothing done");
                return Result.Cancelled;
            }

            // ── 1. Pick the universal master from the loaded families ──
            StingLog.Info($"PropagateUniversalTag: {stingFamilies.Count} candidate family(ies); " +
                          $"{junk.Count} temp-named leftover(s); showing master picker…");
            Family master = PickMaster(stingFamilies);
            if (master == null)
            {
                // Every early exit says which one it was. Three of them used to
                // return Cancelled with no dialog and no log line, so the whole
                // run read as "RunCommand: start / RunCommand: done" seconds
                // apart with nothing between - indistinguishable from the command
                // dying. Observed three times on 2026-09-17 at 20:43-20:44.
                StingLog.Info("PropagateUniversalTag: no master chosen (picker cancelled) - nothing done");
                return Result.Cancelled;
            }

            // ── 2. Targets = every other loaded STING tag family, scoped ──
            var candidates = stingFamilies.Where(f => f.Id != master.Id).ToList();
            var targets = ChooseTargets(candidates, out string scopeLabel);
            if (targets == null)
            {
                StingLog.Info("PropagateUniversalTag: scope dialog cancelled - nothing done");
                return Result.Cancelled;
            }
            if (targets.Count == 0)
            {
                TaskDialog.Show("Propagate Universal Tag", "No target families selected.");
                StingLog.Info("PropagateUniversalTag: scope resolved to zero targets - nothing done");
                return Result.Cancelled;
            }

            // ── 3. Confirmation ──
            StingLog.Info($"PropagateUniversalTag: master='{master.Name}', {targets.Count} target(s) " +
                          $"({scopeLabel}); building confirmation…");

            var variants = TagStyleCatalogue.EnumerateStandardVariants().ToList();
            // Say up front how many targets this cannot serve. A Multi-Category
            // family cannot receive the label (Revit will not move a family INTO
            // Multi-Category), and finding that out one 80-second failure at a
            // time, after committing to the run, is the wrong order to learn it in.
            bool masterIsMulti = master.FamilyCategory != null &&
                master.FamilyCategory.Id.Value == (long)BuiltInCategory.OST_MultiCategoryTags;

            // Declared out - the library's own decision, and the same source the
            // per-family skip consults, so the dialog cannot promise one thing
            // and the run do another.
            // IsNonUniversal, NOT Resolve. Resolve does a full category
            // resolution, and FindTagCategory inside it enumerates every
            // category in the document - once per call. Asking it 206 times to
            // fill in this dialog froze Revit on the UI thread before the dialog
            // could appear, and the command never logged a line because it never
            // got that far (2026-09-22). The flag is a hash lookup; the
            // resolution is not needed to read it.
            var declaredOut = targets
                .Where(t => TagCategoryResolver.IsNonUniversal(t.Name))
                .ToList();

            // Blocked by Revit rather than by choice. Listed separately because
            // they are different problems: one is intended, the other is a limit
            // that a Multi-Category master would lift.
            var declaredOutNames = new HashSet<string>(
                declaredOut.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            var unreachable = masterIsMulti
                ? new List<Family>()
                : targets.Where(t => !declaredOutNames.Contains(t.Name) &&
                        t.FamilyCategory != null &&
                        t.FamilyCategory.Id.Value == (long)BuiltInCategory.OST_MultiCategoryTags)
                    .ToList();

            var confirm = new TaskDialog("Propagate Universal Tag");
            confirm.MainInstruction =
                $"Propagate '{master.Name}' to {targets.Count} target families ({scopeLabel})?";
            confirm.MainContent =
                "For each target family this will:\n" +
                "  • Clone the universal master (EditFamily)\n" +
                "  • Recategorise the clone to the target's tag category\n" +
                "  • Save the clone under the target's exact .rfa file name (so LoadFamily overwrites it)\n" +
                $"  • Add any missing style/visibility params + re-create up to {variants.Count} type variants\n" +
                "  • Overwrite the target family (atomic SaveAs → LoadFamily → move)\n\n" +
                "The universal label rows carry over unchanged (recategorise preserves\n" +
                "label rows). Re-running is a safe RE-SYNC — master edits re-propagate.\n\n" +
                (declaredOut.Count > 0
                    ? $"{declaredOut.Count} of these declare \"Universal: No\" and will be SKIPPED:\n  " +
                      string.Join("\n  ", declaredOut.Take(5).Select(f => f.Name)) +
                      (declaredOut.Count > 5 ? "\n  ..." : "") +
                      "\nThey keep their own bespoke label, which this master does not carry.\n\n"
                    : "") +
                (unreachable.Count > 0
                    ? $"{unreachable.Count} of these are Multi-Category Tags and will be SKIPPED:\n  " +
                      string.Join("\n  ", unreachable.Take(5).Select(f => f.Name)) +
                      (unreachable.Count > 5 ? "\n  ..." : "") +
                      "\nRevit can create a family in Multi-Category but will not move one into it, " +
                      "so the universal label cannot reach them.\n\n"
                    : "") +
                "SMOKE TEST: verify one family (Duct) in Revit before scaling to all.\n" +
                "Press Escape between families to cancel.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() != TaskDialogResult.Ok)
            {
                StingLog.Info($"PropagateUniversalTag: confirmation declined for {targets.Count} target(s) - nothing done");
                return Result.Cancelled;
            }

            // ── Purge pre-fix temp duplicates NOW the user has committed ──
            // Deferred from before the pickers so cancelling the command doesn't
            // delete families (and their placed tag instances) the user kept.
            if (junk.Count > 0)
            {
                using (var junkTx = new Transaction(doc, "STING Purge propagate temp duplicates"))
                {
                    junkTx.Start();
                    foreach (Family f in junk)
                    {
                        string junkName = f.Name; // read before Delete — invalid after
                        try { doc.Delete(f.Id); junkDeleted++; }
                        catch (Exception ex) { StingLog.Warn($"Purge temp duplicate '{junkName}': {ex.Message}"); }
                    }
                    junkTx.Commit();
                }
                StingLog.Info($"PropagateUniversalTag: purged {junkDeleted} temp-named duplicate families");
            }

            // ── Pre-resolve shared arrowhead types ──
            var arrowheads = TagTypeVariantWriter.BuildArrowheadLookup(doc);
            List<string> styleAndVisParams = TagFamilyConfig.StyleParams
                .Concat(TagFamilyConfig.VisibilityParams)
                .Distinct()
                .ToList();

            // ── Pre-flight: will the clone even load back? ──
            // Every target gets a clone of the same master, so a shared-parameter
            // type conflict fails all of them identically — and LoadFamily reports
            // a bare false, which the loop could only write down as "LoadFamily
            // back into project failed". That is what a 17-minute run reported on
            // 2026-09-17 while the real cause (12 parameters the family offers as
            // Text that the project holds as Number/Currency/Length/Yes-No under
            // the same GUIDs) was readable in seconds. Read it here, name the
            // parameters, and stop before touching 206 families.
            {
                string preflightSp = app.SharedParametersFilename;
                MasterPreflight pre;
                try
                {
                    // The same file the loop binds from, so the definitions checked
                    // are the ones propagation would actually add.
                    app.SharedParametersFilename = sharedParamFile;
                    var willAdd = SharedParamPreflight.CollectDefinitions(
                        app.OpenSharedParameterFile(), styleAndVisParams);
                    pre = SharedParamPreflight.CheckMaster(doc, master, willAdd);
                }
                finally
                {
                    try { app.SharedParametersFilename = preflightSp ?? ""; }
                    catch (Exception ex) { StingLog.Warn($"Restore SharedParametersFilename after pre-flight: {ex.Message}"); }
                }

                if (pre.Conflicts.Count > 0)
                {
                    string detail = SharedParamConflictDetector.Describe(pre.Conflicts);
                    StingLog.Warn($"PropagateUniversalTag: aborted before any family — {detail?.Replace("\n", " ")}");

                    var block = new TaskDialog("Propagate Universal Tag");
                    block.MainInstruction =
                        $"'{master.Name}' cannot load into this project — nothing was propagated.";
                    block.MainContent =
                        detail + "\n\n" +
                        "Revit identifies a shared parameter by its GUID and refuses a load that " +
                        "would redefine one, so every target would fail the same way.\n\n" +
                        "Fix it in the FAMILY, not the project: delete the conflicting parameters " +
                        "from the master (a numeric parameter cannot be retyped to Text for a label — " +
                        "use its _TXT display mirror), then re-run.\n\n" +
                        "docs/UNIVERSAL_TAG_CONFLICT_RESOLUTION_RUNBOOK.md has the ordered steps.";
                    block.CommonButtons = TaskDialogCommonButtons.Close;
                    block.Show();
                    return Result.Cancelled;
                }

                // ── The master decides which TIER GATES exist ──
                // TagFamilyConfig.VisibilityParams is the full ten-tier ladder plus
                // the warning, and AddMissingParams used to add all eleven to every
                // clone regardless of the master. That is how the propagated duct tag
                // came to carry TAG_PARA_STATE_3_BOOL when the master has no _3 and
                // the label has no T3 rows: a gate with nothing behind it, on 206
                // families, making a _T3 type variant indistinguishable from _T2.
                //
                // The style matrix is NOT filtered this way. The master carries style
                // as type variants rather than as the 128 BOOLs, so the clones have to
                // be given the matrix for their variants to switch anything - that
                // asymmetry is the design, and it is why "align with the master" means
                // the gates, not the whole set.
                if (pre.MasterRead && pre.MasterParamNames.Count > 0)
                {
                    var allGates = new HashSet<string>(TagFamilyConfig.VisibilityParams,
                                                       StringComparer.OrdinalIgnoreCase);
                    var droppedGates = styleAndVisParams
                        .Where(n => allGates.Contains(n) && !pre.MasterParamNames.Contains(n))
                        .ToList();
                    if (droppedGates.Count > 0)
                    {
                        styleAndVisParams = styleAndVisParams
                            .Where(n => !droppedGates.Contains(n, StringComparer.OrdinalIgnoreCase))
                            .ToList();
                        StingLog.Info("PropagateUniversalTag: not adding " +
                                      string.Join(", ", droppedGates) +
                                      " - the master does not carry " +
                                      (droppedGates.Count == 1 ? "it" : "them") +
                                      ", so the clones will not either");
                    }
                }

                // Not a blocker, but it multiplies by the number of targets: text
                // the author left for themselves is cloned into every family and
                // then prints. Spotted on 2026-09-17 as a red note in a tag family.
                if (pre.AuthoringNotes.Count > 0)
                {
                    var noteDlg = new TaskDialog("Propagate Universal Tag");
                    noteDlg.MainInstruction = pre.AuthoringNotes.Count == 1
                        ? $"'{master.Name}' carries a note that looks like an instruction to its author."
                        : $"'{master.Name}' carries {pre.AuthoringNotes.Count} notes that look like " +
                          "instructions to its author.";
                    noteDlg.MainContent =
                        "  • " + string.Join("\n  • ", pre.AuthoringNotes) + "\n\n" +
                        $"Everything in the master is cloned into each of the {targets.Count} target " +
                        "famil" + (targets.Count == 1 ? "y" : "ies") + ", so this text goes with it and " +
                        "will print on drawings." + "\n\n" +
                        "Delete it in the master and re-run, or propagate anyway.";
                    noteDlg.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                    noteDlg.DefaultButton = TaskDialogResult.No;
                    if (noteDlg.Show() != TaskDialogResult.Yes)
                    {
                        StingLog.Info($"PropagateUniversalTag: declined over {pre.AuthoringNotes.Count} " +
                                      "authoring note(s) in the master - nothing done");
                        return Result.Cancelled;
                    }
                    StingLog.Warn("PropagateUniversalTag: proceeding with authoring note(s) in the master " +
                                  "(operator confirmed)");
                }
            }

            // ── Which targets would have their category CHANGED? ──
            // Asked before the run because it decides whether the load can succeed,
            // and because it is the one variable that separates "the conveyor does
            // not work" from "the conveyor cannot change a category".
            var mode = RecategoriseMode.EnforceDeclared;
            {
                var changing = new List<string>();
                foreach (Family t in targets)
                {
                    var r = TagCategoryResolver.Resolve(doc, t);
                    if (r != null && r.IsMismatch && r.DeclaredTagCategory != null)
                        changing.Add($"{t.Name}: {r.ActualCategory} → {r.DeclaredTagCategory.Name}");
                }

                if (changing.Count > 0)
                {
                    var catDlg = new TaskDialog("Propagate Universal Tag — category");
                    catDlg.MainInstruction = changing.Count == 1
                        ? "One target family is categorised differently from its declaration."
                        : $"{changing.Count} target families are categorised differently from their declaration.";
                    catDlg.MainContent =
                        "  • " + string.Join("\n  • ", changing.Take(8)) +
                        (changing.Count > 8 ? $"\n  • … and {changing.Count - 8} more" : "") + "\n\n" +
                        "Revit will not change a loaded family's category by reloading over it. " +
                        "Enforcing the declaration is correct, but the load can be refused - with no " +
                        "message - which is what every run so far has hit.";
                    catDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                    catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "KEEP each family's current category (recommended for the smoke test)",
                        "Same-name, same-category overwrite - the path this command was built for. " +
                        "The label propagates and the category stays as it is.");
                    catDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "ENFORCE the declared category",
                        "Correct, and the load may be refused. If it is, the report says so per family.");

                    var catChoice = catDlg.Show();
                    if (catChoice == TaskDialogResult.Cancel)
                    {
                        StingLog.Info("PropagateUniversalTag: category dialog cancelled - nothing done");
                        return Result.Cancelled;
                    }
                    mode = catChoice == TaskDialogResult.CommandLink1
                        ? RecategoriseMode.KeepExisting
                        : RecategoriseMode.EnforceDeclared;
                    StingLog.Info($"PropagateUniversalTag: category mode = {mode} " +
                                  $"({changing.Count} target(s) declared differently)");
                }
            }

            // ── Pre-flight: is any target open in the Family Editor? ──
            // Revit will not load a family while a document for it is open, and it
            // refuses by returning false with NO failure message - the 21:02 run
            // spent 90 seconds cloning, adding 138 parameters and minting 14 type
            // variants before hitting that wall. Cheap to ask first.
            {
                var openTargets = targets
                    .Select(t => new { Name = t.Name, Doc = FindOpenFamilyDocument(app, t.Name, null) })
                    .Where(x => x.Doc != null)
                    .ToList();
                if (openTargets.Count > 0)
                {
                    string names = string.Join("\n  • ", openTargets.Select(x => x.Name));
                    StingLog.Warn($"PropagateUniversalTag: aborted - {openTargets.Count} target(s) open in the " +
                                  $"Family Editor: {string.Join(", ", openTargets.Select(x => x.Name))}");

                    var openDlg = new TaskDialog("Propagate Universal Tag");
                    openDlg.MainInstruction = openTargets.Count == 1
                        ? "A target family is open in the Family Editor - nothing was propagated."
                        : $"{openTargets.Count} target families are open in the Family Editor - nothing was propagated.";
                    openDlg.MainContent =
                        "  • " + names + "\n\n" +
                        "Revit will not load a family while a document for it is open, and it says nothing " +
                        "when it refuses - the load just fails.\n\n" +
                        "Close those family tabs (Load into Project and Close, or close without saving) and re-run.";
                    openDlg.CommonButtons = TaskDialogCommonButtons.Close;
                    openDlg.Show();
                    return Result.Cancelled;
                }
            }

            var progress = StingProgressDialog.Show("Propagate Universal Tag", targets.Count);
            var rows = new List<List<string>>();
            int succeeded = 0, failed = 0, cancelled = 0, skipped = 0, totalTypes = 0, totalParams = 0, totalScope = 0;
            // Families whose project update landed but whose library .rfa did not.
            var diskWriteFailures = new List<string>();
            long totalMs = 0;
            string originalSp = app.SharedParametersFilename;

            try
            {
                app.SharedParametersFilename = sharedParamFile;

                // Clear any Escape left latched by an earlier dialog. The check
                // below runs at i == 0, before any work, so a stale latch
                // cancels the entire run before it starts - which is exactly
                // what happened at 00:06 on 2026-09-22: "cancelled BY THE USER
                // (Escape) after 0 of 206", nothing touched, seconds after the
                // previous run's report was dismissed.
                EscapeChecker.DrainPendingEscape();

                // Give the master the parameters and type variants every clone
                // would otherwise add for itself. Measured 2026-09-22: a clone
                // spent 56.6s adding 138 parameters and 7.3s minting 12 type
                // variants, out of 65.3s total. The master carried none of them,
                // so all 206 clones paid for the same work.
                //
                // Fail-soft on purpose. If this cannot run, every clone still
                // adds its own parameters exactly as before - slower, correct.
                // It must never be the reason a propagation run does not start.
                // Reassigned, because reloading the master can replace the
                // Family element and the loop below calls EditFamily(master)
                // 206 times. A stale reference throws InvalidObjectException
                // on the first one - the whole run lost to a speed fix.
                master = PrimeMaster(doc, app, master, sharedParamFile,
                                     styleAndVisParams, variants, arrowheads) ?? master;

                // Prove the master is still usable before spending the run on
                // it. A dead reference fails EVERY family identically, and the
                // first run of PrimeMaster did exactly that: one stale property
                // read, and the command died on 'STING - Birth Pool Tag' with a
                // Revit message that named neither the master nor the cause.
                // One cheap probe turns that into a sentence.
                try { var _probe = master.Name; }
                catch (Exception mex)
                {
                    StingLog.Error("PropagateUniversalTag: master reference invalid after priming", mex);
                    TaskDialog.Show("Propagate Universal Tag",
                        "The master family reference went stale while preparing it, so nothing was " +
                        "propagated.\n\nNothing is damaged — the master was backed up before any change, " +
                        "and priming is idempotent. Re-run the command: the master is found fresh each " +
                        "time, and the second run has nothing left to prime.");
                    return Result.Cancelled;
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    if ((i % 5) == 0 && EscapeChecker.IsEscapePressed())
                    {
                        cancelled = targets.Count - i;
                        StingLog.Info($"PropagateUniversalTag: cancelled BY THE USER (Escape) after {i} of {targets.Count}");
                        break;
                    }

                    Family target = targets[i];
                    string targetName = target.Name;
                    string catName = target.FamilyCategory?.Name ?? "";
                    progress.Increment($"Propagating → {targetName} ({i + 1}/{targets.Count})");

                    var r = PropagateOne(doc, app, master, target, sharedParamFile,
                        styleAndVisParams, variants, arrowheads, mode);
                    totalTypes += r.TypesCreated;
                    totalParams += r.ParamsAdded;
                    totalScope += r.ScopeFixed;
                    if (r.Skipped) skipped++;
                    else if (r.Success) succeeded++;
                    else failed++;
                    if (r.DiskWriteFailed) diskWriteFailures.Add(targetName + ": " + r.DiskWriteDetail);

                    // One line per family, so a long run can be read afterwards
                    // instead of guessed at. "other" is whatever the three named
                    // phases did not account for - if it dominates, the phases
                    // are drawn in the wrong places and this line says so rather
                    // than quietly summing to the total.
                    totalMs += r.MsTotal;
                    StingLog.Info($"PropagateUniversalTag timing: '{targetName}' " +
                        $"total={r.MsTotal}ms (edit={r.MsEdit} params={r.MsParams} variants={r.MsVariants} " +
                        $"saveload={r.MsSaveLoad} other={Math.Max(0, r.MsTotal - r.MsEdit - r.MsParams - r.MsVariants - r.MsSaveLoad)}) " +
                        $"types={r.TypesCreated} params={r.ParamsAdded}");

                    rows.Add(new List<string>
                    {
                        targetName, catName,
                        r.ParamsAdded.ToString(), r.TypesCreated.ToString(),
                        // The library, not the project, is what every later lookup
                        // reads - so a family that updated in the project but not
                        // on disk is reported as its own outcome, never as OK.
                        r.Skipped ? "SKIPPED (declared)"
                            : r.DiskWriteFailed ? "PROJECT ONLY" : r.Success ? "OK" : "FAILED",
                        r.DiskWriteFailed ? r.DiskWriteDetail : r.ErrorMessage ?? ""
                    });
                }
            }
            finally
            {
                progress.Close();
                try { if (!string.IsNullOrEmpty(originalSp)) app.SharedParametersFilename = originalSp; }
                catch (Exception ex) { StingLog.Warn($"Restore SharedParametersFilename: {ex.Message}"); }
            }

            // ── Excel summary ──
            string xlsx = null;
            try
            {
                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                xlsx = Path.Combine(outDir, $"STING_PropagateUniversalTag_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                StingExcelExporter.ExportTable(
                    xlsx, "Propagation",
                    new List<string> { "Target", "Category", "ParamsAdded", "TypesCreated", "Status", "Error" },
                    rows, openFolder: false);
            }
            catch (Exception ex) { StingLog.Warn($"Excel export: {ex.Message}"); }

            // The failures, named in the dialog. This used to report "0
            // propagated, 1 failed" and stop there: the reason was a cell in an
            // .xlsx written to a %TEMP% GUID folder, which is why a 17-minute run
            // that DID explain itself in writing still read as the command dying.
            var failedRows = rows
                .Where(r => r.Count > 4 && string.Equals(r[4], "FAILED", StringComparison.Ordinal))
                .ToList();
            var why = new StringBuilder();
            foreach (var r in failedRows.Take(5))
            {
                string err = r.Count > 5 && !string.IsNullOrWhiteSpace(r[5]) ? r[5] : "(no reason recorded)";
                why.Append($"\n  • {r[0]}: {err}");
            }
            if (failedRows.Count > 5)
                why.Append($"\n  • … and {failedRows.Count - 5} more in the report");

            var td = new TaskDialog("Propagate Universal Tag — done");
            // Skipped belongs in the headline. The run of 2026-09-22 reported
            // "197 propagated, 0 failed" against a 206-family library and said
            // nothing about the other nine - a shortfall the reader has to
            // either explain away or go and investigate. The log knew
            // (skipped=9); the dialog simply did not pass it on. Every family
            // is accounted for in one line, or the counts invite a wrong guess.
            td.MainInstruction = $"{succeeded} propagated, {failed} failed" +
                                 (skipped > 0 ? $", {skipped} skipped (declared)" : "") +
                                 (cancelled > 0 ? $", {cancelled} cancelled" : "");
            td.MainContent =
                $"Master: {master.Name}\n" +
                $"Scope:  {scopeLabel}\n\n" +
                // totalParams and totalTypes are RUNNING TOTALS across every clone
                // (see the += at the per-family site), so "added to each clone" was
                // wrong by however many clones ran: 3450 read as 3450 per family
                // when it was 138 each across 25. State the total and derive the
                // per-clone figure instead of implying it.
                $"Standard params added: {totalParams} total\n" +
                (succeeded > 0 ? $"  ({totalParams / succeeded} per clone)\n" : "") +
                // Named for what it is. "Params added: 139" reads as a side effect;
                // it is the standard style+visibility set that the master does not
                // carry, and it is why the two families differ afterwards.
                $"Type variants (re)created: {totalTypes} total\n" +
                (succeeded > 0 ? $"  ({totalTypes / succeeded} per clone)\n" : "") +
                (totalScope > 0
                    ? $"Tier gates converted Instance -> Type: {totalScope}\n"
                    : "") +
                (junkDeleted > 0 ? $"Purged stale temp-named duplicates: {junkDeleted}\n" : "") +
                (diskWriteFailures.Count > 0
                    ? $"\nWARNING - {diskWriteFailures.Count} family/families updated in the PROJECT " +
                      "but NOT in the library on disk:\n  " +
                      string.Join("\n  ", diskWriteFailures.Take(5)) +
                      (diskWriteFailures.Count > 5 ? "\n  ..." : "") +
                      "\nThe library is what every later lookup and every deploy reads, so these " +
                      "are NOT done. Re-run for them once whatever held the file is gone.\n"
                    : "") +
                (why.Length > 0 ? $"\nFailed:{why}\n" : "") +
                (xlsx != null
                    ? $"\nReport: {xlsx}" +
                      // Where it went, and why it went there. An unsaved project has
                      // no _BIM_COORD to write to, so the report lands in a
                      // per-session %TEMP% GUID folder nobody finds by accident.
                      (xlsx.IndexOf(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) >= 0
                          ? "\n(Project is unsaved, so the report went to TEMP — save the project to keep reports.)"
                          : "")
                    : "\nNo report was written — see the log.");
            td.Show();

            StingLog.Info($"PropagateUniversalTag: master={master.Name}, succeeded={succeeded}, " +
                $"failed={failed}, skipped={skipped}, cancelled={cancelled}, params={totalParams}, types={totalTypes}, " +
                $"elapsed={totalMs / 1000}s" +
                (succeeded > 0 ? $" ({totalMs / succeeded / 1000}s per family)" : ""));
            return Result.Succeeded;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Single-target propagation
        // ──────────────────────────────────────────────────────────────────

        private class PropResult
        {
            public int ParamsAdded;
            public int TypesCreated;
            public bool Success;
            public string ErrorMessage;
            // The project was updated but the library .rfa on disk was not. Every
            // later lookup and every deploy reads the library, so this is a real
            // failure even though nothing in the project went wrong.
            public bool DiskWriteFailed;
            public string DiskWriteDetail;
            // Declared out of scope, not attempted, not a failure. Counting a
            // deliberate exclusion as a failure would put a permanent red number
            // on every run and train everyone to ignore it.
            public bool Skipped;
            // Per-phase milliseconds. At ~82s per family a full library run is
            // about five hours, and nothing in the log said which phase owned
            // that time. Three numbers answer it: opening the master, minting
            // the type variants, and the save+load round trip.
            // MsParams was folded into "other" until 2026-09-21, when other
            // turned out to be 85% of the run. Naming it is the point: an
            // unnamed remainder that dominates is the signal to go looking.
            public long MsEdit, MsParams, MsVariants, MsSaveLoad, MsTotal;
            // The family's own category disagreed with the declared one. A silent
            // correction would hide that the family was authored against the wrong
            // template, so it is carried to the results table as a FINDING.
            public bool CategoryMismatch;
            // Whether THIS run tried to move the family to another category -
            // the one thing Revit refuses to do on reload.
            public bool CategoryChanged;
            // Visibility gates converted from Instance to Type by this run.
            public int ScopeFixed;
            public string CategoryNote;
        }

        private PropResult PropagateOne(Document doc,
            Autodesk.Revit.ApplicationServices.Application app,
            Family master, Family target, string sharedParamFile,
            List<string> styleAndVisParams, List<TypeVariantSpec> variants,
            Dictionary<string, ElementId> arrowheads,
            RecategoriseMode mode = RecategoriseMode.EnforceDeclared)
        {
            var result = new PropResult();
            string targetName = target.Name;

            // Reading the category off the TARGET propagates whatever the target
            // already is, so a mis-categorised family stays mis-categorised through
            // every propagation forever. Confirmed live: "STING - Air Terminal Tag"
            // is a Generic Model Tag, so Revit never offers it for Air Terminals.
            //
            // Resolve against the DECLARED category in STING_TAG_CONFIG_v5_0_*.csv
            // instead, and fall back to the family's own only when nothing is declared.
            var catRes = TagCategoryResolver.Resolve(doc, target);
            ElementId existingCatId = target.FamilyCategory?.Id;
            string existingCatName = target.FamilyCategory?.Name ?? "";

            // KeepExisting makes the load a same-name-same-category overwrite.
            // EnforceDeclared is the correct end state and the one Revit can
            // refuse, so which one ran has to be visible in the result.
            ElementId targetCatId = mode == RecategoriseMode.KeepExisting
                ? existingCatId
                : (catRes.DeclaredTagCategory?.Id ?? existingCatId);
            result.CategoryMismatch = catRes.IsMismatch;
            result.CategoryNote = catRes.Note;
            result.CategoryChanged = targetCatId != null && existingCatId != null &&
                                    targetCatId != existingCatId;
            if (catRes.IsMismatch)
                StingLog.Warn($"PropagateUniversalTag: '{targetName}' — {catRes.Note}" +
                              (mode == RecategoriseMode.KeepExisting
                                  ? " (KeepExisting: left as it is for this run)"
                                  : ""));
            // A family that declares "Universal: No" keeps its own label.
            //
            // This is checked FIRST and on the DECLARATION, not on the category
            // below, because the two protect against different things. The
            // category check describes what Revit currently refuses; the
            // declaration describes what the library intends. Until now only the
            // first existed, which meant the nine LPS tags were protected purely
            // by a Revit error - and the universal master carries 71 generic
            // ASS_* rows and ZERO ELC_LPS_* rows, so had that error ever stopped
            // firing, propagation would have deleted every LPS row without a
            // word. Build a Multi-Category master and it stops firing.
            if (!catRes.Universal)
            {
                result.Skipped = true;
                result.ErrorMessage =
                    "declared \"Universal: No\" in STING_TAG_CONFIG_v5_0_*.csv - it keeps its own " +
                    "bespoke label, which the universal master does not carry.";
                StingLog.Info($"PropagateUniversalTag: '{targetName}' skipped — {result.ErrorMessage}");
                return result;
            }

            // A Multi-Category target cannot receive the universal label, and no
            // amount of retrying changes that.
            //
            // Measured 2026-09-21 on 'STING - LPS SPD Tag': setting a clone of the
            // single-category master to OST_MultiCategoryTags throws "the input
            // category id cannot be assigned as the new category for this family".
            // That is the SAME refusal already documented for reload, in a second
            // place: Revit will not move a family INTO Multi-Category, only create
            // it there. So the nine LPS families, which were deliberately re-born
            // as Multi-Category so they could tag several categories at once, are
            // exactly the families this conveyor cannot serve. The two decisions
            // are incompatible, and a Multi-Category master would be needed to
            // reconcile them.
            //
            // Caught HERE rather than at the API call, because the call costs a
            // full EditFamily + document open first - 80-odd seconds each, nine
            // times - to arrive at an error that was knowable for free. And it
            // reads as a known limit rather than a raw API message.
            if (targetCatId != null &&
                targetCatId.Value == (long)BuiltInCategory.OST_MultiCategoryTags &&
                master.FamilyCategory != null &&
                master.FamilyCategory.Id.Value != (long)BuiltInCategory.OST_MultiCategoryTags)
            {
                result.ErrorMessage =
                    "target is a Multi-Category Tag; Revit will not move the master's clone into " +
                    "Multi-Category (it can only be created there), so the universal label cannot " +
                    "be propagated to it. Either build a Multi-Category master, or re-create this " +
                    "family in a single tag category.";
                StingLog.Warn($"PropagateUniversalTag: '{targetName}' skipped — {result.ErrorMessage}");
                return result;
            }

            Document famDoc = null;
            string tempDir = null; // hoisted so the catch below can clean a half-made temp dir

            using (var tg = new TransactionGroup(doc, $"STING Propagate → {targetName}"))
            {
                try
                {
                    tg.Start();

                    // Fresh clone of the master each iteration — recategorise mutates
                    // the family document, so we must not reuse it across targets.
                    var swTotal = System.Diagnostics.Stopwatch.StartNew();
                    var swPhase = System.Diagnostics.Stopwatch.StartNew();
                    famDoc = doc.EditFamily(master);
                    result.MsEdit = swPhase.ElapsedMilliseconds;
                    if (famDoc == null)
                    {
                        result.ErrorMessage = "EditFamily(master) returned null";
                        tg.RollBack();
                        return result;
                    }

                    FamilyManager fm = famDoc.FamilyManager;
                    var defFile = app.OpenSharedParameterFile();
                    if (defFile == null)
                    {
                        result.ErrorMessage = "OpenSharedParameterFile returned null";
                        famDoc.Close(false); famDoc = null;
                        tg.RollBack();
                        return result;
                    }

                    // Resolve the target's tag category inside the family document.
                    // Built-in category ids are document-independent, so GetCategory
                    // by the target family's category id returns the same category
                    // in famDoc.
                    Category targetCat = (targetCatId != null)
                        ? Category.GetCategory(famDoc, targetCatId)
                        : null;
                    if (targetCat == null)
                    {
                        result.ErrorMessage = $"Could not resolve target tag category ({target.FamilyCategory?.Name})";
                        famDoc.Close(false); famDoc = null;
                        tg.RollBack();
                        return result;
                    }

                    using (var tx = new Transaction(famDoc, "STING Recategorise + author variants"))
                    {
                        tx.Start();

                        // (a) Recategorise — the single net-new Revit API call. Skip if
                        // the master is already in the target category (recategorising
                        // to the same category is a harmless no-op but avoid it anyway).
                        try
                        {
                            if (famDoc.OwnerFamily.FamilyCategory == null ||
                                famDoc.OwnerFamily.FamilyCategory.Id != targetCat.Id)
                            {
                                famDoc.OwnerFamily.FamilyCategory = targetCat;
                            }
                        }
                        catch (Exception catEx)
                        {
                            result.ErrorMessage = $"Set FamilyCategory failed: {catEx.Message}";
                            tx.RollBack();
                            famDoc.Close(false); famDoc = null;
                            tg.RollBack();
                            return result;
                        }

                        // (b) No rename here: a loaded family's project name comes
                        // from its .rfa FILE name, and renaming famDoc.OwnerFamily
                        // does not survive SaveAs. The save below writes the clone
                        // under the target's exact file name instead.

                        // (c) Ensure style/visibility params exist, then (re)create the
                        // data-driven depth/style type variants.
                        swPhase.Restart();
                        result.ParamsAdded = AddMissingParams(fm, defFile, styleAndVisParams);
                        result.MsParams = swPhase.ElapsedMilliseconds;
                        result.ScopeFixed = MakeVisibilityParamsType(fm);
                        swPhase.Restart();
                        result.TypesCreated = TagTypeVariantWriter.CreateStandardVariants(fm, variants, arrowheads);
                        result.MsVariants = swPhase.ElapsedMilliseconds;

                        tx.Commit();
                    }

                    // ── Atomic save-then-publish (from MigrateTagLabelReferences) ──
                    // The temp copy keeps the target's EXACT file name (family name
                    // == file name in Revit) inside a throwaway subfolder, so the
                    // LoadFamily below overwrites the target family in the project
                    // instead of minting a duplicate named after a temp file.
                    string outDir = TagFamilyConfig.GetOutputDirectory();
                    Directory.CreateDirectory(outDir);
                    string rfaFileName = targetName.Replace('/', '-') + ".rfa";

                    // A slash (or any other char stripped by sanitisation) in the
                    // family name makes the .rfa FILE name diverge from the
                    // family's project name. LoadFamily keys on the file name, so
                    // saving under the sanitised name would mint a NEW mis-named
                    // family and leave the real target untouched — reintroducing
                    // the exact bug this fix closes, and the duplicate is NOT
                    // temp-named so the purge won't reap it. Fail explicitly.
                    if (!string.Equals(Path.GetFileNameWithoutExtension(rfaFileName), targetName, StringComparison.Ordinal))
                    {
                        result.ErrorMessage =
                            $"Family name '{targetName}' has no 1:1 file name (sanitised to '{rfaFileName}'); " +
                            "propagating would create a mis-named duplicate instead of overwriting the target. " +
                            "Rename the family without '/' and retry.";
                        StingLog.Warn($"PropagateUniversalTag: skipping '{targetName}' — sanitised file name diverges from family name");
                        famDoc.Close(false); famDoc = null;
                        tg.RollBack();
                        return result;
                    }

                    string finalPath = Path.Combine(outDir, rfaFileName);
                    tempDir = Path.Combine(outDir,
                        ".sting-propagate-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Directory.CreateDirectory(tempDir);
                    string tempPath = Path.Combine(tempDir, rfaFileName);

                    bool savedOk = false;
                    try
                    {
                        var saveOpts = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 };
                        swPhase.Restart();
                        famDoc.SaveAs(tempPath, saveOpts);
                        savedOk = true;
                    }
                    catch (Exception saveEx)
                    {
                        result.ErrorMessage = $"SaveAs failed: {saveEx.Message}";
                        StingLog.Warn($"{targetName}: SaveAs failed: {saveEx.Message}");
                    }
                    // Document.Close returns FALSE when Revit will not close the
                    // document, and that return was discarded. A clone that stays
                    // open is a family document named after the TARGET, and Revit
                    // refuses to load a family while a document for it is open -
                    // returning a bare false with no failure message, which is
                    // precisely what the 21:02 run reported.
                    bool closedOk;
                    try { closedOk = famDoc.Close(false); }
                    catch (Exception closeEx)
                    {
                        closedOk = false;
                        StingLog.Warn($"{targetName}: closing the clone threw: {closeEx.Message}");
                    }
                    famDoc = null;
                    if (!closedOk)
                        StingLog.Warn($"{targetName}: the clone document did not close - " +
                                      "it is still open under the target's name and will block the load");

                    if (!savedOk)
                    {
                        TryDeleteTempDir(tempDir);
                        try { tg.RollBack(); } catch (Exception rbEx) { StingLog.Warn($"{targetName}: tg.RollBack after save fail: {rbEx.Message}"); }
                        return result;
                    }

                    bool loadedOk = false;
                    string loadThrew = null;
                    // LoadFamily's false return carries no reason; Revit's own
                    // explanation arrives as failure messages on this transaction
                    // and, uncaptured, only ever reaches a modal dialog.
                    var loadFailures = new CapturingFailuresPreprocessor();
                    using (var loadTx = new Transaction(doc, $"STING Reload {targetName}"))
                    {
                        loadTx.Start();
                        var fho = loadTx.GetFailureHandlingOptions();
                        loadTx.SetFailureHandlingOptions(fho.SetFailuresPreprocessor(loadFailures));
                        try { loadedOk = doc.LoadFamily(tempPath, new TagFamilyLoadOptions(), out _); }
                        catch (Exception loadEx)
                        {
                            loadThrew = loadEx.Message;
                            StingLog.Warn($"{targetName}: LoadFamily: {loadEx.Message}");
                            loadedOk = false;
                        }
                        if (loadedOk) loadTx.Commit(); else loadTx.RollBack();
                    }

                    // Recorded whether the load succeeded or was refused - a refused
                    // load is the slow case worth knowing the cost of.
                    result.MsSaveLoad = swPhase.ElapsedMilliseconds;
                    result.MsTotal = swTotal.ElapsedMilliseconds;

                    if (!loadedOk)
                    {
                        TryDeleteTempDir(tempDir);
                        string why = loadFailures.Summary() ?? loadThrew;
                        if (why == null)
                        {
                            // A refused load with no failure message has one common
                            // cause: a document for this family is open in the
                            // session. Revit will not overwrite a family it is
                            // editing, and says nothing. Look, rather than guess.
                            Document openDoc = FindOpenFamilyDocument(app, targetName, null);
                            if (openDoc != null)
                                why = $"'{targetName}' is open in the Family Editor " +
                                      $"({openDoc.PathName ?? openDoc.Title}). Revit will not load a family " +
                                      "while a document for it is open. Close that tab (or Load into Project " +
                                      "and Close) and re-run.";
                            else if (!closedOk)
                                why = "the clone document could not be closed, so it was still open under " +
                                      "this family's name when the load was attempted.";
                            else if (result.CategoryChanged)
                            {
                                // The leading explanation once the open-document theory
                                // is ruled out: Revit matches a reloaded family by name
                                // and will not move it to another category. Count what a
                                // delete would cost so the operator can decide, rather
                                // than deleting on a hypothesis.
                                int placed = CountPlacedInstances(doc, target);
                                string newCat = Category.GetCategory(doc, targetCatId)?.Name ?? "the declared category";
                                why = $"this run recategorised the family from '{existingCatName}' to " +
                                      $"'{newCat}', and Revit will not change a loaded family's " +
                                      "category by reloading over it. Re-run and choose KEEP each family's " +
                                      "current category to propagate the label, or delete " +
                                      $"'{targetName}' from the project first (" +
                                      (placed < 0 ? "instance count unavailable" :
                                          placed + " placed instance" + (placed == 1 ? "" : "s") + " would be lost") +
                                      ") and re-load it from disk.";
                            }
                        }
                        result.ErrorMessage = why == null
                            ? "LoadFamily back into project failed, and Revit reported no failure message. " +
                              "No document for this family is open either - see the log for the pre-flight line."
                            : $"LoadFamily back into project failed: {why}";
                        StingLog.Warn($"PropagateUniversalTag: '{targetName}' load refused — {result.ErrorMessage}");
                        try { tg.RollBack(); } catch (Exception rbEx) { StingLog.Warn($"{targetName}: tg.RollBack after load fail: {rbEx.Message}"); }
                        return result;
                    }

                    // Everything succeeded — replace the canonical .rfa.
                    //
                    // NEVER delete-then-move. That opens a window in which the
                    // family exists nowhere: if the move then fails - a lock, a
                    // scanner, a full disk - the canonical family is destroyed,
                    // and the old code carried straight on to Success = true. A
                    // destroyed family reported as a success is the worst
                    // outcome this command can produce, and across 200 families
                    // it only has to happen once.
                    //
                    // File.Replace is atomic on NTFS and leaves the original in
                    // place on failure. It needs the destination to exist, so a
                    // first-time write is a plain Move.
                    try
                    {
                        if (File.Exists(finalPath)) File.Replace(tempPath, finalPath, null);
                        else File.Move(tempPath, finalPath);
                        TryDeleteTempDir(tempDir);
                    }
                    catch (Exception mvEx)
                    {
                        // The project is correct and the artefact survives in temp,
                        // but the library on disk was NOT updated - and the next
                        // deploy or lookup reads the library, not the project. Say
                        // so rather than counting it as a clean success.
                        result.DiskWriteFailed = true;
                        result.DiskWriteDetail = $"{mvEx.Message} — updated family kept at {tempPath}";
                        StingLog.Warn($"{targetName}: replace temp → final: {mvEx.Message} " +
                                      $"(project state OK; the library .rfa is UNCHANGED; artefact at {tempPath})");
                    }

                    tg.Assimilate();
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                    StingLog.Error($"PropagateUniversalTag: {targetName}", ex);
                    try { if (tg.HasStarted() && !tg.HasEnded()) tg.RollBack(); } catch { }
                    try { famDoc?.Close(false); } catch (Exception closeEx) { StingLog.Warn($"Close famDoc: {closeEx.Message}"); }
                    if (tempDir != null) TryDeleteTempDir(tempDir); // don't leak a half-made temp dir on an unhandled throw
                }
            }
            return result;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────────────────────────

        /// <summary>True when a family name is a leftover from a pre-fix run
        /// that loaded the clone under its temp file name. Shared with
        /// <see cref="MigrateTagLabelReferencesCommand"/> so its collector can
        /// exclude the same junk (both markers covered here).</summary>
        internal static bool IsTempNamed(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return false;
            return familyName.IndexOf(".rfa.sting-propagate-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   familyName.IndexOf(".rfa.sting-migrate-", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// An open family document whose file name matches <paramref name="familyName"/>,
        /// or null. Revit refuses to load a family while a document for it is open, and
        /// reports nothing when it does, so this is the difference between a named cause
        /// and a shrug.
        /// </summary>
        internal static Document FindOpenFamilyDocument(
            Autodesk.Revit.ApplicationServices.Application app, string familyName, Document exclude)
        {
            if (app == null || string.IsNullOrEmpty(familyName)) return null;
            try
            {
                foreach (Document d in app.Documents)
                {
                    if (d == null || !d.IsFamilyDocument) continue;
                    if (exclude != null && ReferenceEquals(d, exclude)) continue;
                    // Title carries the file name (with or without .rfa depending on
                    // version); PathName is empty for a document opened by EditFamily.
                    string bare = Path.GetFileNameWithoutExtension(d.Title ?? "");
                    if (string.Equals(bare, familyName, StringComparison.OrdinalIgnoreCase)) return d;
                }
            }
            catch (Exception ex) { StingLog.Warn($"FindOpenFamilyDocument('{familyName}'): {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// How many placed elements use this family's types. Called only on the
        /// failure path, where a full-document pass is cheap next to the run itself,
        /// and only to tell the operator what deleting the family would cost.
        /// Returns -1 when it could not be counted: reporting that as 0 would
        /// invite a delete on no evidence.
        /// </summary>
        private static int CountPlacedInstances(Document doc, Family family)
        {
            try
            {
                var symbolIds = new HashSet<ElementId>(family.GetFamilySymbolIds());
                if (symbolIds.Count == 0) return 0;
                return new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Count(e => symbolIds.Contains(e.GetTypeId()));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CountPlacedInstances: {ex.Message}");
                return -1;
            }
        }

        private static void TryDeleteTempDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception ex) { StingLog.Warn($"Delete temp dir '{dir}': {ex.Message}"); }
        }

        /// <summary>
        /// Add every wanted shared parameter that the family does not already carry.
        /// Style/visibility params are TYPE params (mirrors MigrateTagFamilies).
        /// Must run inside an open transaction on the family document.
        /// </summary>
        /// <summary>
        /// Convert any Instance-scoped tier gate in the clone to Type, and report how
        /// many had to be converted.
        ///
        /// MR_PARAMETERS.csv declares every TAG_PARA_STATE_*_BOOL and
        /// TAG_WARN_VISIBLE_BOOL as Type, and SetParagraphDepthCommand writes to
        /// element TYPES - so an Instance-scoped gate is present, looks right in
        /// Family Types, and can never be driven. The universal master carries
        /// _1, _2 and WARN_VISIBLE as Instance while _4.._10 are Type (seen
        /// 2026-09-17), and AddMissingParams skips a parameter that already exists,
        /// so without this the split is copied into all 206 families.
        ///
        /// Must run inside an open transaction on the family document.
        /// </summary>
        private static int MakeVisibilityParamsType(FamilyManager fm)
        {
            if (fm == null) return 0;
            int converted = 0;
            foreach (string name in TagFamilyConfig.VisibilityParams)
            {
                try
                {
                    FamilyParameter fp = fm.get_Parameter(name);
                    if (fp == null || !fp.IsInstance) continue;
                    fm.MakeType(fp);
                    converted++;
                    StingLog.Info($"PropagateUniversalTag: {name} converted Instance -> Type in the clone");
                }
                catch (Exception ex)
                {
                    // Reported, not swallowed: a gate left Instance is a tier that
                    // silently cannot be switched.
                    StingLog.Warn($"PropagateUniversalTag: could not convert {name} to Type: {ex.Message}");
                }
            }
            return converted;
        }

        /// <summary>
        /// Adds the standard parameters and type variants to the MASTER, once,
        /// so every clone inherits them instead of re-creating them.
        ///
        /// <para>A backup is written first and is not optional. The master lives
        /// only as a loaded family in the project - there is no .rfa for it in
        /// the library - so until this runs, the single copy of an
        /// evening's hand-authored label work is inside one .rvt. The backup is
        /// the first time it exists on disk, and it is taken BEFORE anything is
        /// changed; if the backup cannot be written, nothing is changed.</para>
        ///
        /// <para>Idempotent: a second run finds every parameter present and adds
        /// nothing. Fail-soft: any failure leaves the master as it was and the
        /// clones add their own parameters as before.</para>
        /// </summary>
        private static Family PrimeMaster(Document doc,
            Autodesk.Revit.ApplicationServices.Application app,
            Family master, string sharedParamFile,
            List<string> styleAndVisParams, List<TypeVariantSpec> variants,
            Dictionary<string, ElementId> arrowheads)
        {
            Document mfd = null;

            // Captured BEFORE anything is touched. Reading it after the reload -
            // which is where it was, and what broke the run at 00:24 on
            // 2026-09-22 - reads a property off the reference the reload has just
            // invalidated. The guard against stale references used a stale
            // reference to do its work.
            string masterName;
            try { masterName = master.Name; }
            catch (Exception nex)
            {
                StingLog.Warn($"PrimeMaster: cannot read master name ({nex.Message}) - skipping");
                return master;
            }

            try
            {
                mfd = doc.EditFamily(master);
                if (mfd == null) { StingLog.Warn("PrimeMaster: EditFamily(master) returned null"); return master; }

                var defFile = app.OpenSharedParameterFile();
                if (defFile == null) { StingLog.Warn("PrimeMaster: OpenSharedParameterFile returned null"); return master; }

                // Nothing to do is the common case on every run after the first.
                var have = new HashSet<string>(
                    mfd.FamilyManager.GetParameters().Select(x => x.Definition.Name),
                    StringComparer.OrdinalIgnoreCase);
                int missing = styleAndVisParams.Count(x => !string.IsNullOrEmpty(x) && !have.Contains(x));
                if (missing == 0)
                {
                    StingLog.Info("PrimeMaster: master already carries every standard parameter - nothing to do");
                    return master;
                }

                // ── Backup FIRST, and abandon if it fails ──
                string outDir = TagFamilyConfig.GetOutputDirectory();
                string backupDir = Path.Combine(outDir, "_master_backups");
                Directory.CreateDirectory(backupDir);
                string backupPath = Path.Combine(backupDir,
                    master.Name.Replace('/', '-') + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".rfa");
                try
                {
                    mfd.SaveAs(backupPath, new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 });
                    StingLog.Info($"PrimeMaster: backed up master to {backupPath}");
                }
                catch (Exception bex)
                {
                    StingLog.Warn($"PrimeMaster: backup FAILED ({bex.Message}) - master left untouched");
                    return master;
                }

                int added = 0, types = 0;
                using (var tx = new Transaction(mfd, "STING Prime universal master"))
                {
                    tx.Start();
                    added = AddMissingParams(mfd.FamilyManager, defFile, styleAndVisParams);
                    try { types = TagTypeVariantWriter.CreateStandardVariants(mfd.FamilyManager, variants, arrowheads); }
                    catch (Exception vex) { StingLog.Warn($"PrimeMaster: type variants: {vex.Message}"); }
                    tx.Commit();
                }

                // Back into the project, because EditFamily clones the IN-PROJECT
                // master - a saved file the project does not know about would
                // change nothing.
                // Deliberately NOT in outDir. That is the tag library: a file
                // there becomes a 207th "tag family" with no declaration, which
                // fails EveryShippedTagFamilyHasADeclaration, drifts the content
                // manifest, and would be published by Promote Tag Library. The
                // master is not a tag family - it is what tag families are made
                // from - so it lives beside its backups.
                string masterDir = Path.Combine(outDir, "_master");
                Directory.CreateDirectory(masterDir);
                string masterPath = Path.Combine(masterDir, master.Name.Replace('/', '-') + ".rfa");
                mfd.SaveAs(masterPath, new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 });
                mfd.Close(false); mfd = null;

                using (var lt = new Transaction(doc, "STING Reload primed master"))
                {
                    lt.Start();
                    bool ok = doc.LoadFamily(masterPath, new TagFamilyLoadOptions(), out _);
                    if (ok) lt.Commit(); else lt.RollBack();
                    StingLog.Info($"PrimeMaster: added {added} parameter(s), {types} type variant(s); " +
                                  $"reload into project {(ok ? "OK" : "REFUSED - clones will add their own")}");
                }

                // By NAME, not by the old reference. Revit usually updates the
                // Family in place on an overwriting load, but "usually" is not a
                // contract, and the cost of being wrong is every remaining
                // family.
                return ReFindMaster(doc, masterName, master);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PrimeMaster: {ex.Message} - clones will add their own parameters");
                // Re-find here too. The throw may have happened AFTER the reload,
                // in which case the caller's reference is already dead and
                // handing it back guarantees the failure this method was meant
                // to prevent. That is exactly what happened on the first run.
                return ReFindMaster(doc, masterName, master);
            }
            finally
            {
                try { mfd?.Close(false); } catch (Exception cex) { StingLog.Warn($"PrimeMaster close: {cex.Message}"); }
            }
        }

        /// <summary>
        /// Looks the master up again BY NAME after it has been reloaded.
        ///
        /// <para>An overwriting LoadFamily can replace the Family element, and
        /// the propagation loop calls EditFamily(master) once per target. A dead
        /// reference throws InvalidObjectException on the first of them and
        /// takes the whole run down - which is how this was found.</para>
        ///
        /// <para><paramref name="name"/> must have been captured BEFORE the
        /// reload. Reading it from the reference afterwards is the bug this
        /// exists to fix.</para>
        /// </summary>
        private static Family ReFindMaster(Document doc, string name, Family fallback)
        {
            try
            {
                var found = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));

                if (found != null) return found;

                // Nothing to do about it here, but say so loudly: if the
                // fallback is also dead the run is about to fail, and this line
                // is the difference between a diagnosable failure and a mystery.
                StingLog.Warn($"PrimeMaster: could not re-find master '{name}' after reload. " +
                              "Falling back to the original reference, which may no longer be valid - " +
                              "if the run fails immediately, re-run it and the master will be found fresh.");
            }
            catch (Exception ex) { StingLog.Warn($"PrimeMaster.ReFindMaster('{name}'): {ex.Message}"); }
            return fallback;
        }

        private static int AddMissingParams(FamilyManager fm, DefinitionFile defFile, List<string> wanted)
        {
            int added = 0;
            var existing = new HashSet<string>(
                fm.GetParameters().Select(p => p.Definition.Name),
                StringComparer.OrdinalIgnoreCase);

            // Index the shared-parameter file ONCE.
            //
            // This used to be a linear scan per wanted parameter: 138 params x 38
            // groups x 3,617 definitions, with every Definition.Name crossing the
            // Revit API boundary. Measured 2026-09-21 on 'STING - Duct Tag': the
            // whole family took 115s, of which 97s - 85% - was this loop. Type
            // variants were 14% and the save/load round trip 1.3%.
            //
            // First occurrence wins, exactly as the nested scan did: it broke out
            // of both loops on the first match, so a name defined in two groups
            // resolved to the earlier group. Preserved deliberately - changing
            // which definition wins would silently change which GUID a parameter
            // binds to.
            // ORDINAL, not OrdinalIgnoreCase. The scan compared with `==`, so a
            // name differing only in case did NOT match and the parameter was
            // quietly not added. Case-insensitive lookup would start matching
            // those - probably an improvement, but it changes which definition,
            // and therefore which GUID, a parameter binds to. That is not a
            // change to make as a side effect of a speed fix.
            var index = new Dictionary<string, ExternalDefinition>(StringComparer.Ordinal);
            foreach (DefinitionGroup grp in defFile.Groups)
                foreach (Definition def in grp.Definitions)
                    if (def is ExternalDefinition ed && !index.ContainsKey(def.Name))
                        index[def.Name] = ed;

            foreach (string paramName in wanted)
            {
                if (string.IsNullOrEmpty(paramName) || existing.Contains(paramName)) continue;

                ExternalDefinition extDef;
                if (!index.TryGetValue(paramName, out extDef) || extDef == null) continue;

                try
                {
                    fm.AddParameter(extDef, GroupTypeId.General, /*isInstance*/ false);
                    added++;
                    existing.Add(paramName);
                }
                catch (Exception ex) { StingLog.Warn($"AddMissingParams '{paramName}': {ex.Message}"); }
            }
            return added;
        }

        /// <summary>Pick the universal master family from the loaded STING tag families.</summary>
        private static Family PickMaster(List<Family> families)
        {
            var items = families
                .Select(f => new StingListPicker.ListItem
                {
                    Label = f.Name,
                    Detail = f.FamilyCategory?.Name ?? "",
                    Tag = f,
                    IsSelected = false,
                })
                .ToList();

            // Pre-select the family that LOOKS like the master, because
            // StingListPicker.AcceptSelection falls back to the FIRST item when a
            // single-select list is OK'd with nothing highlighted. Unseeded, that
            // fallback silently nominates the alphabetically-first STING tag
            // family - "STING - 5-Gauss Marker Tag" - and propagates ITS label
            // over every target. Seeding makes the fallback land on the right one.
            var likely = items.FirstOrDefault(i => LooksLikeUniversalMaster(i.Label));
            if (likely != null) likely.IsSelected = true;

            List<StingListPicker.ListItem> picked;
            try
            {
                picked = StingListPicker.Show(
                    "Choose the UNIVERSAL master family",
                    "Pick the one family that carries the hand-built universal label " +
                    "(from UNIVERSAL_TAG_LABEL_BUILD_SHEET.md). Every OTHER loaded STING " +
                    "tag family becomes a propagation target.",
                    items, allowMultiSelect: false);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PickMaster: picker failed: {ex.Message}");
                return null;
            }

            var chosen = picked?.FirstOrDefault()?.Tag as Family;
            if (chosen == null) return null;

            // Belt as well as braces: whatever route produced this family, a
            // non-universal master overwrites 205 labels with the wrong one, and
            // that is not recoverable from inside Revit. Name it and make the
            // operator agree.
            if (!LooksLikeUniversalMaster(chosen.Name))
            {
                var warn = new TaskDialog("Propagate Universal Tag");
                warn.MainInstruction = $"'{chosen.Name}' does not look like the universal master.";
                warn.MainContent =
                    "Its label will be cloned over every target family, replacing theirs.\n\n" +
                    "The master is normally named 'STING_Tag_Universal' or similar. If you clicked " +
                    "OK without highlighting a family, this is the first one in the list, not your " +
                    "master.\n\nPropagate this family's label anyway?";
                warn.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                warn.DefaultButton = TaskDialogResult.No;
                if (warn.Show() != TaskDialogResult.Yes)
                {
                    StingLog.Info($"PickMaster: '{chosen.Name}' declined as master - nothing done");
                    return null;
                }
                StingLog.Warn($"PickMaster: proceeding with non-universal master '{chosen.Name}' (operator confirmed)");
            }
            return chosen;
        }

        /// <summary>
        /// Whether a family name reads as the hand-built universal master. Used only
        /// to seed the picker and to challenge an unlikely choice - never to pick
        /// silently on the operator's behalf.
        /// </summary>
        private static bool LooksLikeUniversalMaster(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return false;
            return familyName.IndexOf("Tag_Universal", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("Universal Tag", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Scope the propagation targets: ALL candidates, or CHOOSE a subset
        /// (used for the mandatory one-family smoke test — pick Duct only).
        /// Returns null when cancelled.
        /// </summary>
        private static List<Family> ChooseTargets(List<Family> candidates, out string scopeLabel)
        {
            scopeLabel = "";
            var td = new TaskDialog("Propagate Universal Tag — scope");
            td.MainInstruction = $"Propagate to which of the {candidates.Count} target families?";
            td.MainContent =
                "SMOKE TEST FIRST: choose a single family (Duct) and verify the result " +
                "in Revit before scaling to all 206.";
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "CHOOSE families … (recommended — start with Duct)",
                "Multi-select picker. Tick just the target(s) to propagate to.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"ALL {candidates.Count} families",
                "Propagate to every loaded STING tag family. Only after the smoke test passes.");

            var choice = td.Show();
            if (choice == TaskDialogResult.Cancel)
            {
                StingLog.Info("ChooseTargets: scope dialog closed with Cancel");
                return null;
            }

            if (choice == TaskDialogResult.CommandLink2)
            {
                scopeLabel = "ALL";
                StingLog.Info($"ChooseTargets: ALL selected - {candidates.Count} target(s)");
                return candidates;
            }

            // CHOOSE
            var items = candidates
                .Select(f => new StingListPicker.ListItem
                {
                    Label = f.Name,
                    Detail = f.FamilyCategory?.Name ?? "",
                    Tag = f,
                    IsSelected = f.Name.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0,
                })
                .ToList();

            List<StingListPicker.ListItem> picked;
            try
            {
                picked = StingListPicker.Show(
                    "Choose target families",
                    "Duct is ALREADY highlighted for the smoke test - just press OK. " +
                    "This list allows multiple selections, so clicking a highlighted row " +
                    "turns it OFF; use Ctrl+click to add another without losing it.",
                    items, allowMultiSelect: true);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ChooseTargets: picker failed: {ex.Message}");
                return null;
            }
            // A cancel and an empty tick-list are NOT the same thing, and treating
            // them the same is what made this look like the command dying. The
            // empty case has a specific cause: the Duct rows arrive PRE-SELECTED
            // above, the picker's list box is SelectionMode.Multiple, and in that
            // mode a plain click TOGGLES a row. So clicking the duct family to
            // "pick" it un-picks it, and OK then returns nothing. Observed
            // 2026-09-17 at 20:57:58: "scope dialog cancelled - nothing done"
            // seconds after the operator picked Duct.
            if (picked == null)
            {
                StingLog.Info("ChooseTargets: target picker cancelled");
                return null;
            }
            if (picked.Count == 0)
            {
                StingLog.Warn("ChooseTargets: OK pressed with nothing highlighted - " +
                              "likely the pre-selected row was clicked and toggled off");
                var empty = new TaskDialog("Propagate Universal Tag — nothing selected");
                empty.MainInstruction = "No target families were highlighted, so nothing was propagated.";
                empty.MainContent =
                    "The list allows multiple selections, which means a click TOGGLES a row.\n" +
                    "The Duct family starts out already highlighted for the smoke test, so " +
                    "clicking it turns it OFF.\n\n" +
                    "Re-run and either press OK straight away (Duct is already highlighted), " +
                    "or click a DIFFERENT row to add it. Ctrl+click toggles one row without " +
                    "disturbing the rest.";
                empty.CommonButtons = TaskDialogCommonButtons.Close;
                empty.Show();
                return null;
            }

            var chosen = picked.Select(p => p.Tag as Family).Where(f => f != null).ToList();
            scopeLabel = $"CHOSEN ({chosen.Count})";
            StingLog.Info($"ChooseTargets: {chosen.Count} target(s) chosen - " +
                          string.Join(", ", chosen.Take(5).Select(f => f.Name)) +
                          (chosen.Count > 5 ? $", +{chosen.Count - 5} more" : ""));
            return chosen;
        }
    }
}
