using System;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// Single migration helper for the 18 legacy Template Manager commands
    /// that historically ended with `TaskDialog.Show(title, body)`. The
    /// helper:
    ///   1. Builds an OperationResult from the title/body (extracts headline,
    ///      parses simple "Key: value" lines into metric tiles, captures
    ///      multi-line sections delimited by blank lines).
    ///   2. Publishes via OperationResultBus.
    ///   3. Falls back to TaskDialog when there's no subscriber so the
    ///      ribbon entry points keep working unchanged.
    ///
    /// One-line drop-in for each command:
    ///     LegacyResultAdapter.Publish("CreateFillPatterns", "Create Fill Patterns",
    ///         doc, $"Created {created}\nSkipped {skipped}\nTotal {total}",
    ///         created, skipped, 0);
    /// </summary>
    public static class LegacyResultAdapter
    {
        public static void Publish(string opTag, string label, Document doc, string body,
            int created = 0, int skipped = 0, int failed = 0,
            ResultSeverity severity = ResultSeverity.Success, string headline = null)
        {
            try
            {
                if (string.IsNullOrEmpty(headline)) headline = ExtractHeadline(body);
                var result = new OperationResult
                {
                    Operation = opTag,
                    OperationLabel = label,
                    Severity = severity,
                    Headline = headline,
                    DocumentPath = doc?.PathName ?? "",
                    UserName = Environment.UserName,
                    Counters =
                    {
                        ["created"] = created.ToString(),
                        ["skipped"] = skipped.ToString(),
                        ["failed"]  = failed.ToString()
                    }
                };
                AppendBodyAsSections(result, body);

                bool delivered = OperationResultBus.Publish(result);
                if (!delivered)
                {
                    try { TaskDialog.Show(label, string.IsNullOrEmpty(body) ? headline : body); }
                    catch (Exception ex) { StingTools.Core.StingLog.Warn($"LegacyResultAdapter TaskDialog: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"LegacyResultAdapter.Publish: {ex.Message}");
                try { TaskDialog.Show(label, body ?? headline ?? ""); } catch { }
            }
        }

        /// <summary>Publish a richer OperationResult directly when caller has structured data already.</summary>
        public static void PublishResult(string opTag, string label, Document doc, OperationResult result, string fallbackBody = null)
        {
            if (result == null) return;
            result.Operation = opTag;
            if (string.IsNullOrEmpty(result.OperationLabel)) result.OperationLabel = label;
            if (string.IsNullOrEmpty(result.DocumentPath)) result.DocumentPath = doc?.PathName ?? "";
            if (string.IsNullOrEmpty(result.UserName)) result.UserName = Environment.UserName;
            bool delivered = OperationResultBus.Publish(result);
            if (!delivered)
            {
                try { TaskDialog.Show(label, fallbackBody ?? result.Headline ?? ""); }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"LegacyResultAdapter TaskDialog: {ex.Message}"); }
            }
        }

        private static string ExtractHeadline(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            // First non-empty line, max 200 chars
            var first = body.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (first == null) return "";
            first = first.Trim();
            return first.Length > 200 ? first.Substring(0, 197) + "…" : first;
        }

        private static void AppendBodyAsSections(OperationResult r, string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return;
            // Split on blank-line section breaks.
            var sections = Regex.Split(body, @"\n\s*\n");
            foreach (var raw in sections)
            {
                var lines = raw.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                if (lines.Count == 0) continue;
                string title = lines[0].TrimEnd(':').Trim();
                if (string.IsNullOrEmpty(title)) continue;
                var sec = new ResultSection { Name = title };
                for (int i = 1; i < lines.Count; i++)
                {
                    var ln = lines[i].Trim();
                    if (string.IsNullOrEmpty(ln)) continue;
                    // "Key: value" → metric tile
                    var m = Regex.Match(ln, @"^\s*([A-Za-z0-9 _\-/]+?):\s+(.+)$");
                    if (m.Success && m.Groups[2].Value.Length <= 64)
                    {
                        sec.Metrics.Add((m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
                    }
                    else
                    {
                        // Treat as a free-text row in Notes
                        sec.Notes = string.IsNullOrEmpty(sec.Notes) ? ln : sec.Notes + "\n" + ln;
                    }
                }
                if (sec.Metrics.Count > 0 || !string.IsNullOrEmpty(sec.Notes))
                    r.Sections.Add(sec);
            }
        }
    }
}
