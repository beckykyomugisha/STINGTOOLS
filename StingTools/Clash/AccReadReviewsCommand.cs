// AccReadReviewsCommand.cs — ACC Reviews as the approval authority, read back into STING.
//
//   ACC_ReadReviews       read ACC's approval decisions on the files STING put in ACC and
//                         queue them as PROPOSALS (acc_review_proposals.json). Changes no
//                         STING record.
//   ACC_ReviewProposals   a person accepts or dismisses the queued proposals. Accept applies
//                         through the EXISTING lifecycle: DeliverableLifecycle (deliverables.json),
//                         TransmittalRecord (transmittals.json), and the register's own
//                         suitability-history writer (document_register.json). Dismiss records
//                         the decision so the same proposal never comes back.
//   ACC_ReadTransmittals  copy ACC transmittals into transmittals.json as READ-ONLY rows
//                         (source "acc"); STING's own rows are never touched.
//   ACC_StartReview       start an ACC review (the project's configured workflow) on a file
//                         version STING knows is in ACC. Also offered after Publish Deliverable.
//
// Which files: every version URN the ACC upload recorded on a transmittal row
// (acc_version_urn / acc_cover_version_urn), plus the files directly in each configured
// cdeFolders folder (tip version). The Revit-free rules (mapping, dedupe, which STING record a
// file belongs to) live in V6/AccReviewProposals.cs and are tested there.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;
using StingTools.V6;

namespace StingTools.Core.Clash
{
    /// <summary>Paths and loaders shared by the four commands - one home, so the reader and
    /// the writers cannot disagree about which file they mean.</summary>
    internal static class AccReviewFiles
    {
        internal static string QueuePath(Document doc)
        {
            try
            {
                string dir = StingPaths.MetaFile(doc, "_BIM_COORD", "acc");
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, AccReviewQueue.FileName);
            }
            catch (Exception ex) { StingLog.Warn("ACC review queue path: " + ex.Message); return null; }
        }

        internal static string TransmittalsPath(Document doc) => BIMManagerEngine.GetBIMManagerFilePath(doc, "transmittals.json");
        internal static string RegisterPath(Document doc) => BIMManagerEngine.GetBIMManagerFilePath(doc, "document_register.json");
        internal static string DeliverablesPath(Document doc)
        {
            // The directory DeliverableLifecycle.Persist writes to, so the matcher reads the
            // file the accept path will write.
            try { return Path.Combine(StingPaths.Meta(doc, "_BIM_COORD"), "deliverables.json"); }
            catch (Exception ex) { StingLog.Warn("deliverables path: " + ex.Message); return null; }
        }

        /// <summary>A JSON array file, or an empty array when absent. A file that exists and
        /// will not parse returns null with <paramref name="error"/> - never an empty array a
        /// caller could save over it.</summary>
        internal static JArray ReadArray(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new JArray();
            try
            {
                string text = File.ReadAllText(path);
                return string.IsNullOrWhiteSpace(text) ? new JArray() : JArray.Parse(text);
            }
            catch (Exception ex) { error = $"{Path.GetFileName(path)} could not be read ({ex.Message})"; return null; }
        }

        internal static bool Configured(AccCredentials creds) =>
            !string.IsNullOrEmpty(creds?.ClientId) && !string.IsNullOrEmpty(creds.RefreshToken) &&
            !string.IsNullOrEmpty(creds.ProjectId);

        internal static string User(Document doc)
        {
            try { return doc?.Application?.Username ?? Environment.UserName; }
            catch (Exception ex) { StingLog.Warn("ACC reviews user: " + ex.Message); return Environment.UserName; }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  ACC_ReadReviews
    // ═════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccReadReviewsCommand : IExternalCommand
    {
        private const string Title = "ACC — Read Review Decisions";
        /// <summary>One approval-status call per version; a guard, reported when hit.</summary>
        internal const int MaxVersions = 400;

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_ReadReviews");
            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC read reviews");
            if (!AccReviewFiles.Configured(creds))
            {
                AccPullClashesCommand.Report(policy, Title,
                    "ACC is not set up for this project: " + AccProjectScope.Describe(creds) + ". Nothing was read.");
                return Result.Cancelled;
            }

            string queuePath = AccReviewFiles.QueuePath(doc);
            if (string.IsNullOrEmpty(queuePath))
            {
                AccPullClashesCommand.Report(policy, Title, "This model has not been saved, so there is no project folder for the proposal queue.");
                return Result.Cancelled;
            }
            var queue = AccReviewQueue.Load(queuePath, out string qErr);
            if (queue == null) { AccPullClashesCommand.Report(policy, Title, "Nothing was read: " + qErr); return Result.Failed; }

            var txRows = AccReviewFiles.ReadArray(AccReviewFiles.TransmittalsPath(doc), out string txErr);
            var delRows = AccReviewFiles.ReadArray(AccReviewFiles.DeliverablesPath(doc), out string delErr);
            var regRows = AccReviewFiles.ReadArray(AccReviewFiles.RegisterPath(doc), out string regErr);
            var warnings = new List<string>();
            foreach (var e in new[] { txErr, delErr, regErr }.Where(e => e != null))
                warnings.Add(e + " — its records cannot be matched this run");

            // ── 1. Which versions ──────────────────────────────────────────
            var candidates = new List<AccKnownFile>();
            var seenVersions = new HashSet<string>(StringComparer.Ordinal);
            string now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            void Add(string fileName, string item, string version, string source)
            {
                if (string.IsNullOrWhiteSpace(version) || !seenVersions.Add(version)) return;
                candidates.Add(new AccKnownFile { FileName = fileName ?? "", ItemUrn = item ?? "", VersionUrn = version, Source = source, SeenAt = now });
            }
            foreach (var row in (txRows ?? new JArray()).OfType<JObject>())
            {
                string id = TransmittalRecord.Id(row);
                Add((string)row["acc_file_name"] ?? id, (string)row["acc_item_urn"], (string)row["acc_version_urn"], "transmittal " + id);
                Add(id + " cover", null, (string)row["acc_cover_version_urn"], "transmittal cover " + id);
            }
            var folderFailures = new List<string>();
            var folders = policy.CdeFolders.Select(kv => (state: kv.Key, urn: kv.Value)).ToList();
            foreach (var (state, urn) in folders)
            {
                var files = AccReviews.ListFolderFilesAsync(creds, creds.ProjectId, urn).GetAwaiter().GetResult();
                if (!files.Succeeded) { folderFailures.Add($"{state}: {files.Detail}"); continue; }
                foreach (var f in files.Value) Add(f.FileName, f.ItemUrn, f.TipVersionUrn, "cdeFolders." + state);
            }
            if (folders.Count == 0)
                warnings.Add("no cdeFolders are configured, so only versions recorded on transmittals were read");

            bool truncated = candidates.Count > MaxVersions;
            var toRead = candidates.Take(MaxVersions).ToList();
            if (toRead.Count == 0)
            {
                string why = "STING knows of no file in ACC for this project: no transmittal records an ACC version, and " +
                             (folders.Count == 0 ? "no cdeFolders are configured." : "the configured folders held no files" +
                              (folderFailures.Count > 0 ? " that could be read:\n  " + string.Join("\n  ", folderFailures) : "."));
                AccPullClashesCommand.Report(policy, Title, why + "\nThe proposal queue was not changed.");
                return folderFailures.Count > 0 && folderFailures.Count == folders.Count ? Result.Failed : Result.Succeeded;
            }

            // ── 2. Read each version's approval records ────────────────────
            var incoming = new List<AccReviewProposal>();
            var readFailures = new List<string>();
            var notFinal = new Dictionary<string, int>(StringComparer.Ordinal);
            var comments = new Dictionary<string, string>(StringComparer.Ordinal);
            int readOk = 0;
            var dKeys = AccReviewProposals.DeliverableKeys(delRows);
            var rKeys = AccReviewProposals.RegisterKeys(regRows);
            foreach (var f in toRead)
            {
                var recs = AccReviews.GetApprovalStatusesAsync(creds, creds.ProjectId, f.VersionUrn).GetAwaiter().GetResult();
                if (!recs.Succeeded)
                {
                    readFailures.Add($"{f.FileName}: {recs.Detail}");
                    // An auth failure on one version is an auth failure on all of them.
                    if (recs.Status == AccFetchStatus.AuthFailed) break;
                    continue;
                }
                readOk++;
                foreach (var rec in recs.Value)
                {
                    string comment = "";
                    if (rec.Value == "REJECTED" && rec.ReviewStatus == "CLOSED" && !string.IsNullOrEmpty(rec.ReviewId))
                    {
                        if (!comments.TryGetValue(rec.ReviewId, out comment))
                        {
                            var prog = AccReviews.GetReviewProgressAsync(creds, creds.ProjectId, rec.ReviewId).GetAwaiter().GetResult();
                            comment = prog.Succeeded ? AccReviews.ReviewerComment(prog.Value)
                                                     : "(reviewer comment could not be read: " + prog.Detail + ")";
                            comments[rec.ReviewId] = comment;
                        }
                    }
                    var p = AccReviewProposals.FromApproval(rec, f.VersionUrn, f.FileName, policy.ReviewApprovalMap,
                        comment, DateTime.Now, out string whyNone);
                    if (p == null) { notFinal[whyNone] = notFinal.TryGetValue(whyNone, out var n) ? n + 1 : 1; continue; }
                    p.ItemUrn = f.ItemUrn;
                    p.TransmittalId = AccReviewProposals.FindTransmittal(txRows, f.VersionUrn, f.ItemUrn);
                    p.DeliverableKey = AccReviewProposals.MatchDocumentKey(f.FileName, dKeys);
                    p.RegisterDocId = AccReviewProposals.MatchDocumentKey(f.FileName, rKeys);
                    incoming.Add(p);
                }
            }

            if (readOk == 0)
            {
                AccPullClashesCommand.Report(policy, Title,
                    "No approval status could be read, so the proposal queue was not changed:\n  " +
                    string.Join("\n  ", readFailures.Take(5)));
                StingLog.Warn("ACC_ReadReviews: every read failed: " + string.Join(" | ", readFailures));
                return Result.Failed;
            }

            // ── 3. Merge + save (additive: a failed read proposes nothing, removes nothing) ──
            var merge = AccReviewProposals.Merge(queue, incoming, DateTime.Now);
            queue.RememberFiles(toRead.Where(f => !f.Source.StartsWith("transmittal cover", StringComparison.Ordinal)));
            try { queue.Save(queuePath); }
            catch (Exception ex)
            {
                AccPullClashesCommand.Report(policy, Title, "The proposal queue could not be saved: " + ex.Message);
                StingLog.Error("ACC_ReadReviews save", ex);
                return Result.Failed;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Versions read:        {readOk} of {toRead.Count}" + (truncated ? $"  (capped at {MaxVersions} of {candidates.Count})" : ""));
            sb.AppendLine($"New proposals:        {merge.Added}");
            if (merge.Updated > 0) sb.AppendLine($"Updated proposals:    {merge.Updated}  (ACC's answer changed while still pending)");
            sb.AppendLine($"Already queued:       {merge.Unchanged}");
            if (merge.AlreadyDecided > 0) sb.AppendLine($"Already decided:      {merge.AlreadyDecided}  (not re-proposed)");
            foreach (var kv in notFinal) sb.AppendLine($"Not final:            {kv.Value} — {kv.Key}");
            sb.AppendLine($"Waiting for a person: {queue.Pending.Count()}  → ACC_ReviewProposals");
            int noTarget = queue.Pending.Count(p => !p.HasTarget);
            if (noTarget > 0) sb.AppendLine($"  of which {noTarget} match no STING deliverable, transmittal or register row");
            if (readFailures.Count > 0 || folderFailures.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"NOT READ ({readFailures.Count + folderFailures.Count}):");
                foreach (var f in folderFailures.Concat(readFailures).Take(8)) sb.AppendLine("  " + f);
            }
            foreach (var w in warnings) { sb.AppendLine(); sb.AppendLine("Note: " + w); }
            sb.AppendLine();
            sb.AppendLine("Nothing in STING was changed. Queue: " + queuePath);
            AccPullClashesCommand.Report(policy, Title, sb.ToString());
            StingLog.Info($"ACC_ReadReviews: read={readOk}/{toRead.Count} added={merge.Added} updated={merge.Updated} " +
                          $"unchanged={merge.Unchanged} decided={merge.AlreadyDecided} failures={readFailures.Count + folderFailures.Count}");
            return readFailures.Count + folderFailures.Count > 0 ? Result.Failed : Result.Succeeded;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  ACC_ReviewProposals — accept / dismiss
    // ═════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccReviewProposalsCommand : IExternalCommand
    {
        private const string Title = "ACC — Review Decisions";

        /// <summary>The codes a person may choose for an approval with no mapped code.</summary>
        private static readonly string[] ApprovalCodes =
            { "A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3", "B4", "B5", "CR", "S4", "S5", "S6", "S7" };

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_ReviewProposals");

            string queuePath = AccReviewFiles.QueuePath(doc);
            var queue = AccReviewQueue.Load(queuePath, out string qErr);
            if (queue == null) { AccPullClashesCommand.Report(policy, Title, qErr); return Result.Failed; }
            var pending = queue.Pending.ToList();
            if (pending.Count == 0)
            {
                AccPullClashesCommand.Report(policy, Title, "No ACC review decision is waiting. Run ACC_ReadReviews to read ACC.");
                return Result.Succeeded;
            }
            // Accepting changes a contractual record: it is a person's act, never a scheduled one.
            if (!policy.MayPrompt)
            {
                StingLog.Info($"{Title}: {pending.Count} proposal(s) pending; the project runs unattended, so none was decided.");
                return Result.Succeeded;
            }

            var items = pending.Select(p => new StingListPicker.ListItem
            {
                Label = p.Describe(),
                Detail = Targets(p) + (string.IsNullOrWhiteSpace(p.Comment) ? "" : "  · comment: " + p.Comment) +
                         "  · " + p.MappingReason,
                Tag = p,
                IsInvalid = !p.HasTarget,
            }).ToList();
            var picked = StingListPicker.Show(Title,
                "ACC decided these. Nothing changes in STING until you accept. Red rows match no STING record.",
                items, allowMultiSelect: true);
            if (picked == null || picked.Count == 0) return Result.Cancelled;
            var chosen = picked.Select(i => i.Tag as AccReviewProposal).Where(p => p != null).ToList();

            var td = new TaskDialog(Title)
            {
                MainInstruction = $"{chosen.Count} ACC decision(s) selected",
                MainContent = "Accept applies each decision to its STING records through the deliverable, transmittal " +
                              "and register lifecycle. Dismiss records that you chose not to apply it; it will not be proposed again.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Accept");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Dismiss");
            var choice = td.Show();
            if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2) return Result.Cancelled;

            string user = AccReviewFiles.User(doc);
            var report = new StringBuilder();
            int accepted = 0, dismissed = 0, refused = 0;
            foreach (var p in chosen)
            {
                if (choice == TaskDialogResult.CommandLink2)
                {
                    if (AccReviewProposals.Decide(p, AccProposalState.Dismissed, user, DateTime.Now, "", "dismissed in STING"))
                    { dismissed++; report.AppendLine("Dismissed: " + p.Describe()); }
                    continue;
                }
                string outcome = Apply(doc, p, user, out string applied, out bool ok);
                if (ok)
                {
                    AccReviewProposals.Decide(p, AccProposalState.Accepted, user, DateTime.Now, applied, outcome);
                    accepted++;
                    report.AppendLine("Accepted: " + p.Describe() + "\n    " + outcome);
                }
                else { refused++; report.AppendLine("NOT applied (still pending): " + p.Describe() + "\n    " + outcome); }
            }

            try { queue.Save(queuePath); }
            catch (Exception ex)
            {
                StingLog.Error("ACC_ReviewProposals save", ex);
                report.AppendLine().AppendLine("WARNING: the decisions were applied but the queue could not be saved (" + ex.Message +
                                               ") — they will be proposed again.");
            }
            try { BIMCoordinationCenterCommand.RefreshBccIfOpen(doc); }
            catch (Exception ex) { StingLog.Warn("ACC_ReviewProposals BCC refresh: " + ex.Message); }

            new TaskDialog(Title)
            {
                MainInstruction = $"{accepted} accepted, {dismissed} dismissed" + (refused > 0 ? $", {refused} not applied" : ""),
                MainContent = report.ToString(),
            }.Show();
            StingLog.Info($"ACC_ReviewProposals: accepted={accepted} dismissed={dismissed} refused={refused}");
            return refused > 0 ? Result.Failed : Result.Succeeded;
        }

        private static string Targets(AccReviewProposal p)
        {
            var t = new List<string>();
            if (!string.IsNullOrEmpty(p.DeliverableKey)) t.Add("deliverable " + p.DeliverableKey);
            if (!string.IsNullOrEmpty(p.TransmittalId)) t.Add("transmittal " + p.TransmittalId);
            if (!string.IsNullOrEmpty(p.RegisterDocId)) t.Add("register " + p.RegisterDocId);
            return t.Count == 0 ? "no STING record" : string.Join(", ", t);
        }

        /// <summary>Apply one accepted proposal. ok=false leaves it pending (nothing to apply
        /// to, no code chosen, or every target refused).</summary>
        private static string Apply(Document doc, AccReviewProposal p, string user, out string appliedCode, out bool ok)
        {
            appliedCode = "";
            ok = false;
            if (!p.HasTarget)
                return "it matches no STING deliverable, transmittal or register row - dismiss it, or record the document in STING first";

            bool approve = p.Kind == AccProposalKind.Approve;
            string code = p.ProposedSuitability;
            if (approve && string.IsNullOrEmpty(code))
            {
                var pick = StingListPicker.Show(Title + " — choose the code",
                    $"ACC approved {p.FileName} ('{p.ApprovalLabel}'). Which ISO 19650 suitability does that grant?",
                    ApprovalCodes.Select(c => $"{c} — {Core.Drawing.Iso19650Suitability.DescriptionFor(c)}").ToList());
                code = Core.Drawing.Iso19650Suitability.ExtractCode(pick);
                if (string.IsNullOrEmpty(code)) return "no suitability code was chosen";
            }
            appliedCode = approve ? code : "";
            string reason = approve
                ? $"ACC review #{p.ReviewSequenceId} approved ('{p.ApprovalLabel}') → {code}"
                : $"ACC review #{p.ReviewSequenceId} rejected" + (string.IsNullOrWhiteSpace(p.Comment) ? "" : ": " + p.Comment);

            var done = new List<string>();
            var failed = new List<string>();

            // Deliverable — the lifecycle state machine (audit, workflow gate, persistence).
            if (!string.IsNullOrEmpty(p.DeliverableKey))
            {
                try
                {
                    var rows = AccReviewFiles.ReadArray(AccReviewFiles.DeliverablesPath(doc), out string err);
                    var row = rows?.OfType<JObject>().FirstOrDefault(o => string.Equals(
                        DocumentIdentity.FirstNonBlank(o, DocumentIdentity.DeliverableKeys), p.DeliverableKey, StringComparison.OrdinalIgnoreCase));
                    if (row == null) failed.Add($"deliverable {p.DeliverableKey}: {err ?? "no longer in deliverables.json"}");
                    else
                    {
                        dynamic d = row.ToObject<BIMCoordinationCenter.DeliverableRow>();
                        var engine = new Planscape.Docs.Templates.TemplateEngine(doc);
                        var lr = approve
                            ? Planscape.Docs.Templates.DeliverableLifecycle.ApproveFromReview(d, doc, engine.Registry.Manifest, user, code, reason)
                            : Planscape.Docs.Templates.DeliverableLifecycle.RejectFromReview(d, doc, engine.Registry.Manifest, user, reason);
                        if (lr != null && lr.Ok) done.Add($"deliverable {p.DeliverableKey} → {lr.Message}" + (approve ? $" {code}" : ""));
                        else failed.Add($"deliverable {p.DeliverableKey}: {lr?.Message ?? "no result"}");
                    }
                }
                catch (Exception ex) { StingLog.Error("ACC proposal → deliverable", ex); failed.Add($"deliverable {p.DeliverableKey}: {ex.Message}"); }
            }

            // Transmittal — TransmittalRecord owns the transition and its history shape.
            if (!string.IsNullOrEmpty(p.TransmittalId))
            {
                try
                {
                    string path = AccReviewFiles.TransmittalsPath(doc);
                    var rows = AccReviewFiles.ReadArray(path, out string err);
                    if (rows == null) failed.Add($"transmittal {p.TransmittalId}: {err}");
                    else
                    {
                        var row = TransmittalRecord.RecordReviewDecision(rows, p.TransmittalId, approve, code, DateTime.Now, user, reason, out string why);
                        if (row == null) failed.Add($"transmittal {p.TransmittalId}: {why}");
                        else { BIMManagerEngine.SaveJsonFile(path, rows); done.Add($"transmittal {p.TransmittalId} → {row["status"]}"); }
                    }
                }
                catch (Exception ex) { StingLog.Error("ACC proposal → transmittal", ex); failed.Add($"transmittal {p.TransmittalId}: {ex.Message}"); }
            }

            // Register — its own suitability-history writer. A rejection changes no code.
            if (!string.IsNullOrEmpty(p.RegisterDocId) && approve)
            {
                try
                {
                    BIMManagerEngine.UpdateDocumentSuitability(doc, p.RegisterDocId, code, reason);
                    done.Add($"register {p.RegisterDocId} → {code}");
                }
                catch (Exception ex) { StingLog.Error("ACC proposal → register", ex); failed.Add($"register {p.RegisterDocId}: {ex.Message}"); }
            }

            ok = done.Count > 0;
            string text = string.Join("; ", done);
            if (failed.Count > 0) text += (text.Length > 0 ? "; " : "") + "NOT applied: " + string.Join("; ", failed);
            return text;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  ACC_ReadTransmittals
    // ═════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccReadTransmittalsCommand : IExternalCommand
    {
        private const string Title = "ACC — Read Transmittals";

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_ReadTransmittals");
            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC read transmittals");
            if (!AccReviewFiles.Configured(creds))
            {
                AccPullClashesCommand.Report(policy, Title, "ACC is not set up for this project: " + AccProjectScope.Describe(creds) + ".");
                return Result.Cancelled;
            }

            var pull = AccReviews.ListTransmittalsAsync(creds, creds.ProjectId, withDocuments: true).GetAwaiter().GetResult();
            if (!pull.Succeeded)
            {
                AccPullClashesCommand.Report(policy, Title,
                    AccCommandOutcome.FailureMessage("the ACC transmittal list", pull.Status, pull.HttpStatus, pull.Detail, creds.ProjectId) +
                    "\ntransmittals.json was left untouched.");
                return Result.Failed;
            }

            string path = AccReviewFiles.TransmittalsPath(doc);
            var rows = AccReviewFiles.ReadArray(path, out string err);
            if (rows == null)
            {
                AccPullClashesCommand.Report(policy, Title, err + " — it was NOT overwritten, nothing was imported.");
                return Result.Failed;
            }
            var (added, updated, unchanged) = AccReviewProposals.MergeTransmittals(rows, pull.Value, DateTime.Now);
            if (added + updated > 0) BIMManagerEngine.SaveJsonFile(path, rows);

            AccPullClashesCommand.Report(policy, Title,
                $"ACC transmittals read: {pull.Value.Count}" + (pull.Status == AccFetchStatus.EmptyOk ? " (the project has none)" : "") + "\n" +
                $"New in STING:  {added}\nUpdated:       {updated}\nUnchanged:     {unchanged}\n\n" +
                "They are recorded as read-only rows (source \"acc\"); STING's own transmittals were not touched. " +
                "Autodesk's API cannot create ACC transmittals, so this is one-way.");
            StingLog.Info($"ACC_ReadTransmittals: read={pull.Value.Count} added={added} updated={updated} unchanged={unchanged}");
            return Result.Succeeded;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  ACC_StartReview + the Publish Deliverable offer
    // ═════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccStartReviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var policy = AccProjectSettingsFile.LoadFor(doc, "ACC_StartReview");
            if (!policy.MayPrompt)
            {
                StingLog.Info("ACC_StartReview: unattended project — a review is only started from Publish Deliverable.");
                return Result.Cancelled;
            }
            var queue = AccReviewQueue.Load(AccReviewFiles.QueuePath(doc), out string qErr);
            if (queue == null) { TaskDialog.Show(AccReviewStarter.Title, qErr); return Result.Failed; }
            if (queue.Files.Count == 0)
            {
                TaskDialog.Show(AccReviewStarter.Title, "STING has not seen any file in ACC yet. Run ACC_ReadReviews first.");
                return Result.Cancelled;
            }
            var pick = StingListPicker.Show(AccReviewStarter.Title, "Start an ACC review on which file version?",
                queue.Files.Select(f => new StingListPicker.ListItem { Label = f.FileName, Detail = f.VersionUrn, Tag = f }).ToList());
            var file = pick?.FirstOrDefault()?.Tag as AccKnownFile;
            if (file == null) return Result.Cancelled;
            return AccReviewStarter.Start(doc, policy, queue, file, "", interactive: true) ? Result.Succeeded : Result.Failed;
        }
    }

    internal static class AccReviewStarter
    {
        internal const string Title = "ACC — Start Review";

        /// <summary>
        /// After Publish Deliverable succeeds: if the project names a review workflow
        /// (startAccReviewOnPublish) and STING has seen the deliverable's file in ACC, offer
        /// to start an ACC review on that version (or start it, when the project runs
        /// unattended AND opted in with "unattended": true). Never throws; never blocks the
        /// publish that already happened.
        /// </summary>
        internal static void OfferAfterPublish(Document doc, string deliverableKey)
        {
            try
            {
                if (doc == null || string.IsNullOrWhiteSpace(deliverableKey)) return;
                var policy = AccProjectSettingsFile.LoadFor(doc, "Publish → ACC review");
                if (string.IsNullOrEmpty(policy.ReviewWorkflowId)) return;
                var queue = AccReviewQueue.Load(AccReviewFiles.QueuePath(doc), out string qErr);
                if (queue == null) { StingLog.Warn("Publish → ACC review: " + qErr); return; }

                var file = queue.Files.FirstOrDefault(f =>
                    !string.IsNullOrEmpty(AccReviewProposals.MatchDocumentKey(f.FileName, new[] { deliverableKey })));
                if (file == null)
                {
                    StingLog.Info($"Publish → ACC review: no file for '{deliverableKey}' has been seen in ACC (run ACC_ReadReviews after uploading) — no review started.");
                    if (policy.MayPrompt)
                        TaskDialog.Show(Title, $"'{deliverableKey}' was published in STING, but STING has not seen its file in ACC, " +
                                               "so no ACC review was started. Upload it, run ACC_ReadReviews, then ACC_StartReview.");
                    return;
                }
                if (queue.StartedReviews.Any(s => string.Equals(s.VersionUrn, file.VersionUrn, StringComparison.Ordinal)))
                {
                    StingLog.Info($"Publish → ACC review: a review was already started on {file.VersionUrn}.");
                    return;
                }
                if (policy.MayPrompt)
                {
                    var td = new TaskDialog(Title)
                    {
                        MainInstruction = "Start an ACC review?",
                        MainContent = $"ACC Reviews is this project's approval authority.\n\nFile: {file.FileName}\n" +
                                      $"Version: {file.VersionUrn}\nWorkflow: {policy.ReviewWorkflowId}\n\n" +
                                      "The workflow's reviewers are notified by ACC.",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No,
                    };
                    if (td.Show() != TaskDialogResult.Yes) return;
                    Start(doc, policy, queue, file, deliverableKey, interactive: true);
                }
                else if (policy.ReviewStartUnattended) Start(doc, policy, queue, file, deliverableKey, interactive: false);
                else StingLog.Info("Publish → ACC review: unattended project without startAccReviewOnPublish.unattended — not started.");
            }
            catch (Exception ex) { StingLog.Warn("Publish → ACC review: " + ex.Message); }
        }

        internal static bool Start(Document doc, AccOperatingPolicy policy, AccReviewQueue queue, AccKnownFile file,
            string deliverableKey, bool interactive)
        {
            if (string.IsNullOrEmpty(policy.ReviewWorkflowId))
            {
                Say(interactive, "This project names no ACC approval workflow. Add \"startAccReviewOnPublish\": {\"workflowId\": \"…\"} " +
                                 "to its ACC settings (" + policy.SettingsPath + ").");
                return false;
            }
            var creds = AccProjectSettingsFile.LoadCredentials(doc, "ACC start review");
            if (!AccReviewFiles.Configured(creds)) { Say(interactive, "ACC is not set up: " + AccProjectScope.Describe(creds)); return false; }

            // The queue remembers the version STING last SAW, which may be the previous revision
            // when a new one was uploaded since (ACC_UploadModel, the Export Centre, or a person
            // in ACC). ACC is the one source of truth for "current": re-read the item's tip and
            // start the review on that. If the tip cannot be read, refuse rather than start a
            // review - which notifies real reviewers - on a possibly superseded version.
            var tip = AccReviews.CurrentVersionAsync(creds, creds.ProjectId, file.ItemUrn, file.VersionUrn)
                                .GetAwaiter().GetResult();
            if (!tip.Succeeded)
            {
                Say(interactive, "The ACC review was NOT started: STING could not confirm which version of " +
                                 file.FileName + " is current in ACC (" + tip.Detail + ")." +
                                 (tip.Status == AccFetchStatus.AuthFailed || tip.Status == AccFetchStatus.NotFound
                                     ? "\n" + AccCommandOutcome.Remedy(tip.Status) : ""));
                return false;
            }
            string version = tip.Value;
            if (!string.Equals(version, file.VersionUrn, StringComparison.Ordinal))
            {
                StingLog.Info($"ACC start review: {file.FileName} moved on in ACC since STING saw it " +
                              $"({file.VersionUrn} → {version}); the review is started on the current version.");
                if (queue.StartedReviews.Any(s => string.Equals(s.VersionUrn, version, StringComparison.Ordinal)))
                {
                    Say(interactive, $"A review was already started on the current version of {file.FileName} ({version}).");
                    return false;
                }
                file.VersionUrn = version;
                if (string.IsNullOrEmpty(file.ItemUrn)) file.ItemUrn = AccReviews.ItemUrnForVersion(version);
            }

            string name = $"STING — {file.FileName}";
            var r = AccReviews.StartReviewAsync(creds, creds.ProjectId, policy.ReviewWorkflowId, name, new[] { version })
                              .GetAwaiter().GetResult();
            if (!r.Ok)
            {
                Say(interactive, "The ACC review was NOT started: " + r.Detail +
                                 (r.Status == AccFetchStatus.AuthFailed || r.Status == AccFetchStatus.NotFound
                                     ? "\n" + AccCommandOutcome.Remedy(r.Status) : ""));
                return false;
            }
            queue.StartedReviews.Add(new AccStartedReview
            {
                VersionUrn = file.VersionUrn, ReviewId = r.ReviewId, WorkflowId = policy.ReviewWorkflowId,
                DeliverableKey = deliverableKey ?? "", StartedBy = AccReviewFiles.User(doc),
                StartedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            });
            bool queueSaved = true;
            string queueErr = "";
            try { queue.Save(AccReviewFiles.QueuePath(doc)); }
            catch (Exception ex)
            {
                // The review exists in ACC either way; what failed is STING's record of it, and
                // without that a later Start could start a second review on the same version.
                StingLog.Warn("ACC start review: queue save: " + ex.Message);
                queueSaved = false;
                queueErr = ex.Message;
            }
            string queueNote = queueSaved ? "" :
                "\nSTING could NOT record this review locally (" + queueErr + "). Check ACC before " +
                "starting another review on this file - STING will not know this one exists.";
            Say(interactive, $"ACC review started on {file.FileName}" +
                             (string.IsNullOrEmpty(r.SequenceId) ? "" : $" (review #{r.SequenceId})") + ". " +
                             "Its decision comes back through ACC_ReadReviews as a proposal." +
                             (string.IsNullOrEmpty(r.Detail) ? "" : "\n" + r.Detail) + queueNote);
            return true;
        }

        private static void Say(bool interactive, string text)
        {
            if (interactive) TaskDialog.Show(Title, text);
            StingLog.Info("ACC start review: " + text);
        }
    }
}
