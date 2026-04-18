// ClashSlaIntegration.cs — wire clash groups into the existing CoordIssue SLA engine.
//
// Prompt's heredoc assumed `CoordIssue` lived in StingTools.Core with CreatedUtc. In
// this repo it is Planscape.Shared.BCF.CoordIssue with CreationDate (Phase 95). Fields
// adjusted to match the real type per prompt's anticipated-mismatch note.
using System;
using System.Collections.Generic;
using System.Linq;
using Planscape.Shared.BCF;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public static class ClashSlaIntegration
    {
        public static List<CoordIssue> CreateIssues(ClashRunRecord run, ClashMatrix matrix)
        {
            var result = new List<CoordIssue>();
            if (run?.Groups == null) return result;
            foreach (var g in run.Groups)
            {
                var cell = matrix?.Cells?.FirstOrDefault(c => c.PairId == g.Anchor);
                var issue = new CoordIssue
                {
                    Guid = Guid.NewGuid().ToString(),
                    Title = $"Clash group {g.Id} ({g.Size} clashes)",
                    Description = $"Matrix pair {g.Anchor}. Severity {cell?.Severity ?? "MED"}.",
                    Status = "Open",
                    Priority = cell?.Severity ?? "MED",
                    Assignee = cell?.OwnerDiscipline ?? "Coord",
                    CreationDate = DateTime.UtcNow
                };
                g.Assignee = issue.Assignee;
                result.Add(issue);
            }
            return result;
        }
    }
}
