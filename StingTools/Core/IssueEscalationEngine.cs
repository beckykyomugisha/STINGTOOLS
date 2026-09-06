// ══════════════════════════════════════════════════════════════════════════
//  IssueEscalationEngine.cs — the ONE warning→issue escalation path.
//  ISO IM runner, Phase 2 (ROADMAP IM-5).
//
//  WHAT THIS REPLACES
//  ------------------
//  Four independent escalation implementations, each with its own dedup key,
//  its own identifier minting and its own serialization:
//
//    WarningsEngine.CreateIssuesFromWarnings        — grouped by Category,
//        dedup by NOTHING (re-raised the same issues every run), ids by a
//        hand-rolled string scan, record built by STRING CONCATENATION with
//        element_ids as a comma-joined string rather than an array.
//    WarningsEngineExt.AutoCreateIssuesFromWarnings — grouped by description
//        prefix, dedup on the description text, ids by max-numeric-suffix.
//    WarningsEngineExt.AutoRaiseStaleIssues         — dedup by "does any open
//        issue's title contain the word 'stale'", ids by max-numeric-suffix,
//        and it wrote through a hand-rolled path that bypassed the legacy merge.
//    Phase75Enhancements.WarningToIssueCreator      — grouped by description
//        prefix, dedup on (category, description), ids by Count+1.
//
//  Three different dedup rules over the same register meant an escalation
//  raised by one entry point was invisible to the next one's dedup check, so
//  the same warning could hold three issues at once — under three different
//  identifier spellings.
//
//  All four now funnel into Escalate(), which:
//    * groups on a caller-supplied key,
//    * derives a STABLE dedup hash per group and hands it to IssueStore, which
//      refuses to re-raise while the prior issue is still open,
//    * mints ids through IssueIdMinter (per-type, high-water mark, reserved
//      across the batch),
//    * writes the canonical schema once, atomically,
//    * stamps provenance so a coordinator can tell where each issue came from.
//
//  Entry points keep their public signatures and return shapes — this is a
//  substitution of the engine underneath, not a change to their callers.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>One escalation candidate: a group of related findings that warrant one issue.</summary>
    internal sealed class EscalationCandidate
    {
        /// <summary>Stable identity of the finding — becomes source_hash, and therefore the dedup key.</summary>
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        /// <summary>Register bucket + id prefix: NCR / SI / RFI / CLASH.</summary>
        public string Type { get; set; } = "SI";
        public string Priority { get; set; } = "HIGH";
        public string Discipline { get; set; } = "";
        public List<long> ElementIds { get; set; } = new List<long>();
        /// <summary>Extra fields merged onto the record (warning_category, source_warning, …).</summary>
        public JObject Extra { get; set; }
    }

    /// <summary>Outcome of one escalation pass.</summary>
    internal sealed class EscalationResult
    {
        public int Created { get; set; }
        /// <summary>Candidates skipped because an open issue already covered them.</summary>
        public int Deduped { get; set; }
        public List<string> Details { get; } = new List<string>();
        /// <summary>(issueId, title, elementCount) for callers that report a table.</summary>
        public List<(string issueId, string title, int elementCount)> Rows { get; } =
            new List<(string, string, int)>();
    }

    internal static class IssueEscalationEngine
    {
        /// <summary>Cap on issues raised per pass, so one bad scan cannot flood the register.</summary>
        private const int MaxPerPass = 20;

        /// <summary>
        /// Raise one issue per candidate, deduped against still-open issues with the same
        /// (source, key). One load, one atomic save, one audit pass.
        /// </summary>
        public static EscalationResult Escalate(Document doc, IEnumerable<EscalationCandidate> candidates,
                                                IssueSource source, int cap = MaxPerPass)
        {
            var result = new EscalationResult();
            if (doc == null || candidates == null) return result;

            try
            {
                using var batch = IssueStore.Begin(doc);
                if (!batch.Ok)
                {
                    StingLog.Warn("IssueEscalationEngine: issue register unreadable — escalation skipped.");
                    return result;
                }

                string revision = "";
                try { revision = PhaseAutoDetect.DetectProjectRevision(doc) ?? ""; }
                catch (Exception ex) { StingLog.Warn($"IssueEscalationEngine revision: {ex.Message}"); }

                foreach (var c in candidates)
                {
                    if (result.Created >= cap) break;
                    if (c == null || string.IsNullOrWhiteSpace(c.Title)) continue;

                    // A candidate with no key cannot be deduped, and an escalation that cannot
                    // dedup re-raises itself on every scan. Fall back to the title.
                    string key = string.IsNullOrWhiteSpace(c.Key) ? c.Title : c.Key;

                    var spec = new IssueSpec
                    {
                        Type        = c.Type,
                        Title       = c.Title,
                        Description = c.Description,
                        Priority    = c.Priority,
                        Discipline  = c.Discipline,
                        Revision    = revision,
                        Source      = source,
                        SourceHash  = key,
                        ElementIds  = c.ElementIds ?? new List<long>(),
                        Extra       = c.Extra,
                    };

                    int before = batch.Created.Count;
                    JObject row = batch.Create(spec);
                    if (row == null) continue;

                    if (batch.Created.Count == before) { result.Deduped++; continue; }

                    string id = IssueSchema.IdOf(row);
                    result.Created++;
                    result.Rows.Add((id, c.Title, c.ElementIds?.Count ?? 0));
                    result.Details.Add($"Created {id}: {Truncate(c.Title, 60)}");
                }

                batch.Commit();
            }
            catch (Exception ex) { StingLog.Error("IssueEscalationEngine.Escalate", ex); }

            return result;
        }

        // ── Candidate builders (one per legacy entry point) ────────────────

        /// <summary>
        /// Group classified warnings by CATEGORY — the shape
        /// WarningsEngine.CreateIssuesFromWarnings used.
        /// </summary>
        public static List<EscalationCandidate> ByCategory(
            IEnumerable<ClassifiedWarning> warnings, WarningSeverity minSeverity)
        {
            var list = new List<EscalationCandidate>();
            if (warnings == null) return list;

            // Critical=0, High=1 — a LOWER enum value is a HIGHER severity.
            foreach (var group in warnings.Where(w => w != null && w.Severity <= minSeverity)
                                          .GroupBy(w => w.Category))
            {
                var members = group.ToList();
                if (members.Count == 0) continue;

                var worst = members.Min(w => w.Severity);
                bool critical = worst == WarningSeverity.Critical;

                var elementIds = new HashSet<long>();
                foreach (var w in members)
                    if (w.FailingElements != null)
                        foreach (var eid in w.FailingElements) elementIds.Add(eid.Value);

                list.Add(new EscalationCandidate
                {
                    Key         = $"category:{group.Key}",
                    Type        = critical ? "NCR" : "SI",
                    Priority    = critical ? "CRITICAL" : "HIGH",
                    Title       = $"Warning: {group.Key} — {members.Count} {(critical ? "critical" : "high")} issues detected",
                    Description = $"Auto-created from {members.Count} Revit warnings in category {group.Key}.",
                    Discipline  = members.FirstOrDefault()?.Discipline ?? "",
                    ElementIds  = elementIds.ToList(),
                    Extra       = new JObject
                    {
                        ["warning_category"] = group.Key.ToString(),
                        ["element_count"]    = elementIds.Count,
                    },
                });
            }
            return list;
        }

        /// <summary>
        /// Group classified warnings by DESCRIPTION prefix — the shape both
        /// AutoCreateIssuesFromWarnings and WarningToIssueCreator used. Their two
        /// slightly different prefix lengths (50 vs 80) are unified on 80, which is
        /// what the titles were already truncated to.
        /// </summary>
        public static List<EscalationCandidate> ByDescription(
            IEnumerable<ClassifiedWarning> warnings, WarningSeverity minSeverity)
        {
            var list = new List<EscalationCandidate>();
            if (warnings == null) return list;

            foreach (var group in warnings
                         .Where(w => w != null && w.Severity <= minSeverity && !string.IsNullOrEmpty(w.Description))
                         .GroupBy(w => Truncate(w.Description, 80)))
            {
                var rep = group.First();
                bool critical = rep.Severity == WarningSeverity.Critical;

                var elementIds = group.SelectMany(w => w.FailingElements ?? Array.Empty<ElementId>())
                                      .Select(e => e.Value).Distinct().ToList();

                list.Add(new EscalationCandidate
                {
                    Key         = $"{rep.Category}:{group.Key}",
                    Type        = critical ? "NCR" : "SI",
                    Priority    = critical ? "CRITICAL" : "HIGH",
                    Title       = $"[Auto] {group.Key}",
                    Description = $"Auto-created from {group.Count()} {rep.Category} warnings." +
                                  (string.IsNullOrWhiteSpace(rep.FixStrategy) ? "" : $" Fix: {rep.FixStrategy}"),
                    Discipline  = rep.Discipline ?? "GEN",
                    ElementIds  = elementIds,
                    Extra       = new JObject
                    {
                        ["warning_category"] = rep.Category.ToString(),
                        ["source_warning"]   = group.Key,
                        ["element_count"]    = elementIds.Count,
                    },
                });
            }
            return list;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max));
    }
}
