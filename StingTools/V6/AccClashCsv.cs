// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccClashCsv.cs
//
// The rows of STING_ACC_Clashes_<set>.csv (ACC_PullClashes). A14: numbers are written with
// the INVARIANT culture. On a machine set to a comma-decimal culture (de-DE, fr-FR, and many
// others), "0.875" became "0,875" and split a comma-separated row into an extra column, so
// every field after the score shifted one place and a spreadsheet or a re-import read the
// wrong values without any error.
//
// Revit-free and log-free: linked into StingTools.Acc.Tests.

using System.Collections.Generic;
using System.Globalization;

namespace StingTools.V6
{
    public static class AccClashCsv
    {
        public const string Header =
            "Score,Category,ClashId,PenetrationMm,Status,LeftDocument,RightDocument,LeftObjectId,RightObjectId,Rationale";

        public static List<string> Rows(IEnumerable<ScoredClash> scored, IReadOnlyDictionary<string, AccClashRecord> byId)
        {
            var ci = CultureInfo.InvariantCulture;
            var rows = new List<string> { Header };
            foreach (var s in scored ?? new List<ScoredClash>())
            {
                if (s == null) continue;
                AccClashRecord c = null;
                if (byId != null && s.ClashId != null) byId.TryGetValue(s.ClashId, out c);
                rows.Add(string.Join(",",
                    s.Score.ToString("F3", ci), Quote(s.Category), Quote(s.ClashId),
                    (c?.PenetrationMm ?? 0).ToString("F0", ci), Quote(c?.Status),
                    Quote(c?.LeftDocument), Quote(c?.RightDocument),
                    (c?.LeftObjectId ?? 0).ToString(ci), (c?.RightObjectId ?? 0).ToString(ci), Quote(s.Rationale)));
            }
            return rows;
        }

        public static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
