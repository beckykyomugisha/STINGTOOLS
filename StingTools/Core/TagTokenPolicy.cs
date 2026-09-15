// StingTools — per-token tag completeness policy
//
// Revit-free by design: every entry point takes strings and resolved paths, so the
// substitution decisions can be unit-tested without a Revit host. The Revit-bound
// caller (TagConfig.BuildAndWriteTag) hands it a token value and reads back what to
// write; this file never touches a Document.
//
// Layering matches VisibilityPresetStore / MepSizingRegistry: a corporate baseline
// from Data/STING_TAG_TOKEN_POLICY.json, with the per-project file layered on top,
// project rules winning by token name.
//
// WHY THIS IS DATA. Sectors disagree about which tokens may be assumed. A hospital
// treats ZONE as mandatory because it is the fire compartment; a single-building
// lodge does not. Encoding that in C# means every such project needs a code change.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core
{
    /// <summary>How a blank token is treated. The vocabulary is the shipped file's.</summary>
    public enum TagTokenLevel
    {
        /// <summary>Blank is legitimate and silent.</summary>
        Optional = 0,
        /// <summary>May fall back, but the fallback is RECORDED — complete-but-assumed
        /// is not the same as complete.</summary>
        Derived = 1,
        /// <summary>Never blank. A blank is an error: counted, surfaced in the tagging
        /// result, and the element reported as incomplete.</summary>
        Mandatory = 2,
    }

    /// <summary>One token's rule, as read from the policy file.</summary>
    public class TagTokenRule
    {
        public int Index { get; set; } = -1;
        public string Token { get; set; } = "";
        public string Param { get; set; } = "";

        /// <summary>Raw level string from JSON. Parsed by <see cref="ResolvedLevel"/> so a
        /// misspelling is visible rather than silently becoming the enum's zero value.</summary>
        public string Level { get; set; }

        /// <summary>The value to substitute for a blank. NULL means there is no defensible
        /// fallback — see <see cref="TagTokenPolicy.Resolve"/> for what that causes.</summary>
        public string Fallback { get; set; }

        public string Why { get; set; }

        /// <summary>
        /// Parse <see cref="Level"/>. Returns false for a missing or unrecognised value
        /// rather than defaulting: "OPTIONAL" is the enum's zero, so a typo in the JSON
        /// would silently downgrade a MANDATORY token to a silent blank — the exact
        /// Newtonsoft failure mode this codebase keeps producing.
        /// </summary>
        public bool TryResolveLevel(out TagTokenLevel level)
        {
            level = TagTokenLevel.Derived;
            if (string.IsNullOrWhiteSpace(Level)) return false;

            switch (Level.Trim().ToUpperInvariant())
            {
                case "MANDATORY": level = TagTokenLevel.Mandatory; return true;
                case "DERIVED": level = TagTokenLevel.Derived; return true;
                case "OPTIONAL": level = TagTokenLevel.Optional; return true;
                default: return false;
            }
        }
    }

    /// <summary>Root JSON document.</summary>
    public class TagTokenPolicyLibrary
    {
        public string SchemaVersion { get; set; }
        public string Name { get; set; }
        public List<TagTokenRule> Tokens { get; set; } = new List<TagTokenRule>();
    }

    /// <summary>What the policy decided for one token.</summary>
    public class TokenResolution
    {
        /// <summary>The value to use. Empty only when the token is OPTIONAL.</summary>
        public string Value { get; set; } = "";

        /// <summary>True when Value came from the policy's fallback rather than from
        /// the model. A tag built with any of these is complete-but-ASSUMED.</summary>
        public bool Substituted { get; set; }

        /// <summary>True when the token was blank and the policy offers no fallback.
        /// The caller must NOT write a tag — see Resolve.</summary>
        public bool Refused { get; set; }

        public TagTokenLevel Level { get; set; } = TagTokenLevel.Derived;

        /// <summary>Human-readable reason, set on Refused and on a policy defect.</summary>
        public string Reason { get; set; }
    }

    public static class TagTokenPolicy
    {
        public const string BaselineFileName = "STING_TAG_TOKEN_POLICY.json";
        public const string ProjectFileName = "tag_token_policy.json";

        /// <summary>
        /// The literals BuildAndWriteTag used before this file governed anything. They
        /// remain as the LAST-RESORT safety net for a token the merged policy does not
        /// mention at all — never writing a doubled separator is more important than
        /// honouring a policy that forgot a token. Reaching one of these is a defect and
        /// is reported through <see cref="TokenResolution.Reason"/>.
        /// </summary>
        private static readonly Dictionary<string, string> LastResort =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "DISC", "A" }, { "LOC", "BLD1" }, { "ZONE", "Z01" }, { "LVL", "L00" },
                { "SYS", "GEN" }, { "FUNC", "GEN" }, { "PROD", "GEN" },
            };

        public static TagTokenPolicyLibrary Parse(string json, IList<string> warnings = null)
        {
            if (string.IsNullOrWhiteSpace(json)) return new TagTokenPolicyLibrary();

            var lib = JsonConvert.DeserializeObject<TagTokenPolicyLibrary>(json)
                      ?? new TagTokenPolicyLibrary();
            if (lib.Tokens == null) lib.Tokens = new List<TagTokenRule>();
            lib.Tokens.RemoveAll(t => t == null || string.IsNullOrWhiteSpace(t.Token));

            // Normalise and report rather than silently accepting a malformed rule.
            foreach (var t in lib.Tokens)
            {
                t.Token = t.Token.Trim().ToUpperInvariant();
                TagTokenLevel parsed;
                if (!t.TryResolveLevel(out parsed) && warnings != null)
                    warnings.Add("Token '" + t.Token + "' has level '" + (t.Level ?? "(missing)") +
                                 "', which is not MANDATORY, DERIVED or OPTIONAL. It will be " +
                                 "treated as DERIVED.");
            }
            return lib;
        }

        public static string Serialise(TagTokenPolicyLibrary lib)
        {
            return JsonConvert.SerializeObject(lib ?? new TagTokenPolicyLibrary(), Formatting.Indented);
        }

        /// <summary>A missing file yields an empty library — the normal state for a project
        /// that has never overridden the policy, not an error.</summary>
        public static TagTokenPolicyLibrary LoadFile(string path, IList<string> warnings = null)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new TagTokenPolicyLibrary();
            try
            {
                return Parse(File.ReadAllText(path), warnings);
            }
            catch (Exception ex)
            {
                if (warnings != null)
                    warnings.Add("Could not read the tag token policy from '" +
                                 Path.GetFileName(path) + "': " + ex.Message);
                StingLog.Warn("TagTokenPolicy.LoadFile(" + path + "): " + ex.Message);
                return new TagTokenPolicyLibrary();
            }
        }

        /// <summary>
        /// Corporate baseline with the project file layered on top, project rules winning
        /// by token name. A project rule REPLACES the baseline rule wholesale rather than
        /// merging field by field — a half-overridden rule (new level, inherited fallback)
        /// is the kind of state nobody can reason about from either file alone.
        /// </summary>
        public static TagTokenPolicyLibrary Merge(
            TagTokenPolicyLibrary baseline, TagTokenPolicyLibrary project)
        {
            var merged = new TagTokenPolicyLibrary();
            var byToken = new Dictionary<string, TagTokenRule>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in (baseline ?? new TagTokenPolicyLibrary()).Tokens ?? new List<TagTokenRule>())
                byToken[t.Token] = t;
            foreach (var t in (project ?? new TagTokenPolicyLibrary()).Tokens ?? new List<TagTokenRule>())
                byToken[t.Token] = t;

            merged.SchemaVersion = (project != null && !string.IsNullOrWhiteSpace(project.SchemaVersion))
                ? project.SchemaVersion
                : (baseline != null ? baseline.SchemaVersion : null);
            merged.Tokens = byToken.Values.OrderBy(t => t.Index).ThenBy(t => t.Token, StringComparer.Ordinal).ToList();
            return merged;
        }

        public static TagTokenRule Find(TagTokenPolicyLibrary lib, string token)
        {
            if (lib == null || lib.Tokens == null || string.IsNullOrWhiteSpace(token)) return null;
            return lib.Tokens.FirstOrDefault(t =>
                string.Equals(t.Token, token, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Decide what to write for one token.
        ///
        ///   value non-empty              use it, record nothing
        ///   blank + OPTIONAL             leave blank, silently — blank is a real answer
        ///   blank + fallback present     substitute AND record it as assumed
        ///   blank + fallback null        REFUSE: there is no defensible value
        ///
        /// The refusal is the whole point of the file. A tag segment that was guessed and
        /// a tag segment that was measured must not be indistinguishable, and for SEQ and
        /// (if a project chooses) LVL there is no value that could be right — a tag ending
        /// in a bare separator is always wrong, so the honest output is no tag at all.
        /// </summary>
        public static TokenResolution Resolve(TagTokenPolicyLibrary lib, string token, string value)
        {
            var res = new TokenResolution();

            if (!string.IsNullOrEmpty(value))
            {
                res.Value = value;
                var known = Find(lib, token);
                TagTokenLevel lvlOk;
                if (known != null && known.TryResolveLevel(out lvlOk)) res.Level = lvlOk;
                return res;
            }

            var rule = Find(lib, token);
            if (rule == null)
            {
                // The policy does not mention this token. Fall back to the historic literal
                // so the tag never gains a doubled separator, and say so — this is a defect
                // in the policy file, not a normal path.
                string safety;
                if (LastResort.TryGetValue(token ?? "", out safety))
                {
                    res.Value = safety;
                    res.Substituted = true;
                    res.Level = TagTokenLevel.Derived;
                    res.Reason = "Token '" + token + "' is not described by " + BaselineFileName +
                                 " or the project override; used the built-in '" + safety + "'.";
                    return res;
                }
                res.Refused = true;
                res.Reason = "Token '" + token + "' is blank, and neither the policy nor the " +
                             "built-in safety net offers a value for it.";
                return res;
            }

            TagTokenLevel level;
            if (!rule.TryResolveLevel(out level)) level = TagTokenLevel.Derived;
            res.Level = level;

            if (level == TagTokenLevel.Optional)
            {
                res.Value = "";
                return res;
            }

            if (rule.Fallback != null)
            {
                res.Value = rule.Fallback;
                res.Substituted = true;
                return res;
            }

            res.Refused = true;
            res.Reason = "Token " + rule.Token + " is blank and " + BaselineFileName +
                         " gives it no fallback" +
                         (level == TagTokenLevel.Mandatory ? " (MANDATORY)" : "") +
                         (string.IsNullOrWhiteSpace(rule.Why) ? "." : ": " + rule.Why);
            return res;
        }
    }
}
