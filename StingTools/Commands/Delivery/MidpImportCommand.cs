// ══════════════════════════════════════════════════════════════════════════
//  MidpImportCommand.cs — bulk-import a MIDP/TIDP CSV into deliverables.json.
//
//  Command tag: Midp_Import
//
//  The gap this fills: until now the ONLY way a row reached deliverables.json
//  was the template engine's per-deliverable issuance flow, one at a time.
//  Midp_DriftReport reads a plan CSV but never writes — it parses transiently
//  to compare against the live lifecycle. So onboarding a project meant issuing
//  every deliverable by hand before the Deliverables tab showed anything.
//
//  This writes through DeliverableLifecycle.Persist rather than composing JSON
//  directly, so the schema-version gate, the dedup-by-key rule and the atomic
//  temp-file write all stay in one place.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using Planscape.Docs.Templates;
using StingTools.Core;
using StingTools.Core.Delivery;
using StingTools.UI;

namespace StingTools.Commands.Delivery
{
    /// <summary>
    /// Import a whole MIDP/TIDP spreadsheet into the deliverables register.
    ///
    /// <para><b>Add-only, by design.</b> A code already in the register is left
    /// completely untouched. <see cref="DeliverableLifecycle.Persist"/> replaces a
    /// matched row wholesale (<c>arr[idx] = row</c>), so importing over an
    /// existing project would silently wipe every Status, CDE state, IssuedDate
    /// and RevisionHistory the lifecycle had accumulated — turning a re-run of a
    /// harmless-looking import into data loss. Skipping instead makes the command
    /// idempotent and safe to run twice, which is what someone re-importing an
    /// updated programme will inevitably do.</para>
    ///
    /// <para>Updating existing rows from a revised programme is a genuinely
    /// different operation (it has to decide, per field, whether the spreadsheet
    /// or the lifecycle wins) and is deliberately not bundled in here.</para>
    ///
    /// <para><b>ReadOnly transaction:</b> this writes a JSON sidecar, never the
    /// Revit model — the same shape as Midp_DriftReport, which writes a CSV.</para>
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MidpImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pick a MIDP/TIDP CSV to import (Code,Title,Discipline,Milestone,PlannedDate,RequiredSuitability)",
                    Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                // Same parser the drift report uses — one header-matching
                // implementation, so the two commands cannot disagree about which
                // column is the planned date.
                var plan = MidpDriftReportCommand.ParseMidpCsv(dlg.FileName, out int skipped);
                if (plan.Count == 0)
                {
                    StingResultPanel.Create("MIDP import")
                        .AddSection("NO ROWS")
                        .Text("No deliverable rows parsed. Expected header columns: "
                            + "Code,Title,Discipline,Milestone,PlannedDate,RequiredSuitability.")
                        .Text(skipped > 0 ? $"{skipped} row(s) had an unparseable date." : "")
                        .Show();
                    return Result.Cancelled;
                }

                var existing = LoadExistingKeys(doc);

                int imported = 0, alreadyPresent = 0, failed = 0;
                var failures = new List<string>();

                foreach (var row in plan)
                {
                    if (existing.Contains(row.Code)) { alreadyPresent++; continue; }

                    if (DeliverableLifecycle.Persist(doc, ToDeliverable(row)))
                    {
                        imported++;
                        // Guard against a CSV that lists the same code twice: the
                        // second occurrence would otherwise re-Persist and count
                        // as a second import of one deliverable.
                        existing.Add(row.Code);
                    }
                    else
                    {
                        // Persist logs the reason; surface the codes so the user
                        // knows which rows to look at rather than just a count.
                        failed++;
                        if (failures.Count < 10) failures.Add(row.Code);
                    }
                }

                var panel = StingResultPanel.Create("MIDP import")
                    .SetSubtitle($"{Path.GetFileName(dlg.FileName)} → deliverables register")
                    .AddSection("RESULT")
                    .Metric("Imported", imported.ToString())
                    .Metric("Already present", alreadyPresent.ToString())
                    .Metric("Skipped (bad date)", skipped.ToString())
                    .Metric("Failed", failed.ToString());

                if (alreadyPresent > 0)
                    panel.Text($"{alreadyPresent} row(s) already in the register were left untouched — "
                             + "import never overwrites lifecycle state (status, CDE, revision history).");
                if (skipped > 0)
                    panel.Text($"{skipped} row(s) skipped: no parseable date in the planned-date column.");
                if (failures.Count > 0)
                    panel.Text("Failed to save: " + string.Join(", ", failures)
                             + (failed > failures.Count ? $" (+{failed - failures.Count} more)" : "")
                             + " — see StingTools.log.");
                if (imported > 0)
                    panel.Text("Open the BIM Coordination Center → DELIVERABLES to see them.");

                panel.Show();
                StingLog.Info($"Midp_Import: {imported} imported, {alreadyPresent} already present, "
                            + $"{skipped} skipped, {failed} failed (from {Path.GetFileName(dlg.FileName)})");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Midp_Import", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Shape a plan row for <see cref="DeliverableLifecycle.Persist"/>, which
        /// serialises whatever properties this object carries straight into the
        /// file (<c>JObject.FromObject</c>).
        ///
        /// <para>Every field name here is one <c>BuildCoordData</c>'s reader
        /// already looks for, so an imported row renders on the FIRST read of the
        /// Deliverables tab rather than only after some later edit happens to
        /// normalise it.</para>
        ///
        /// <para><b>DocNumber AND Code are both set, deliberately.</b>
        /// <c>DeliverableLifecycle.DeliverableKey</c> reads
        /// <c>(string)d.DocNumber, (string)d.Code</c> — both arguments are
        /// evaluated, so on an anonymous type missing either one the dynamic
        /// binder throws, its enclosing catch returns "", and Persist then
        /// refuses the row as unkeyed. The failure is silent apart from a log
        /// line, and it would have made every single import a no-op.</para>
        /// </summary>
        private static object ToDeliverable(DeliverablePlanItem row) => new
        {
            // Identity — both spellings, see the remarks above.
            DocNumber = row.Code,
            Code = row.Code,

            Title = row.Title ?? "",
            Discipline = row.Discipline ?? "",

            // The CSV's Milestone column is the register's DataDrop.
            DataDrop = row.Milestone ?? "",

            // Planned, not yet issued: the required suitability IS the current
            // suitability until something is actually produced against it.
            Suitability = row.RequiredSuitability ?? "",
            RequiredSuitability = row.RequiredSuitability ?? "",

            // yyyy-MM-dd so BuildCoordData's DateTime.TryParse resolves it
            // culture-independently and its IsOverdue comparison is meaningful.
            DueDate = row.PlannedDate.ToString("yyyy-MM-dd"),
            PlannedDate = row.PlannedDate.ToString("yyyy-MM-dd"),

            // Planned work nobody has started. Drives the Deliverables tab's
            // PENDING KPI card.
            Status = "Pending",

            // WIP is the CDE state of something planned but not yet shared — the
            // starting point of the lifecycle's own state machine, and what BCC's
            // "add row" button already defaults a new register row to.
            CDE = "WIP",

            // Provenance, so a later reader can tell an imported plan row from one
            // a person created through the issuance flow.
            Source = "midp_import",
            ImportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        /// <summary>
        /// Codes already in the register. Read through the same resolver and the
        /// same key rule the rest of the codebase uses, so "already present"
        /// cannot mean something different here than it does to Persist's own
        /// dedup or to the Deliverables tab's reader.
        ///
        /// An absent file is the normal first-import case, not an error.
        /// </summary>
        private static HashSet<string> LoadExistingKeys(Document doc)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = MidpDriftReportCommand.ResolveDeliverablesPath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return keys;

                foreach (var o in JArray.Parse(File.ReadAllText(path)).OfType<JObject>())
                {
                    string key = DocumentIdentity.FirstNonBlank(o, DocumentIdentity.DeliverableKeys);
                    if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
                }
            }
            catch (Exception ex)
            {
                // Returning an empty set here does NOT open a path to silently
                // overwriting anything: Persist parses the very same file before
                // it writes, so a register this cannot read is one Persist cannot
                // read either — every row then fails its save, lands in the
                // "Failed" count, and the existing file is left alone.
                StingLog.Warn($"Midp_Import: could not read the existing register ({ex.Message}); "
                            + "rows will be attempted individually and will fail if the file is unreadable.");
            }
            return keys;
        }
    }
}
