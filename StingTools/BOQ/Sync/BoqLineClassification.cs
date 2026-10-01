// ══════════════════════════════════════════════════════════════════════════
//  BoqLineClassification.cs — how a BOQ line is classified on Planscape Server
//  (DSCH-44 follow-up). Revit-free; tested in StingTools.Boq.Tests.
//
//  Every server QuantityLine needs a ClassificationCode (a required foreign key).
//  The sync used to send none, so new lines were inserted against Guid.Empty and
//  PostgreSQL refused them. The plugin does not know server ids; it sends the
//  classification it already carries — the line's NRM2 work section — as
//  (system "NRM2", code), and the server resolves it within the tenant or refuses
//  the push with the codes it is missing. No code is ever invented: a line with no
//  NRM2 section is reported before anything is pushed.
// ══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using System.Linq;

namespace StingTools.BOQ.Sync
{
    public static class BoqLineClassification
    {
        /// <summary>The server ClassificationSystem.Code the NRM2 section is resolved in.</summary>
        public const string SystemCode = "NRM2";

        /// <summary>The code sent for a line: its NRM2 section, trimmed; null when it has none.</summary>
        public static string CodeFor(string nrm2Section)
        {
            string s = (nrm2Section ?? "").Trim();
            return s.Length == 0 ? null : s;
        }

        /// <summary>
        /// Why a push must not start, or null. Counts lines with no NRM2 section —
        /// the server would refuse them, and a baseline created first would be left empty.
        /// </summary>
        public static string PreflightProblem(IEnumerable<string> nrm2Sections)
        {
            int missing = (nrm2Sections ?? Enumerable.Empty<string>()).Count(s => CodeFor(s) == null);
            return missing == 0 ? null
                : $"{missing} BOQ line(s) have no NRM2 section, so Planscape cannot classify them — "
                  + "assign a section (or exclude the lines) and sync again.";
        }
    }
}
