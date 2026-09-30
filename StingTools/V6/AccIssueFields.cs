// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccIssueFields.cs — ACC custom attributes and root cause on the issues STING
// escalates (acc_settings.json: issueCustomAttributes, issueRootCause).
//
// Settings name attributes and root causes by TITLE, because a title is what an Information
// Manager can read off ACC's admin page; ids differ per container. Titles are resolved
// against the container once per run. A title that does not resolve - absent, ambiguous, or
// a list attribute (which needs an option id STING has no way to choose) - is REPORTED, and
// the issue is still created without it: losing an escalation because of a label would be
// the worse failure.
//
// Revit-free; linked into StingTools.Acc.Tests.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StingTools.V6
{
    public sealed class AccIssueFieldResolution
    {
        /// <summary>STING field → resolved definition.</summary>
        public Dictionary<string, AccIssueAttributeDefinition> ByField { get; } =
            new Dictionary<string, AccIssueAttributeDefinition>(StringComparer.Ordinal);
        public string RootCauseId { get; set; } = string.Empty;
        /// <summary>Why a configured title was not used. Every entry is shown to the user.</summary>
        public List<string> Problems { get; } = new List<string>();
        public bool Any => ByField.Count > 0 || !string.IsNullOrEmpty(RootCauseId);

        /// <summary>The definition id carrying <paramref name="field"/>, or null.</summary>
        public string DefinitionIdFor(string field)
            => ByField.TryGetValue(field ?? "", out var d) ? d.Id : null;
    }

    public static class AccIssueFields
    {
        /// <summary>Resolve configured titles against what the container offers. Pure.</summary>
        public static AccIssueFieldResolution Resolve(
            IReadOnlyDictionary<string, string> mapping, IList<AccIssueAttributeDefinition> definitions,
            string rootCauseTitle, IList<AccRootCause> rootCauses)
        {
            var r = new AccIssueFieldResolution();
            foreach (var kv in mapping ?? new Dictionary<string, string>())
            {
                string title = (kv.Value ?? "").Trim();
                if (definitions == null) { r.Problems.Add($"{kv.Key}: custom attributes could not be listed, so '{title}' was not sent"); continue; }
                var hits = definitions.Where(d => string.Equals((d.Title ?? "").Trim(), title, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count == 0)
                {
                    r.Problems.Add($"{kv.Key}: no custom attribute titled '{title}' in this ACC project" +
                                   (definitions.Count == 0 ? " (it has none)" : " (has: " + string.Join(", ", definitions.Select(d => "'" + d.Title + "'")) + ")"));
                    continue;
                }
                if (hits.Count > 1) { r.Problems.Add($"{kv.Key}: {hits.Count} custom attributes are titled '{title}' — ambiguous, not sent"); continue; }
                var def = hits[0];
                if (string.Equals(def.DataType, "list", StringComparison.OrdinalIgnoreCase))
                {
                    r.Problems.Add($"{kv.Key}: '{title}' is a list attribute; its value must be one of its option ids, which STING cannot choose — use a text attribute");
                    continue;
                }
                r.ByField[kv.Key] = def;
            }

            string rc = (rootCauseTitle ?? "").Trim();
            if (rc.Length > 0)
            {
                if (rootCauses == null) r.Problems.Add($"root cause: root causes could not be listed, so '{rc}' was not sent");
                else
                {
                    var hits = rootCauses.Where(x => string.Equals((x.Title ?? "").Trim(), rc, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (hits.Count == 1) r.RootCauseId = hits[0].Id;
                    else if (hits.Count == 0)
                        r.Problems.Add($"root cause: none titled '{rc}' in this ACC project" +
                                       (rootCauses.Count == 0 ? "" : " (has: " + string.Join(", ", rootCauses.Select(x => "'" + x.Title + "'")) + ")"));
                    else r.Problems.Add($"root cause: {hits.Count} root causes are titled '{rc}' (in {string.Join(", ", hits.Select(h => h.CategoryTitle))}) — ambiguous, not sent");
                }
            }
            return r;
        }

        /// <summary>The customAttributes to send, from STING field → value. A numeric attribute
        /// whose value is not a number is skipped and reported; an empty value is not sent.</summary>
        public static List<AccCustomAttributeValue> Values(AccIssueFieldResolution r,
            IReadOnlyDictionary<string, string> stingValues, List<string> problems = null)
        {
            var list = new List<AccCustomAttributeValue>();
            if (r == null || stingValues == null) return list;
            foreach (var kv in r.ByField)
            {
                if (!stingValues.TryGetValue(kv.Key, out string v) || string.IsNullOrWhiteSpace(v)) continue;
                JToken value;
                if (string.Equals(kv.Value.DataType, "numeric", StringComparison.OrdinalIgnoreCase))
                {
                    if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    {
                        problems?.Add($"{kv.Key}: '{v}' is not a number, and '{kv.Value.Title}' is numeric — not sent");
                        continue;
                    }
                    value = new JValue(d);
                }
                else value = new JValue(v.Length > 1000 ? v.Substring(0, 1000) : v);
                list.Add(new AccCustomAttributeValue { AttributeDefinitionId = kv.Value.Id, Value = value });
            }
            return list;
        }

        /// <summary>Read definitions / root causes only for what the policy configures, then
        /// resolve. A failed read becomes a Problem, never an exception.</summary>
        public static async Task<AccIssueFieldResolution> ResolveAsync(AccCredentials creds,
            IReadOnlyDictionary<string, string> mapping, string rootCauseTitle)
        {
            List<AccIssueAttributeDefinition> defs = null;
            List<AccRootCause> causes = null;
            var early = new List<string>();
            if (mapping != null && mapping.Count > 0)
            {
                var d = await AccIssueSync.GetAttributeDefinitionsAsync(creds).ConfigureAwait(false);
                if (d.Succeeded) defs = d.Value; else early.Add("custom attributes: " + d.Detail);
            }
            if (!string.IsNullOrWhiteSpace(rootCauseTitle))
            {
                var c = await AccIssueSync.GetRootCausesAsync(creds).ConfigureAwait(false);
                if (c.Succeeded) causes = c.Value; else early.Add("root causes: " + c.Detail);
            }
            var r = Resolve(mapping, defs, rootCauseTitle, causes);
            r.Problems.InsertRange(0, early);
            return r;
        }
    }
}
