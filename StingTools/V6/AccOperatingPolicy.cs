// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccOperatingPolicy.cs
//
// One answer to "may I prompt, and if not what do I do instead?", for every ACC
// command in the fortnightly KUT coordination cycle.
//
// WHY ONE TYPE. Four commands need this decision (model-set picker, escalation
// confirmation, suitability picker, upload file picker). There was no unattended /
// no-prompt concept anywhere in the codebase, so whichever command implemented it
// first would have become the convention by accident, and the other three would
// have grown their own keys. That is the vocabulary drift ROADMAP IM-14 records for
// suitability codes and IM-17 records for transmittal status - two vocabularies for
// one field, reconciled by a silent fallback.
//
// THE CONTRACT THAT MATTERS MOST: absent configuration means PROMPT, never
// "assume". A missing settings file is the normal state of every project that
// exists today, and a default that silently picked a model set or a suitability
// code would make an unconfigured project act on a guess. Interactive is the safe
// default; unattended is opt-in, per project, in writing.
//
// SCOPE. These are PROJECT settings, resolved through StingPaths by the caller:
//     <project>/_BIM_COORD/acc_settings.json
// A coordination model-set id is project-scoped - the same coordinator working on
// KUT and on another job needs a different one per model. Credentials stay machine-
// scoped in %APPDATA%\Planscape\acc_credentials.json.
//   KNOWN MIS-SCOPING, deliberately not fixed here: AccCredentials also carries
//   ProjectId and CoordContainerId, which are project-scoped values living in a
//   machine-scoped file. That is a migration with its own verification, not a side
//   quest; see the PR that introduced this file.
//
// Revit-free AND log-free, so it links into StingTools.Acc.Tests. It builds no
// project paths of its own - the caller holds the Document and asks StingPaths,
// exactly as CommissioningSource and NiagaraConnection do.
//
// PARSED WITH JObject, NOT DeserializeObject, on purpose. Newtonsoft leaves an
// absent or mistyped field at its type default, which would make "escalateMinScore
// is not configured" and "escalateMinScore is 0.0" the same value - and 0.0 as a
// score threshold escalates everything. Presence is checked explicitly, and a field
// that is present but unreadable makes the WHOLE file malformed rather than
// silently taking a default.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.V6
{
    /// <summary>Where the answers came from. <see cref="Malformed"/> answers exactly like
    /// <see cref="Absent"/> - everything reverts to prompting - but is a DIFFERENT value,
    /// because a corrupt settings file must never read as a deliberate default. That
    /// conflation is the IM-17 shape: two meanings for one state, reconciled silently.</summary>
    public enum AccPolicySource
    {
        /// <summary>No settings file. The normal state; everything prompts.</summary>
        Absent = 0,
        /// <summary>A settings file was read and understood.</summary>
        Loaded = 1,
        /// <summary>A settings file exists and could not be trusted. Every setting is
        /// discarded (not partially applied) and everything prompts.</summary>
        Malformed = 2,
    }

    /// <summary>How a remembered coordination model set resolved against what ACC returned.</summary>
    public enum AccModelSetResolution
    {
        /// <summary>The remembered id is present in the container. Use it, no picker.</summary>
        Chosen = 0,
        /// <summary>Nothing is remembered. Prompt.</summary>
        NoneRemembered = 1,
        /// <summary>Something is remembered and ACC no longer returns it - renamed, archived
        /// or replaced. NEVER falls through to "the first one": pulling clashes from the
        /// wrong model set is worse than pulling none, because it looks like a clean
        /// federation, which is the exact defect PR #927 closed.</summary>
        RememberedMissing = 2,
        /// <summary>The container returned no model sets at all. Not a resolution failure -
        /// the caller's existing empty-but-successful path owns this, and conflating the two
        /// would turn a legitimately empty container into an error.</summary>
        NoSetsAvailable = 3,
    }

    /// <summary>The outcome of matching a remembered model set against the available ones.</summary>
    public sealed class AccModelSetChoice
    {
        public AccModelSetResolution Resolution { get; set; } = AccModelSetResolution.NoneRemembered;

        /// <summary>Non-null only when <see cref="Resolution"/> is <see cref="AccModelSetResolution.Chosen"/>.</summary>
        public AccModelSet Chosen { get; set; }

        public string RememberedId { get; set; } = string.Empty;
        public string RememberedName { get; set; } = string.Empty;

        /// <summary>Human sentence naming what happened. Empty when Chosen.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>True only when a set was actually resolved.</summary>
        public bool Resolved => Resolution == AccModelSetResolution.Chosen && Chosen != null;
    }

    /// <summary>How many clashes to escalate to ACC Issues, and above what triage score.
    ///
    /// BOTH, never either. A count alone escalates trivia on a clean model; a score alone
    /// escalates hundreds on a bad one. A configuration giving one without the other is
    /// REFUSED (escalation off, with the reason named) rather than half-applied.</summary>
    public sealed class AccEscalationPolicy
    {
        public bool Enabled { get; private set; }
        public int MaxCount { get; private set; }
        public double MinScore { get; private set; }

        /// <summary>Why escalation is off. Empty when it is on.</summary>
        public string DisabledReason { get; private set; } = string.Empty;

        public static AccEscalationPolicy Off(string reason) =>
            new AccEscalationPolicy { Enabled = false, DisabledReason = reason ?? string.Empty };

        public static AccEscalationPolicy On(int maxCount, double minScore) =>
            new AccEscalationPolicy { Enabled = true, MaxCount = maxCount, MinScore = minScore };

        /// <summary>One line for a dialog or a log, so an operator can see the rule that is
        /// about to apply rather than inferring it from the result.</summary>
        public string Describe() => Enabled
            ? $"escalate at most {MaxCount} clash(es) scoring {MinScore:F2} or higher"
            : $"escalation is OFF ({DisabledReason})";
    }

    /// <summary>What an escalation step will actually do, and why. The whole decision in
    /// one place so it can be asserted without a Revit dialog.</summary>
    public sealed class AccEscalationPlan
    {
        /// <summary>The clashes to push. Empty means push nothing.</summary>
        public IReadOnlyList<ScoredClash> ToPush { get; set; } = Array.Empty<ScoredClash>();

        /// <summary>Whether an interactive run may OFFER a push. False in an unattended run
        /// (there is nobody to offer it to) and false when there is nothing to offer.</summary>
        public bool OfferInteractively { get; set; }

        /// <summary>Human sentence: the rule that applied and what it selected.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>How many were skipped because they are already tracked in
        /// pushed_clashes.json. Reported so "nothing to do" is distinguishable from
        /// "nothing qualified".</summary>
        public int AlreadyTracked { get; set; }
    }

    public sealed class AccOperatingPolicy
    {
        /// <summary>The settings file name. One constant, so the reader and every writer
        /// cannot disagree about it.</summary>
        public const string FileName = "acc_settings.json";

        /// <summary>Interactive escalation offer when no policy is configured. This is the
        /// pre-existing behaviour of AccPullClashesCommand and is preserved exactly: a
        /// person clicking the button still gets the top 10 offered. It is NOT a default
        /// for unattended runs - those escalate nothing without an explicit policy.</summary>
        public const int InteractiveFallbackCount = 10;

        private static readonly HashSet<string> KnownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "unattended", "coordModelSetId", "coordModelSetName",
            "escalateMaxCount", "escalateMinScore", "publishSuitability",
        };

        public AccPolicySource Source { get; private set; } = AccPolicySource.Absent;

        /// <summary>Why the file could not be trusted. Non-empty only when
        /// <see cref="Source"/> is <see cref="AccPolicySource.Malformed"/>.</summary>
        public string LoadError { get; private set; } = string.Empty;

        /// <summary>The path that was consulted, so a report can name it.</summary>
        public string SettingsPath { get; private set; } = string.Empty;

        // ── The four answers ────────────────────────────────────────────────

        /// <summary>May this run show a dialog and wait for a person?
        ///
        /// TRUE unless the project explicitly opted out, and ALWAYS true when the file is
        /// absent or malformed. Note this is a PROJECT MODE, not a per-invocation flag:
        /// IExternalCommand carries no run-mode, and inventing one would mean threading a
        /// flag through every dispatch layer. A project that has opted out behaves the same
        /// whether a timer or a person started the run, which is also the honest thing - an
        /// operator then sees exactly what the scheduled run would produce.</summary>
        public bool MayPrompt { get; private set; } = true;

        /// <summary>Convenience inverse; reads better at call sites that branch on it.</summary>
        public bool IsUnattended => !MayPrompt;

        /// <summary>The remembered coordination model-set id, or empty when none.</summary>
        public string CoordModelSetId { get; private set; } = string.Empty;

        /// <summary>The remembered set's name, for reporting only. Never used to match -
        /// a rename must not silently repoint the cycle at a different set.</summary>
        public string CoordModelSetName { get; private set; } = string.Empty;

        /// <summary>Never null. Off by default.</summary>
        public AccEscalationPolicy Escalation { get; private set; } =
            AccEscalationPolicy.Off("no escalation policy is configured for this project");

        /// <summary>The suitability code an unattended publish should use, or empty to
        /// prompt exactly as today.</summary>
        public string PublishSuitability { get; private set; } = string.Empty;

        // ── Loading ─────────────────────────────────────────────────────────

        /// <summary>The prompt-preserving default: everything asks, nothing is assumed.</summary>
        public static AccOperatingPolicy Interactive(string settingsPath = "") => new AccOperatingPolicy
        {
            Source = AccPolicySource.Absent,
            SettingsPath = settingsPath ?? string.Empty,
        };

        /// <summary>Read the policy from an ALREADY-RESOLVED path. Never throws.
        ///
        /// A file that is absent, unreadable, not an object, carries an unknown key, or
        /// carries a key of the wrong type yields the prompt-preserving default. The first
        /// three are obvious; the last two are deliberate and strict, because a typo'd key
        /// (<c>coordModelSet</c> for <c>coordModelSetId</c>) that is merely ignored leaves an
        /// Information Manager believing the project is configured when it is not, and the
        /// only symptom is a picker that keeps appearing.</summary>
        public static AccOperatingPolicy Load(string settingsPath)
        {
            var policy = Interactive(settingsPath);
            if (string.IsNullOrEmpty(settingsPath)) return policy;

            string text;
            try
            {
                if (!File.Exists(settingsPath)) return policy;      // Absent - the normal state
                text = File.ReadAllText(settingsPath);
            }
            catch (Exception ex) { return Malformed(policy, "the settings file could not be read: " + ex.Message); }

            if (string.IsNullOrWhiteSpace(text))
                return Malformed(policy, "the settings file is empty");

            JObject o;
            try { o = JObject.Parse(text); }
            catch (Exception ex) { return Malformed(policy, "the settings file is not valid JSON: " + ex.Message); }

            // Unknown keys are an error, not noise. See the Load doc-comment.
            var unknown = o.Properties()
                           .Select(p => p.Name)
                           .Where(n => !n.StartsWith("_", StringComparison.Ordinal) && !KnownKeys.Contains(n))
                           .ToList();
            if (unknown.Count > 0)
                return Malformed(policy,
                    "the settings file carries key(s) this build does not read: " + string.Join(", ", unknown) +
                    ". Known keys are: " + string.Join(", ", KnownKeys.OrderBy(k => k, StringComparer.Ordinal)));

            bool unattended = false;
            string modelSetId = string.Empty, modelSetName = string.Empty, suitability = string.Empty;
            int? maxCount = null;
            double? minScore = null;

            try
            {
                if (TryGet(o, "unattended", out var uTok)) unattended = RequireBool(uTok, "unattended");
                if (TryGet(o, "coordModelSetId", out var idTok)) modelSetId = RequireString(idTok, "coordModelSetId");
                if (TryGet(o, "coordModelSetName", out var nmTok)) modelSetName = RequireString(nmTok, "coordModelSetName");
                if (TryGet(o, "publishSuitability", out var suTok)) suitability = RequireString(suTok, "publishSuitability");
                if (TryGet(o, "escalateMaxCount", out var mcTok)) maxCount = RequireInt(mcTok, "escalateMaxCount");
                if (TryGet(o, "escalateMinScore", out var msTok)) minScore = RequireDouble(msTok, "escalateMinScore");
            }
            catch (FormatException ex) { return Malformed(policy, ex.Message); }

            policy.Source = AccPolicySource.Loaded;
            policy.MayPrompt = !unattended;
            policy.CoordModelSetId = modelSetId.Trim();
            policy.CoordModelSetName = modelSetName.Trim();
            policy.PublishSuitability = suitability.Trim();
            policy.Escalation = BuildEscalation(maxCount, minScore);
            return policy;
        }

        /// <summary>Both or neither. Named here so the rule has one home and one wording.</summary>
        internal static AccEscalationPolicy BuildEscalation(int? maxCount, double? minScore)
        {
            if (maxCount == null && minScore == null)
                return AccEscalationPolicy.Off("no escalation policy is configured for this project");
            if (maxCount == null)
                return AccEscalationPolicy.Off(
                    "escalateMinScore is set but escalateMaxCount is not - a score alone escalates " +
                    "hundreds of clashes on a bad model, so both are required");
            if (minScore == null)
                return AccEscalationPolicy.Off(
                    "escalateMaxCount is set but escalateMinScore is not - a count alone escalates " +
                    "trivia on a clean model, so both are required");
            if (maxCount.Value <= 0)
                return AccEscalationPolicy.Off($"escalateMaxCount is {maxCount.Value}, so nothing would be escalated");
            return AccEscalationPolicy.On(maxCount.Value, minScore.Value);
        }

        private static AccOperatingPolicy Malformed(AccOperatingPolicy policy, string why)
        {
            // Every setting is discarded, not partially applied: half a configuration is a
            // guess wearing a file's authority. The values stay at their prompt-preserving
            // defaults; only Source and LoadError change.
            policy.Source = AccPolicySource.Malformed;
            policy.LoadError = why ?? string.Empty;
            return policy;
        }

        private static bool TryGet(JObject o, string key, out JToken tok)
        {
            tok = o[key];
            return tok != null && tok.Type != JTokenType.Null;
        }

        private static bool RequireBool(JToken t, string key) =>
            t.Type == JTokenType.Boolean ? (bool)t
            : throw new FormatException($"'{key}' must be true or false, found {t.Type}");

        private static string RequireString(JToken t, string key) =>
            t.Type == JTokenType.String ? ((string)t ?? string.Empty)
            : throw new FormatException($"'{key}' must be a string, found {t.Type}");

        private static int RequireInt(JToken t, string key) =>
            t.Type == JTokenType.Integer ? (int)t
            : throw new FormatException($"'{key}' must be a whole number, found {t.Type}");

        private static double RequireDouble(JToken t, string key) =>
            (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) ? (double)t
            : throw new FormatException($"'{key}' must be a number, found {t.Type}");

        // ── The answers, as decisions rather than fields ────────────────────

        /// <summary>Match the remembered model set against what ACC actually returned.
        ///
        /// Pure: no I/O, no dialog. The one rule worth restating is that a remembered id the
        /// container no longer offers resolves to <see cref="AccModelSetResolution.RememberedMissing"/>
        /// and NEVER to the first available set.</summary>
        public AccModelSetChoice ResolveModelSet(IReadOnlyList<AccModelSet> available)
        {
            var choice = new AccModelSetChoice
            {
                RememberedId = CoordModelSetId,
                RememberedName = CoordModelSetName,
            };

            if (available == null || available.Count == 0)
            {
                choice.Resolution = AccModelSetResolution.NoSetsAvailable;
                choice.Reason = "the container returned no coordination model sets";
                return choice;
            }
            if (string.IsNullOrEmpty(CoordModelSetId))
            {
                choice.Resolution = AccModelSetResolution.NoneRemembered;
                choice.Reason = "no coordination model set is remembered for this project";
                return choice;
            }

            var hit = available.FirstOrDefault(s =>
                string.Equals(s?.Id, CoordModelSetId, StringComparison.OrdinalIgnoreCase));
            if (hit != null)
            {
                choice.Resolution = AccModelSetResolution.Chosen;
                choice.Chosen = hit;
                return choice;
            }

            choice.Resolution = AccModelSetResolution.RememberedMissing;
            choice.Reason =
                $"the remembered coordination model set is no longer offered by this container: " +
                $"'{(string.IsNullOrEmpty(CoordModelSetName) ? "(name not recorded)" : CoordModelSetName)}' " +
                $"[{CoordModelSetId}]. It may have been renamed, archived or replaced. " +
                $"{available.Count} set(s) are available; none of them is being assumed.";
            return choice;
        }

        /// <summary>Decide what an escalation step does.
        ///
        /// Unattended: only what the policy names, and nothing at all without one.
        /// Interactive: the policy when there is one; otherwise the pre-existing top-N offer,
        /// unchanged, because a person clicking the button is the consent the policy replaces.
        ///
        /// Already-tracked clashes are removed BEFORE the count is applied, so a cycle whose
        /// top entries were escalated last fortnight still escalates the next ones down rather
        /// than filling its quota with rows it will skip anyway.</summary>
        public AccEscalationPlan PlanEscalation(
            IReadOnlyList<ScoredClash> scored,
            Func<ScoredClash, string> signatureOf,
            ISet<string> alreadyTracked)
        {
            var plan = new AccEscalationPlan();
            var all = scored ?? Array.Empty<ScoredClash>();
            var tracked = alreadyTracked ?? new HashSet<string>(StringComparer.Ordinal);

            Func<ScoredClash, bool> isTracked = s =>
            {
                if (signatureOf == null) return false;
                string sig = signatureOf(s);
                return !string.IsNullOrEmpty(sig) && tracked.Contains(sig);
            };

            if (Escalation.Enabled)
            {
                var qualifying = all.Where(s => s != null && s.Score >= Escalation.MinScore).ToList();
                var fresh = qualifying.Where(s => !isTracked(s)).ToList();
                plan.AlreadyTracked = qualifying.Count - fresh.Count;
                plan.ToPush = fresh.OrderByDescending(s => s.Score)
                                   .Take(Escalation.MaxCount)
                                   .ToList();
                plan.OfferInteractively = MayPrompt && plan.ToPush.Count > 0;
                plan.Reason = $"policy: {Escalation.Describe()}. " +
                              $"{qualifying.Count} of {all.Count} clash(es) met the score; " +
                              $"{plan.AlreadyTracked} already tracked; {plan.ToPush.Count} to push.";
                return plan;
            }

            if (IsUnattended)
            {
                // The whole point of A3. An unattended run creates ACC Issues assigned to
                // real people; it may not do that on a default nobody chose.
                plan.ToPush = Array.Empty<ScoredClash>();
                plan.OfferInteractively = false;
                plan.Reason = $"unattended run and {Escalation.Describe()} - pulled and triaged, escalated nothing";
                return plan;
            }

            // Interactive with no policy: exactly today's behaviour.
            var top = all.Where(s => s != null)
                         .OrderByDescending(s => s.Score)
                         .Take(InteractiveFallbackCount)
                         .ToList();
            var offerable = top.Where(s => !isTracked(s)).ToList();
            plan.AlreadyTracked = top.Count - offerable.Count;
            plan.ToPush = top;      // the push itself skips tracked ones, as it always has
            plan.OfferInteractively = top.Count > 0;
            plan.Reason = $"{Escalation.Describe()}; offering the top {top.Count} for a person to confirm";
            return plan;
        }

        /// <summary>The suitability an unattended publish should use, or empty to prompt.
        /// Validated against the ISO 19650 shared set so a typo cannot reach a bundle name:
        /// an unrecognised code prompts rather than being written through.</summary>
        public string ResolveSuitability(IReadOnlyCollection<string> validCodes, out string reason)
        {
            if (string.IsNullOrEmpty(PublishSuitability))
            {
                reason = Source == AccPolicySource.Malformed
                    ? "the project settings file could not be read, so the suitability was not taken from it"
                    : "no publish suitability is configured for this project";
                return string.Empty;
            }
            if (validCodes != null && validCodes.Count > 0 &&
                !validCodes.Contains(PublishSuitability, StringComparer.OrdinalIgnoreCase))
            {
                reason = $"the configured publish suitability '{PublishSuitability}' is not a recognised code " +
                         $"({string.Join(", ", validCodes)})";
                return string.Empty;
            }
            reason = $"using the configured publish suitability '{PublishSuitability}'";
            return PublishSuitability;
        }

        /// <summary>One line naming where the answers came from, for a log or a dialog.</summary>
        public string DescribeSource() => Source switch
        {
            AccPolicySource.Loaded => $"project ACC settings: {SettingsPath}",
            AccPolicySource.Malformed => $"project ACC settings at {SettingsPath} could NOT be read ({LoadError}) - " +
                                         "every setting was discarded and this run will prompt",
            AccPolicySource.Absent => "no project ACC settings file - this run will prompt for everything",
            _ => throw new ArgumentOutOfRangeException(nameof(Source), Source,
                     "Unhandled AccPolicySource: every source needs a description, or a reader is told nothing."),
        };
    }
}
