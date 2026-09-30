// AccFederatedComplianceCommand.cs — ACC_FederatedCompliance: ISO 19650 tag compliance of
// EVERY model in the ACC coordination federation, without opening any of them in Revit.
//
// The Revit half of V6/AccModelProperties (the read, over the ACC Model Properties API) and
// V6/AccFederatedCompliance (the tally). Everything that decides a number lives there,
// Revit-free and tested over loopback; this command gathers the inputs from the open
// document (credentials, the remembered coordination model set, STING's parameter names,
// taggable category names, the code vocabularies), writes the report and shows it.
//
// READ-ONLY against ACC and against the model. The index / query jobs it starts in ACC are
// the Model Properties service's own cached reads; nothing is created in Docs or Issues.
//
// Returns Failed when any model in the federation could not be read, so a workflow step
// cannot record "the federation is compliant" for a run that saw only part of it.

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
using StingTools.V6;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccFederatedComplianceCommand : IExternalCommand
    {
        private const string Title = "ACC — Federated Tag Compliance";

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_FederatedCompliance");
            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC federated compliance");
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                AccPullClashesCommand.Report(policy, Title,
                    "ACC is not set up for this project on this machine.\n\n" +
                    "BIM Coordination Center > ACC: enter the APS Client ID, 'Sign in with Autodesk', " +
                    "then 'Discover' to choose the ACC project.\n\n" + AccProjectScope.Describe(creds) + ".");
                return policy.IsUnattended ? Result.Failed : Result.Cancelled;
            }
            string containerId = creds.CoordContainer;

            // 1. Which federation: the remembered coordination model set, never a guess.
            AccFetchResult<List<AccModelSet>> sets;
            try { sets = AccModelCoordSync.ListModelSetsAsync(creds, containerId).GetAwaiter().GetResult(); }
            catch (Exception ex) { StingLog.Error("ACC_FederatedCompliance model sets", ex); AccPullClashesCommand.Report(policy, Title, "Model-set request failed: " + ex.Message); return Result.Failed; }
            if (!sets.Succeeded)
            {
                AccPullClashesCommand.Report(policy, Title, AccPullClashesCommand.FailureMessage("model sets", sets.Status, sets.HttpStatus, sets.Detail, containerId));
                return Result.Failed;
            }
            var choice = policy.ResolveModelSet(sets.Value);
            AccModelSet chosen = choice.Resolution == AccModelSetResolution.Chosen ? choice.Chosen : null;
            if (chosen == null)
            {
                if (!policy.MayPrompt || sets.Value.Count == 0)
                {
                    AccPullClashesCommand.Report(policy, Title,
                        "Cannot tell which coordination model set is the federation.\n\n" + choice.Reason +
                        "\n\nSet it on the BIM Coordination Center ACC card (or run Pull Clashes once and remember it).");
                    StingLog.Warn("ACC_FederatedCompliance: no model set — " + choice.Reason);
                    return Result.Failed;
                }
                string pick = StingListPicker.Show("ACC — pick the coordination model set",
                    "Every model in the latest version of this model set is checked.",
                    sets.Value.Select(s => $"{s.Name}  [{s.Id}]").ToList());
                if (string.IsNullOrEmpty(pick)) return Result.Cancelled;
                chosen = sets.Value.First(s => $"{s.Name}  [{s.Id}]" == pick);
            }

            // 2. Its latest version's documents.
            AccFetchResult<AccModelSetVersion> latest;
            try { latest = AccModelCoordSync.GetLatestModelSetVersionAsync(creds, containerId, chosen.Id).GetAwaiter().GetResult(); }
            catch (Exception ex) { StingLog.Error("ACC_FederatedCompliance latest version", ex); AccPullClashesCommand.Report(policy, Title, "Model-set version request failed: " + ex.Message); return Result.Failed; }
            if (!latest.Succeeded)
            {
                AccPullClashesCommand.Report(policy, Title, AccPullClashesCommand.FailureMessage(
                    $"the latest version of model set '{chosen.Name}'", latest.Status, latest.HttpStatus, latest.Detail, containerId));
                return Result.Failed;
            }
            if (latest.Value.Documents.Count == 0)
            {
                AccPullClashesCommand.Report(policy, Title,
                    $"Model set '{chosen.Name}' v{latest.Value.Version} contains no documents. Nothing was checked - this is not a compliant result.");
                return policy.IsUnattended ? Result.Failed : Result.Cancelled;
            }

            // 3. What to read: STING's names, never literals.
            var tokens = new List<string> { ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE, ParamRegistry.LVL,
                                            ParamRegistry.SYS, ParamRegistry.FUNC, ParamRegistry.PROD, ParamRegistry.SEQ };
            string tagName = ParamRegistry.TAG1;
            if (tokens.Any(string.IsNullOrEmpty) || string.IsNullOrEmpty(tagName))
            {
                AccPullClashesCommand.Report(policy, Title, "STING's parameter registry did not load (a token name is empty) - see the log.");
                return Result.Failed;
            }
            var options = new AccModelPropertiesOptions
            {
                ParameterNames = tokens.Concat(new[] { tagName }).ToList(),
                CategoryNames = TaggableCategoryNames(),
                AlwaysIncludeWhenPresent = tagName,
            };

            List<AccDocumentRead> reads;
            try
            {
                // Network only, no Revit API inside: safe to block on from the API thread.
                reads = AccModelProperties.ReadFederationAsync(creds, latest.Value.Documents, options).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                StingLog.Error("ACC_FederatedCompliance read", ex);
                AccPullClashesCommand.Report(policy, Title, "The federation could not be read: " + ex.Message);
                return Result.Failed;
            }

            var vocab = new AccComplianceVocabulary();
            try
            {
                vocab.Add(ParamRegistry.DISC, ISO19650Validator.ValidDiscCodes);
                vocab.Add(ParamRegistry.SYS, ISO19650Validator.ValidSysCodes);
                vocab.Add(ParamRegistry.FUNC, ISO19650Validator.ValidFuncCodes);
            }
            catch (Exception ex) { StingLog.Warn("ACC_FederatedCompliance vocabulary (codes not checked): " + ex.Message); }

            var report = AccFederatedCompliance.Build(reads, tokens, tagName, vocab);

            // 4. Report files.
            string header = $"STING ACC federated tag compliance — {DateTime.Now:yyyy-MM-dd HH:mm}\n" +
                            $"Model set: {chosen.Name} [{chosen.Id}] version {latest.Value.Version}\n" +
                            $"Checked from: {SafeTitle(doc)}";
            string summaryPath = null, elementsPath = null;
            try
            {
                summaryPath = OutputLocationHelper.GetRoutedTimestampedPath(doc, "Compliance", "STING_ACC_FederatedCompliance", ".csv");
                File.WriteAllText(summaryPath, AccFederatedCompliance.SummaryCsv(report, header), new UTF8Encoding(true));
                elementsPath = OutputLocationHelper.GetRoutedTimestampedPath(doc, "Compliance", "STING_ACC_FederatedCompliance_Elements", ".csv");
                File.WriteAllText(elementsPath, AccFederatedCompliance.ElementsCsv(report), new UTF8Encoding(true));
            }
            catch (Exception ex) { StingLog.Warn("ACC_FederatedCompliance report write: " + ex.Message); }

            foreach (var d in report.Documents)
                if (d.Status == AccDocumentReadStatus.Failed) StingLog.Warn($"ACC_FederatedCompliance: '{d.DocumentName}' NOT READ — {d.Detail}");
            StingLog.Info($"{Title}: {report.Headline()}" + (summaryPath != null ? " — " + summaryPath : ""));

            // 5. Show it.
            if (policy.MayPrompt)
            {
                var sb = new StringBuilder();
                foreach (var d in report.Documents)
                {
                    string line = d.Status switch
                    {
                        AccDocumentReadStatus.Read => d.Counts.Scanned == 0
                            ? $"  {d.DocumentName}: no elements in scope"
                            : $"  {d.DocumentName}: {d.Counts.Full:N0}/{d.Counts.Scanned:N0} fully tagged ({d.Counts.FullPct:F0} %), " +
                              $"{d.Counts.Untagged:N0} untagged; most missing: {d.Counts.TopMissing(2)}",
                        AccDocumentReadStatus.NotRevit => $"  {d.DocumentName}: skipped — {d.Detail}",
                        _ => $"  {d.DocumentName}: NOT READ — {d.Detail}",
                    };
                    sb.AppendLine(line);
                    if (d.Status == AccDocumentReadStatus.Read && d.AbsentParameters.Count > 0)
                        sb.AppendLine($"      STING parameters not in this model: {string.Join(", ", d.AbsentParameters.Take(4))}" +
                                      (d.AbsentParameters.Count > 4 ? " …" : ""));
                }
                if (report.Duplicates.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Duplicate tags: {report.Duplicates.Count} ({report.CrossModelDuplicates} across models — invisible to in-model checks)");
                    foreach (var dup in report.Duplicates.Take(5))
                        sb.AppendLine($"  {dup.Tag}: {string.Join(", ", dup.Occurrences.Select(o => o.DocumentName).Distinct().Take(3))}");
                }
                sb.AppendLine();
                sb.AppendLine(summaryPath != null ? "Summary: " + summaryPath : "The report could not be written — see StingTools_yyyyMMdd.log.");
                if (elementsPath != null) sb.AppendLine("Worklist: " + elementsPath);
                sb.AppendLine("Read-only: nothing was changed in ACC or in this model.");
                new TaskDialog(Title) { MainInstruction = report.Headline(), MainContent = sb.ToString() }.Show();
            }

            return report.Complete ? Result.Succeeded : Result.Failed;
        }

        /// <summary>The categories STING tags, by the display name the index's _RC field carries.
        /// Names are the running Revit's language; a model published from a Revit in another
        /// language reports different names (its tagged elements are still found through
        /// ASS_TAG_1_TXT - see AccModelPropertiesOptions.AlwaysIncludeWhenPresent).</summary>
        private static List<string> TaggableCategoryNames()
        {
            var names = new List<string>();
            foreach (var bic in SharedParamGuids.AllCategoryEnums)
            {
                try { string n = LabelUtils.GetLabelFor(bic); if (!string.IsNullOrWhiteSpace(n)) names.Add(n); }
                catch (Exception ex) { StingLog.Warn($"ACC_FederatedCompliance category label {bic}: {ex.Message}"); }
            }
            return names.Distinct(StringComparer.Ordinal).ToList();
        }

        private static string SafeTitle(Document doc)
        {
            try { return doc.Title; }
            catch (Exception ex) { StingLog.Warn("ACC_FederatedCompliance title: " + ex.Message); return "(unknown)"; }
        }
    }
}
