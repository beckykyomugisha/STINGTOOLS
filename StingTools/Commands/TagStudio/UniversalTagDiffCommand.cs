// ============================================================================
// UniversalTagDiffCommand.cs — audit a tag family's gates and bindings.
//
// WHAT THIS COMMAND IS FOR
//
// The universal tag renders its tiers by content gating: each label row is a
// calculated value whose formula is if(TAG_PARA_STATE_n_BOOL, <source>, ""), and
// Revit omits a parameter that evaluates to empty, so a gate turned off collapses
// its rows. Verified live on STING_Tag_Universal 2026-08-13: unticking
// TAG_PARA_STATE_2_BOOL collapsed the tag to the ISO code alone.
//
// This command reports the parts of that machinery the Revit API can actually
// observe: which gates are bound, whether each is an instance or a type
// parameter, and which of the spec's source parameters exist in the family.
//
// WHAT IT DELIBERATELY DOES NOT CLAIM
//
// It cannot see label calculated values. They live in the label's own field
// definition, NOT in FamilyManager.Parameters, and the API exposes no reader for
// them. An earlier version of this command enumerated family parameters, failed
// to find names like "Show T4 - Commissioning - State", and reported all 64 rows
// "calculated value missing" — then concluded from that absence that the rows had
// never been built. Every one of them existed, correctly formulated, in the
// label. The absence was an artefact of looking in the wrong place.
//
// So the row-by-row comparison is gone rather than reworded. A check that cannot
// observe its subject must not report on it: a confident wrong answer cost more
// here than no answer would have. To inspect label rows, open Edit Label in the
// Family Editor and read the Formula field of a calculated value — that is the
// only reliable route, and the report says so.
//
// READ-ONLY. Writes two CSVs and changes nothing.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Tags;

namespace StingTools.Commands.TagStudio
{
    /// <summary>
    /// Read-only audit of a tag family's tier gates and spec source bindings.
    /// Reports gate presence and instance/type binding, flags the mixed-binding
    /// inconsistency, and writes a full parameter inventory.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class UniversalTagDiffCommand : IExternalCommand
    {
        private const string WarnGate = "TAG_WARN_VISIBLE_BOOL";
        private const string DialogTitle = "Universal Tag — Gate Audit";

        /// <summary>All eleven gates the tier system uses, in tier order.</summary>
        private static IEnumerable<string> AllGates()
        {
            for (int i = 1; i <= 10; i++) yield return "TAG_PARA_STATE_" + i + "_BOOL";
            yield return WarnGate;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // ExternalCommandData is always null on the dock-panel path; RunCommand
            // discards `message`, so every exit here has to speak for itself.
            UIApplication uiapp = commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp;
            Document doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                message = "No active document.";
                TaskDialog.Show(DialogTitle,
                    "No active document.\n\n" +
                    "Open a tag family in the Family Editor, or a project with tag " +
                    "families loaded, and run this again.");
                return Result.Failed;
            }

            try
            {
                UniversalTagRowSpec.Reload();
                var spec = UniversalTagRowSpec.Load();
                if (spec.Count == 0)
                {
                    TaskDialog.Show(DialogTitle,
                        $"The row spec is unavailable — {UniversalTagRowSpec.DataFileName} was not found\n" +
                        "in the plugin's data folder.\n\n" +
                        "Regenerate it from the build sheet:\n" +
                        "  python tools/extract_universal_tag_rows.py\n\n" +
                        "No audit was made.");
                    return Result.Cancelled;
                }

                if (doc.IsFamilyDocument)
                    return ReportOn(doc.FamilyManager, doc.Title, doc);

                Family fam = PickTagFamily(doc);
                if (fam == null) return Result.Cancelled;

                Document fdoc = null;
                try
                {
                    fdoc = doc.EditFamily(fam);
                    if (fdoc == null || !fdoc.IsFamilyDocument)
                    {
                        TaskDialog.Show(DialogTitle, $"Could not open '{fam.Name}' for inspection.");
                        return Result.Failed;
                    }
                    return ReportOn(fdoc.FamilyManager, fam.Name, doc);
                }
                finally
                {
                    if (fdoc != null)
                    {
                        try { fdoc.Close(false); }
                        catch (Exception ex) { StingLog.Info($"UniversalTagDiff: leaving '{fam.Name}' open — {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("UniversalTagDiffCommand", ex);
                message = ex.Message;
                TaskDialog.Show(DialogTitle, "Failed:\n\n" + ex.Message);
                return Result.Failed;
            }
        }

        // ------------------------------------------------------------------

        private static Result ReportOn(FamilyManager fm, string familyName, Document contextDoc)
        {
            if (fm == null)
            {
                TaskDialog.Show(DialogTitle, $"'{familyName}' has no FamilyManager.");
                return Result.Failed;
            }

            var byName = new Dictionary<string, FamilyParameter>(StringComparer.Ordinal);
            foreach (FamilyParameter fp in fm.Parameters)
            {
                string n = fp.Definition?.Name;
                if (!string.IsNullOrEmpty(n)) byName[n] = fp;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Family: {familyName}");
            sb.AppendLine($"Family parameters: {byName.Count}");
            sb.AppendLine();

            // ---- gates -------------------------------------------------------
            var missing = new List<string>();
            var instanceGates = new List<string>();
            var typeGates = new List<string>();

            foreach (string g in AllGates())
            {
                FamilyParameter fp;
                if (!byName.TryGetValue(g, out fp)) { missing.Add(g); continue; }
                bool isInstance;
                try { isInstance = fp.IsInstance; } catch { isInstance = false; }
                (isInstance ? instanceGates : typeGates).Add(g);
            }

            sb.AppendLine("  TIER GATES");
            sb.AppendLine($"    bound as TYPE parameters      {typeGates.Count,3}");
            sb.AppendLine($"    bound as INSTANCE parameters  {instanceGates.Count,3}");
            sb.AppendLine($"    not present                   {missing.Count,3}");

            if (missing.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("    NOT PRESENT — rows gated on these can never be switched:");
                foreach (string g in missing) sb.AppendLine($"      {g}");
            }

            // The split is the defect worth surfacing. Tier Defaults and
            // SetParagraphDepth both write gates on the FamilySymbol — the TYPE.
            // A gate bound as an instance parameter is not on the symbol, so those
            // writes cannot reach it and report success anyway.
            if (instanceGates.Count > 0 && typeGates.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("    MIXED BINDING — this family gates on both, which splits control:");
                sb.AppendLine($"      instance: {string.Join(", ", instanceGates.Select(Short))}");
                sb.AppendLine($"      type:     {string.Join(", ", typeGates.Select(Short))}");
                sb.AppendLine();
                sb.AppendLine("    Instance gates do NOT appear in Edit Type — look in the Properties");
                sb.AppendLine("    palette of a selected tag. 'Tier Defaults' writes the TYPE, so it");
                sb.AppendLine("    cannot set the instance ones. Run 'Normalise Gates' to make them");
                sb.AppendLine("    all type parameters.");
            }

            // ---- spec sources ------------------------------------------------
            var sources = UniversalTagRowSpec.SourceParameters();
            int bound = sources.Count(byName.ContainsKey);
            sb.AppendLine();
            sb.AppendLine("  SOURCE PARAMETERS the build sheet's rows read");
            sb.AppendLine($"    bound in this family          {bound,3} of {sources.Count}");
            sb.AppendLine("    (a row can still render a source that is not bound here — a label");
            sb.AppendLine("     pulls category parameters from the tagged element directly)");

            // ---- the honest limit --------------------------------------------
            sb.AppendLine();
            sb.AppendLine("  NOT CHECKED — label calculated values");
            sb.AppendLine("    The tier formulas live in the label's field definition, which the");
            sb.AppendLine("    Revit API does not expose. This audit cannot confirm or deny that a");
            sb.AppendLine("    row is gated. To check one: Family Editor > select the label >");
            sb.AppendLine("    Edit Label > select a row > fx, and read its Formula.");
            sb.AppendLine("    A gated row reads  if(TAG_PARA_STATE_n_BOOL, <source>, \"\")");

            // ---- verdict ------------------------------------------------------
            sb.AppendLine();
            if (missing.Count == 0 && instanceGates.Count == 0)
            {
                sb.AppendLine("GATES OK. All eleven are bound as type parameters, so Tier Defaults");
                sb.AppendLine("and per-drawing depth can drive every tier.");
            }
            else if (missing.Count == 0)
            {
                sb.AppendLine($"GATES PRESENT BUT SPLIT. All eleven are bound, {instanceGates.Count} as instance");
                sb.AppendLine("parameters. Tier depth works, but only from two different places.");
            }
            else
            {
                sb.AppendLine($"{missing.Count} GATE(S) MISSING. Rows depending on them cannot be switched at all.");
            }

            string invPath = WriteInventory(familyName, byName, contextDoc);
            if (!string.IsNullOrEmpty(invPath))
            {
                sb.AppendLine();
                sb.AppendLine("Family parameter inventory:");
                sb.AppendLine("  " + invPath);
            }

            StingLog.Info($"UniversalTagDiff [{familyName}]: gates type={typeGates.Count} " +
                          $"instance={instanceGates.Count} missing={missing.Count}; " +
                          $"spec sources bound {bound}/{sources.Count}");

            TaskDialog.Show(DialogTitle, sb.ToString());
            return Result.Succeeded;
        }

        /// <summary>TAG_PARA_STATE_4_BOOL -> T4; TAG_WARN_VISIBLE_BOOL -> WARN.</summary>
        private static string Short(string gate)
        {
            if (string.Equals(gate, WarnGate, StringComparison.Ordinal)) return "WARN";
            var m = System.Text.RegularExpressions.Regex.Match(gate, @"STATE_(\d+)_");
            return m.Success ? "T" + m.Groups[1].Value : gate;
        }

        /// <summary>
        /// In the Family Editor there is no project to anchor to, so
        /// OutputLocationHelper falls through to system temp and raises its
        /// "could not write to the project directory" dialog. A saved family has a
        /// better home: its own folder, beside the .rfa.
        /// </summary>
        private static string ResolveOutputDir(Document contextDoc)
        {
            try
            {
                if (contextDoc != null && contextDoc.IsFamilyDocument &&
                    !string.IsNullOrEmpty(contextDoc.PathName))
                {
                    string famDir = System.IO.Path.GetDirectoryName(contextDoc.PathName);
                    if (!string.IsNullOrEmpty(famDir) && System.IO.Directory.Exists(famDir))
                        return famDir;
                }
            }
            catch (Exception ex) { StingLog.Warn($"UniversalTagDiff.ResolveOutputDir: {ex.Message}"); }
            return OutputLocationHelper.GetOutputDirectory(contextDoc);
        }

        /// <summary>Every family parameter, with binding kind and formula.</summary>
        private static string WriteInventory(string familyName,
            Dictionary<string, FamilyParameter> byName, Document contextDoc)
        {
            try
            {
                string safe = string.Join("_", (familyName ?? "family").Split(System.IO.Path.GetInvalidFileNameChars()));
                string dir = ResolveOutputDir(contextDoc);
                if (string.IsNullOrEmpty(dir)) return null;
                string path = System.IO.Path.Combine(dir, $"universal_tag_params_{safe}.csv");

                var specSources = new HashSet<string>(UniversalTagRowSpec.SourceParameters(), StringComparer.Ordinal);
                var gates = new HashSet<string>(AllGates(), StringComparer.Ordinal);

                var sb = new StringBuilder();
                sb.AppendLine("Name,IsShared,Binding,HasFormula,IsTierGate,IsSpecSource,Formula");
                foreach (var kv in byName.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    FamilyParameter fp = kv.Value;
                    string formula = null;
                    bool shared = false, instance = false;
                    try { formula = fp.Formula; } catch { }
                    try { shared = fp.IsShared; } catch { }
                    try { instance = fp.IsInstance; } catch { }

                    sb.AppendLine(string.Join(",",
                        Q(kv.Key),
                        shared ? "yes" : "no",
                        instance ? "instance" : "type",
                        string.IsNullOrWhiteSpace(formula) ? "no" : "yes",
                        gates.Contains(kv.Key) ? "yes" : "no",
                        specSources.Contains(kv.Key) ? "yes" : "no",
                        Q(formula)));
                }

                System.IO.File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"UniversalTagDiff.WriteInventory: {ex.Message}");
                return null;
            }
        }

        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        private static Family PickTagFamily(Document doc)
        {
            var fams = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory != null &&
                            f.FamilyCategory.Name.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fams.Count == 0)
            {
                TaskDialog.Show(DialogTitle,
                    "No tag families are loaded in this project.\n\n" +
                    "Open a tag family in the Family Editor and run this again, " +
                    "or load the tag families first.");
                return null;
            }

            string chosen = StingTools.Select.StingListPicker.Show(
                DialogTitle,
                "Which tag family should be audited?",
                fams.Select(f => f.Name).ToList());

            if (string.IsNullOrEmpty(chosen)) return null;
            return fams.FirstOrDefault(f => string.Equals(f.Name, chosen, StringComparison.Ordinal));
        }
    }
}
