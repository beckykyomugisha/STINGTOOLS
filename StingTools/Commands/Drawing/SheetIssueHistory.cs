// StingTools — Drawing Template Manager · has this sheet been issued?
//
// WHY THIS FILE EXISTS
// --------------------
// Renumbering is safe right up until a sheet has been sent to somebody. After
// that, the number on the drawing in their hands no longer matches the number in
// the model, and nothing in Revit notices: view reference tags re-read the sheet
// number live and correct themselves, so the model stays perfectly consistent
// with itself while diverging from every PDF already issued.
//
// That was documented as a caveat for the operator to remember. A caveat is not a
// safeguard — it is the failure mode this codebase keeps producing, phrased as
// advice. The information needed to detect it is already in the project:
//
//   * PRJ_TB_LAST_TRANSMITTAL_TXT, stamped by Stamp TX when a sheet goes out
//   * Revit's own current revision on the sheet
//   * transmittals.json, the record of what was actually sent
//
// So the renumber commands ask, and say so before anything is written. They do
// NOT refuse: renumbering an issued sheet is sometimes exactly right (a set
// issued for comment, coming back for reorganisation before the real issue).
// Refusing would be a guess about the project; naming it is not.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Docs;

namespace StingTools.Commands.Drawing
{
    internal static class SheetIssueHistory
    {
        /// <summary>Why a sheet counts as issued, or null when it does not.
        /// Phrased for a report — the operator has to decide, so the reason has to
        /// be readable rather than a flag.</summary>
        internal sealed class Evidence
        {
            public string Reason;
            public string Detail;
        }

        /// <summary>Sheet numbers named in transmittals.json, read once per run.
        /// Returns an empty set when there is no file, which is the normal state of a
        /// project that has never issued anything — and is NOT the same as an error,
        /// so it is not reported as one.</summary>
        internal static HashSet<string> IssuedNumbers(Document doc)
        {
            var issued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                JArray txs = TransmittalStamper.LoadTransmittals(doc);
                if (txs == null) return issued;

                foreach (JToken tx in txs)
                {
                    // The store has grown several shapes over the years and a reader
                    // that knows only the current one silently finds nothing in an
                    // older project — which would read as "never issued".
                    foreach (string key in new[] { "sheets", "sheet_numbers", "documents", "items" })
                    {
                        if (!(tx[key] is JArray arr)) continue;
                        foreach (JToken row in arr)
                        {
                            string n = row.Type == JTokenType.String
                                ? row.ToString()
                                : (row["sheet_number"] ?? row["number"] ?? row["sheetNumber"])?.ToString();
                            if (!string.IsNullOrWhiteSpace(n)) issued.Add(n.Trim());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetIssueHistory: reading transmittals.json: {ex.Message}");
            }
            return issued;
        }

        /// <summary>Everything that says this sheet has gone out. Empty means no
        /// evidence was found, which is weaker than "it was never issued" — a set
        /// exported straight to PDF without a transmittal leaves no trace anywhere,
        /// and the report says so rather than implying safety.</summary>
        internal static List<Evidence> For(Document doc, ViewSheet sheet,
                                           Element titleBlock, HashSet<string> issuedNumbers)
        {
            var found = new List<Evidence>();
            if (sheet == null) return found;

            string tx = Read(titleBlock, sheet, ParamRegistry.TB_LAST_TRANSMITTAL);
            if (!string.IsNullOrWhiteSpace(tx))
                found.Add(new Evidence { Reason = "transmittal stamp", Detail = tx.Trim() });

            try
            {
                string rev = sheet.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION)?.AsString();
                if (!string.IsNullOrWhiteSpace(rev))
                    found.Add(new Evidence { Reason = "revision", Detail = rev.Trim() });
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetIssueHistory revision '{sheet.SheetNumber}': {ex.Message}");
            }

            if (issuedNumbers != null && issuedNumbers.Contains(sheet.SheetNumber ?? ""))
                found.Add(new Evidence { Reason = "named in a transmittal", Detail = sheet.SheetNumber });

            return found;
        }

        /// <summary>The warning block for a renumber preview, or "" when nothing in
        /// the plan has been issued.</summary>
        internal static string WarningFor(IEnumerable<KeyValuePair<string, List<Evidence>>> issued)
        {
            var rows = issued.Where(kv => kv.Value != null && kv.Value.Count > 0).ToList();
            if (rows.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{rows.Count} of these sheet(s) HAVE BEEN ISSUED:");
            foreach (var kv in rows.Take(12))
                sb.AppendLine($"    {kv.Key}  —  "
                    + string.Join(", ", kv.Value.Select(e => $"{e.Reason} {e.Detail}")));
            if (rows.Count > 12) sb.AppendLine($"    … and {rows.Count - 12} more");
            sb.AppendLine();
            sb.AppendLine("Renumbering these breaks the link between the drawings somebody already "
                + "holds and the ones in this model. View reference tags re-read the sheet number "
                + "and correct themselves, so the model stays consistent with ITSELF while "
                + "diverging from every PDF and transmittal already sent — nothing in Revit will "
                + "flag that later.");
            sb.AppendLine();
            sb.AppendLine("Old and new numbers are recorded in sheet_number_history.json either "
                + "way, so the trail survives. Proceed only if this set has not really gone out.");
            return sb.ToString();
        }

        private static string Read(Element titleBlock, ViewSheet sheet, string name)
        {
            try
            {
                string v = ParameterHelpers.GetString(titleBlock, name);
                if (!string.IsNullOrWhiteSpace(v)) return v;
                return ParameterHelpers.GetString(sheet, name);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetIssueHistory read '{name}': {ex.Message}");
                return null;
            }
        }
    }
}
