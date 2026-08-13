// ============================================================================
// NormaliseTagGatesCommand.cs — make every tier gate a TYPE parameter.
//
// THE DEFECT
//
// STING_Tag_Universal binds its eleven tier gates two different ways:
//
//   TAG_PARA_STATE_4..10_BOOL   type
//   TAG_PARA_STATE_1/2/3_BOOL   instance
//   TAG_WARN_VISIBLE_BOOL       instance
//
// That splits control of one feature across two places. Instance parameters do
// not appear in Edit Type, so an operator looking there sees seven of eleven
// gates and reasonably concludes the rest do not exist — which is exactly what
// happened on 2026-08-13.
//
// Worse, it silently breaks the commands built to drive depth.
// SetTagTierDefaultsCommand and SetParagraphDepthCommand both write the gates on
// the FamilySymbol — the TYPE. A gate bound as an instance parameter is not on
// the symbol, so those writes cannot reach it, find nothing to set, and report
// success. Tiers 1-3 have never been settable by either command.
//
// WHY TYPE AND NOT INSTANCE
//
// Type is what the rest of the system already assumes: MR_PARAMETERS declares
// these "Generic Models, Type", SetTagTierDefaults writes the symbol, and the
// per-drawing depth feature is meant to act on a tag type rather than on every
// placed tag. Instance gates would be more flexible — different depth on two
// tags in one view — but nothing in the codebase drives them that way, and a
// half-wired flexibility is what caused this. Consistency wins.
//
// The trade is real and worth stating: after normalising, depth is switched in
// Edit Type and applies to every tag of that type. Per-tag depth would need
// separate tag types.
//
// WHAT IT DOES
//
// For each selected loaded tag family: EditFamily, MakeType on every gate that
// is currently an instance parameter, SaveAs, reload. Families whose gates are
// already type are reported untouched. It never creates a gate that is absent —
// a family missing TAG_PARA_STATE_2_BOOL is reported, not "fixed", because
// inventing a gate no label row references would just be noise.
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
using StingTools.Tags;   // TagFamilyConfig.GetOutputDirectory, TagFamilyLoadOptions

namespace StingTools.Commands.TagStudio
{
    /// <summary>
    /// Converts every TAG_PARA_STATE_*_BOOL / TAG_WARN_VISIBLE_BOOL that is bound
    /// as an instance parameter into a type parameter, so one feature is driven
    /// from one place.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class NormaliseTagGatesCommand : IExternalCommand
    {
        private const string DialogTitle = "Normalise Tag Gates";
        private const string WarnGate = "TAG_WARN_VISIBLE_BOOL";

        private static List<string> AllGates()
        {
            var g = new List<string>();
            for (int i = 1; i <= 10; i++) g.Add("TAG_PARA_STATE_" + i + "_BOOL");
            g.Add(WarnGate);
            return g;
        }

        private sealed class FamilyOutcome
        {
            public string Family;
            public int Converted;
            public int AlreadyType;
            public int Missing;
            public string Error;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp;
            Document doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                message = "No active document.";
                TaskDialog.Show(DialogTitle, "No active document.\n\nOpen a project with tag families loaded.");
                return Result.Failed;
            }
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(DialogTitle,
                    "This runs on a PROJECT, not inside the Family Editor — it edits and\n" +
                    "reloads each loaded tag family in turn.\n\n" +
                    "Close the family editor and run it from the project.");
                return Result.Cancelled;
            }

            try
            {
                var fams = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Where(f => f.FamilyCategory != null &&
                                f.FamilyCategory.Name.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Where(f => f.IsEditable)
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (fams.Count == 0)
                {
                    TaskDialog.Show(DialogTitle, "No editable tag families are loaded in this project.");
                    return Result.Cancelled;
                }

                var items = fams
                    .Select(f => new StingTools.Select.StingListPicker.ListItem { Label = f.Name })
                    .ToList();
                var picked = StingTools.Select.StingListPicker.Show(
                    DialogTitle,
                    "Which tag families should have their gates normalised to TYPE parameters?",
                    items,
                    true /* multi-select */);

                if (picked == null || picked.Count == 0) return Result.Cancelled;
                var chosen = new HashSet<string>(picked.Select(p => p.Label), StringComparer.Ordinal);
                var targets = fams.Where(f => chosen.Contains(f.Name)).ToList();
                if (targets.Count == 0) return Result.Cancelled;

                var outcomes = new List<FamilyOutcome>();
                foreach (Family fam in targets)
                    outcomes.Add(Normalise(doc, uiapp.Application, fam));

                TaskDialog.Show(DialogTitle, BuildReport(outcomes));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("NormaliseTagGatesCommand", ex);
                message = ex.Message;
                TaskDialog.Show(DialogTitle, "Failed:\n\n" + ex.Message);
                return Result.Failed;
            }
        }

        private static FamilyOutcome Normalise(Document doc,
            Autodesk.Revit.ApplicationServices.Application app, Family fam)
        {
            var outcome = new FamilyOutcome { Family = fam.Name };
            Document famDoc = null;
            try
            {
                famDoc = doc.EditFamily(fam);
                if (famDoc == null)
                {
                    outcome.Error = "EditFamily returned null";
                    return outcome;
                }

                FamilyManager fm = famDoc.FamilyManager;
                var toConvert = new List<FamilyParameter>();

                foreach (string gateName in AllGates())
                {
                    FamilyParameter fp = null;
                    foreach (FamilyParameter cand in fm.Parameters)
                    {
                        if (string.Equals(cand.Definition?.Name, gateName, StringComparison.Ordinal))
                        { fp = cand; break; }
                    }
                    if (fp == null) { outcome.Missing++; continue; }

                    bool isInstance;
                    try { isInstance = fp.IsInstance; } catch { isInstance = false; }
                    if (isInstance) toConvert.Add(fp);
                    else outcome.AlreadyType++;
                }

                if (toConvert.Count == 0)
                {
                    // Nothing to do — do NOT save and reload. A pointless SaveAs
                    // churns the .rfa and invalidates the operator's sense of what
                    // this command touched.
                    famDoc.Close(false);
                    return outcome;
                }

                using (var tx = new Transaction(famDoc, "STING Normalise Tag Gates"))
                {
                    tx.Start();
                    foreach (FamilyParameter fp in toConvert)
                    {
                        string n = fp.Definition?.Name;
                        try
                        {
                            fm.MakeType(fp);
                            outcome.Converted++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"NormaliseTagGates {fam.Name}: MakeType('{n}') — {ex.Message}");
                        }
                    }
                    tx.Commit();
                }

                if (outcome.Converted == 0)
                {
                    famDoc.Close(false);
                    outcome.Error = "no gate could be converted (see log)";
                    return outcome;
                }

                string savePath = Path.Combine(TagFamilyConfig.GetOutputDirectory(), fam.Name + ".rfa");
                try
                {
                    famDoc.SaveAs(savePath, new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 });
                }
                catch (Exception saveEx)
                {
                    outcome.Error = "SaveAs failed: " + saveEx.Message;
                    StingLog.Warn($"NormaliseTagGates SaveAs {fam.Name}: {saveEx.Message}");
                    famDoc.Close(false);
                    return outcome;
                }
                famDoc.Close(false);
                famDoc = null;

                using (var tx = new Transaction(doc, $"STING Reload {fam.Name}"))
                {
                    tx.Start();
                    if (File.Exists(savePath))
                    {
                        try { doc.LoadFamily(savePath, new TagFamilyLoadOptions(), out _); }
                        catch (Exception loadEx)
                        {
                            outcome.Error = "reload failed: " + loadEx.Message;
                            StingLog.Warn($"NormaliseTagGates reload {fam.Name}: {loadEx.Message}");
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                outcome.Error = ex.Message;
                StingLog.Error($"NormaliseTagGates: {fam.Name}", ex);
                try { famDoc?.Close(false); } catch (Exception closeEx) { StingLog.Warn($"Close famDoc: {closeEx.Message}"); }
            }
            return outcome;
        }

        private static string BuildReport(List<FamilyOutcome> outcomes)
        {
            var sb = new StringBuilder();
            int converted = outcomes.Sum(o => o.Converted);
            int touched = outcomes.Count(o => o.Converted > 0);
            int failed = outcomes.Count(o => !string.IsNullOrEmpty(o.Error));

            sb.AppendLine($"Families processed : {outcomes.Count}");
            sb.AppendLine($"Families changed   : {touched}");
            sb.AppendLine($"Gates converted    : {converted}   (instance -> type)");
            if (failed > 0) sb.AppendLine($"Families failed    : {failed}");
            sb.AppendLine();

            foreach (var o in outcomes.OrderByDescending(x => x.Converted))
            {
                if (!string.IsNullOrEmpty(o.Error))
                    sb.AppendLine($"  FAIL  {o.Family}: {o.Error}");
                else if (o.Converted > 0)
                    sb.AppendLine($"  {o.Converted,2} ->type  {o.Family}" +
                                  (o.Missing > 0 ? $"   ({o.Missing} gate(s) not present)" : ""));
                else
                    sb.AppendLine($"   already  {o.Family}" +
                                  (o.Missing > 0 ? $"   ({o.Missing} gate(s) not present)" : ""));
            }

            if (converted > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Depth is now switched in ONE place: select a tag > Edit Type, and the");
                sb.AppendLine("change applies to every tag of that type. The converted gates are no");
                sb.AppendLine("longer in the Properties palette. 'Tier Defaults' can now reach them.");
                sb.AppendLine();
                sb.AppendLine("Any per-instance value previously set on a converted gate is gone —");
                sb.AppendLine("a type parameter holds one value per type. Re-check depth on a sheet");
                sb.AppendLine("before issuing.");
            }
            return sb.ToString();
        }
    }
}
