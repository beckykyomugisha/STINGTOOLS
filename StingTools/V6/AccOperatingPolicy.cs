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
//   The ACC container ids (projectId, coordContainerId) are project-scoped too and
//   are read from here first (IM-18); the credentials file's copies are a logged,
//   deprecated fallback - see AccProjectScope.
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
            "projectId", "coordContainerId",
            // Project-scoped values that used to live in the machine credentials file (IM-18
            // moved the container ids; these are the rest of the same defect).
            "hubId", "folderUrn", "distToMm", "issueTypeId", "issueSubtypeId", "region",
            // Flexibility: where each CDE state lives, how a model name maps to a discipline,
            // and what an escalated issue carries.
            "cdeFolders", "disciplineMap", "docsAttributes", "docsAttributesCreateMissing",
            "escalateDueDays", "escalateAssignedTo", "escalateAssignedToType", "escalateExcludeStatuses",
            // ACC-HARD-5: how an escalated clash issue lets its assignee FIND the objects.
            "issueDeepLinks", "issueViewerLinks", "issueBcfAttachment",
            // ACC Docs naming and metadata: the attribute names the project's ACC admin
            // created (one set, not a STING set beside the admin's), and whether file names
            // are the 7-field ISO 19650 name or carry suitability + revision (9 fields).
            "docsAttributeNames", "fileNamingFields",
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

        /// <summary>The ACC project (Issues container) for THIS project, or empty. IM-18:
        /// this used to live only in the machine-wide credentials file, so a coordinator on
        /// two jobs had one value for both. See <see cref="AccProjectScope"/>.</summary>
        public string ProjectId { get; private set; } = string.Empty;

        /// <summary>The Model Coordination container for this project, or empty (then the
        /// Issues container is used).</summary>
        public string CoordContainerId { get; private set; } = string.Empty;

        /// <summary>The ACC hub (account) the project lives in. Needed to resolve the
        /// project's top folders; discovery records it.</summary>
        public string HubId { get; private set; } = string.Empty;

        /// <summary>Default upload folder for this project. A folder URN belongs to ONE ACC
        /// project; in the machine file it pointed a coordinator's uploads for job B at job
        /// A's folder.</summary>
        public string FolderUrn { get; private set; } = string.Empty;

        /// <summary>Multiply ACC clash 'dist' by this to get millimetres; null = not set.</summary>
        public double? DistToMm { get; private set; }

        /// <summary>The ACC issue type / subtype to raise STING issues as, for THIS project's
        /// container. Empty = resolve from the container by name, never by position.</summary>
        public string IssueTypeId { get; private set; } = string.Empty;
        public string IssueSubtypeId { get; private set; } = string.Empty;

        /// <summary>Where the ACC project is hosted: US (default), EMEA or AUS. Sent as the
        /// region header on the ACC APIs that take one.</summary>
        public string Region { get; private set; } = string.Empty;

        /// <summary>CDE state (WIP / SHARED / PUBLISHED / ARCHIVE) → ACC folder URN. A state
        /// with no entry is NOT CONFIGURED - an upload for it is refused with that reason, it
        /// is never sent to some other folder.</summary>
        public IReadOnlyDictionary<string, string> CdeFolders { get; private set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Model-name token → discipline code (S, M, P, E, FP, A …). Consulted before
        /// the ISO 19650 role field and the built-in words.</summary>
        public IReadOnlyDictionary<string, string> DisciplineMap { get; private set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Stamp ISO 19650 metadata onto uploaded ACC documents as custom attributes.</summary>
        public bool DocsAttributes { get; private set; }

        /// <summary>Let STING create missing custom-attribute definitions on a folder. Off by
        /// default: definitions are project-wide admin configuration.</summary>
        public bool DocsAttributesCreateMissing { get; private set; }

        /// <summary>The ACC custom-attribute names STING writes to ("docsAttributeNames").
        /// Defaults are the names docs/KUT_ACC_DAY1_PLAYBOOK.md §3.4 tells the admin to create.</summary>
        public AccAttributeNames DocsAttributeNames { get; private set; } = AccAttributeNames.Default;

        /// <summary>"fileNamingFields": 7 = the ISO 19650 name WITHOUT suitability and revision
        /// (they travel as ACC attributes, and the ACC item keeps one name across revisions);
        /// 9 = the name carries "-{Suitability}-{Revision}". Null when the file does not say.</summary>
        public int? FileNamingFields { get; private set; }

        /// <summary>True when exports for this project should use the 7-field name and an ACC
        /// upload must refuse a name that embeds suitability/revision: said explicitly
        /// (fileNamingFields 7), or — with the setting absent — whenever the project's ACC
        /// settings are configured (a project, a folder or CDE folders), because an ACC item is
        /// matched by file name and a revision-bearing name makes every revision a new item.</summary>
        public bool SevenFieldNaming =>
            FileNamingFields == 7 ||
            (FileNamingFields == null && Source == AccPolicySource.Loaded &&
             (ProjectId.Length > 0 || FolderUrn.Length > 0 || CdeFolders.Count > 0));

        /// <summary>Due date for an escalated clash issue, in days from today; null = none.</summary>
        public int? EscalateDueDays { get; private set; }

        /// <summary>Who an escalated clash issue is assigned to (an ACC user, company or role
        /// id), and which of those it is. Both or neither.</summary>
        public string EscalateAssignedTo { get; private set; } = string.Empty;
        public string EscalateAssignedToType { get; private set; } = string.Empty;

        /// <summary>Put a planscape://revit/select link in each escalated clash issue (the
        /// objects' Revit UniqueIds, resolved through Model Derivative). Default on.</summary>
        public bool IssueDeepLinks { get; private set; } = true;

        /// <summary>Put ACC's own viewer link for each model version in the issue. Default on.</summary>
        public bool IssueViewerLinks { get; private set; } = true;

        /// <summary>Attach a one-topic BCF 2.1 file naming the two elements. Default on.</summary>
        public bool IssueBcfAttachment { get; private set; } = true;

        /// <summary>ACC clash statuses that are never escalated.</summary>
        public IReadOnlyCollection<string> EscalateExcludeStatuses { get; private set; } = DefaultExcludedClashStatuses;

        public static readonly IReadOnlyCollection<string> DefaultExcludedClashStatuses =
            new[] { "closed", "resolved", "approved", "not_an_issue" };

        /// <summary>The four ISO 19650 CDE states, the only keys cdeFolders accepts.</summary>
        public static readonly IReadOnlyCollection<string> CdeStates = new[] { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" };

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
            string projectId = string.Empty, coordContainerId = string.Empty;
            int? maxCount = null;
            double? minScore = null;
            string hubId = string.Empty, folderUrn = string.Empty, issueTypeId = string.Empty,
                   issueSubtypeId = string.Empty, region = string.Empty, assignedTo = string.Empty,
                   assignedToType = string.Empty;
            double? distToMm = null;
            int? dueDays = null;
            bool docsAttributes = false, docsCreate = false;
            bool deepLinks = true, viewerLinks = true, bcfAttachment = true;
            var cdeFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var disciplineMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> excludeStatuses = null;
            AccAttributeNames attrNames = AccAttributeNames.Default;
            int? namingFields = null;

            try
            {
                if (TryGet(o, "docsAttributeNames", out var anTok))
                    attrNames = AccAttributeNames.FromSettings(RequireStringMap(anTok, "docsAttributeNames"));
                if (TryGet(o, "fileNamingFields", out var nfTok))
                {
                    namingFields = RequireInt(nfTok, "fileNamingFields");
                    if (namingFields != 7 && namingFields != 9)
                        throw new FormatException($"'fileNamingFields' must be 7 (ISO 19650 name only) or 9 (name + suitability + revision), found {namingFields}");
                }
                if (TryGet(o, "unattended", out var uTok)) unattended = RequireBool(uTok, "unattended");
                if (TryGet(o, "coordModelSetId", out var idTok)) modelSetId = RequireString(idTok, "coordModelSetId");
                if (TryGet(o, "coordModelSetName", out var nmTok)) modelSetName = RequireString(nmTok, "coordModelSetName");
                if (TryGet(o, "publishSuitability", out var suTok)) suitability = RequireString(suTok, "publishSuitability");
                if (TryGet(o, "escalateMaxCount", out var mcTok)) maxCount = RequireInt(mcTok, "escalateMaxCount");
                if (TryGet(o, "escalateMinScore", out var msTok)) minScore = RequireDouble(msTok, "escalateMinScore");
                if (TryGet(o, "projectId", out var pjTok)) projectId = RequireString(pjTok, "projectId");
                if (TryGet(o, "coordContainerId", out var ccTok)) coordContainerId = RequireString(ccTok, "coordContainerId");
                if (TryGet(o, "hubId", out var hTok)) hubId = RequireString(hTok, "hubId");
                if (TryGet(o, "folderUrn", out var fTok)) folderUrn = RequireString(fTok, "folderUrn");
                if (TryGet(o, "distToMm", out var dTok))
                {
                    distToMm = RequireDouble(dTok, "distToMm");
                    if (distToMm <= 0) throw new FormatException($"'distToMm' must be greater than 0, found {distToMm}");
                }
                if (TryGet(o, "issueTypeId", out var itTok)) issueTypeId = RequireString(itTok, "issueTypeId");
                if (TryGet(o, "issueSubtypeId", out var isTok)) issueSubtypeId = RequireString(isTok, "issueSubtypeId");
                if (TryGet(o, "region", out var rTok))
                {
                    region = RequireString(rTok, "region").Trim().ToUpperInvariant();
                    if (region.Length > 0 && !AccIds.KnownRegions.Contains(region))
                        throw new FormatException($"'region' must be one of {string.Join(", ", AccIds.KnownRegions)}, found '{region}'");
                }
                if (TryGet(o, "cdeFolders", out var cfTok))
                {
                    cdeFolders = RequireStringMap(cfTok, "cdeFolders");
                    var badState = cdeFolders.Keys.FirstOrDefault(k => !CdeStates.Contains(k, StringComparer.OrdinalIgnoreCase));
                    if (badState != null)
                        throw new FormatException($"'cdeFolders' key '{badState}' is not a CDE state ({string.Join(", ", CdeStates)})");
                }
                if (TryGet(o, "disciplineMap", out var dmTok)) disciplineMap = RequireStringMap(dmTok, "disciplineMap");
                if (TryGet(o, "docsAttributes", out var daTok)) docsAttributes = RequireBool(daTok, "docsAttributes");
                if (TryGet(o, "docsAttributesCreateMissing", out var dcTok)) docsCreate = RequireBool(dcTok, "docsAttributesCreateMissing");
                if (TryGet(o, "issueDeepLinks", out var dlTok)) deepLinks = RequireBool(dlTok, "issueDeepLinks");
                if (TryGet(o, "issueViewerLinks", out var vlTok)) viewerLinks = RequireBool(vlTok, "issueViewerLinks");
                if (TryGet(o, "issueBcfAttachment", out var baTok)) bcfAttachment = RequireBool(baTok, "issueBcfAttachment");
                if (TryGet(o, "escalateDueDays", out var ddTok))
                {
                    dueDays = RequireInt(ddTok, "escalateDueDays");
                    if (dueDays < 0) throw new FormatException($"'escalateDueDays' must not be negative, found {dueDays}");
                }
                if (TryGet(o, "escalateAssignedTo", out var atTok)) assignedTo = RequireString(atTok, "escalateAssignedTo").Trim();
                if (TryGet(o, "escalateAssignedToType", out var attTok))
                {
                    assignedToType = RequireString(attTok, "escalateAssignedToType").Trim().ToLowerInvariant();
                    if (assignedToType.Length > 0 && assignedToType != "user" && assignedToType != "company" && assignedToType != "role")
                        throw new FormatException($"'escalateAssignedToType' must be user, company or role, found '{assignedToType}'");
                }
                if ((assignedTo.Length > 0) != (assignedToType.Length > 0))
                    throw new FormatException("'escalateAssignedTo' and 'escalateAssignedToType' must be set together - " +
                                              "an assignee id means nothing without saying whether it is a user, company or role");
                if (TryGet(o, "escalateExcludeStatuses", out var exTok))
                {
                    if (exTok.Type != JTokenType.Array)
                        throw new FormatException($"'escalateExcludeStatuses' must be a list of strings, found {exTok.Type}");
                    excludeStatuses = new List<string>();
                    foreach (var t in exTok)
                    {
                        if (t.Type != JTokenType.String)
                            throw new FormatException("'escalateExcludeStatuses' must contain only strings");
                        excludeStatuses.Add(((string)t ?? string.Empty).Trim().ToLowerInvariant());
                    }
                }
            }
            catch (FormatException ex) { return Malformed(policy, ex.Message); }

            policy.Source = AccPolicySource.Loaded;
            policy.MayPrompt = !unattended;
            policy.CoordModelSetId = modelSetId.Trim();
            policy.CoordModelSetName = modelSetName.Trim();
            policy.PublishSuitability = suitability.Trim();
            policy.ProjectId = projectId.Trim();
            policy.CoordContainerId = coordContainerId.Trim();
            policy.Escalation = BuildEscalation(maxCount, minScore);
            policy.HubId = hubId.Trim();
            policy.FolderUrn = folderUrn.Trim();
            policy.DistToMm = distToMm;
            policy.IssueTypeId = issueTypeId.Trim();
            policy.IssueSubtypeId = issueSubtypeId.Trim();
            policy.Region = region;
            policy.CdeFolders = cdeFolders;
            policy.DisciplineMap = disciplineMap;
            policy.DocsAttributes = docsAttributes;
            policy.DocsAttributesCreateMissing = docsCreate;
            policy.IssueDeepLinks = deepLinks;
            policy.IssueViewerLinks = viewerLinks;
            policy.IssueBcfAttachment = bcfAttachment;
            policy.EscalateDueDays = dueDays;
            policy.EscalateAssignedTo = assignedTo;
            policy.EscalateAssignedToType = assignedToType;
            if (excludeStatuses != null) policy.EscalateExcludeStatuses = excludeStatuses;
            policy.DocsAttributeNames = attrNames;
            policy.FileNamingFields = namingFields;
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

        private static Dictionary<string, string> RequireStringMap(JToken t, string key)
        {
            if (!(t is JObject obj)) throw new FormatException($"'{key}' must be an object of strings, found {t.Type}");
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in obj.Properties())
            {
                if (p.Value.Type != JTokenType.String)
                    throw new FormatException($"'{key}.{p.Name}' must be a string, found {p.Value.Type}");
                string v = ((string)p.Value ?? string.Empty).Trim();
                if (v.Length == 0) throw new FormatException($"'{key}.{p.Name}' is empty");
                map[p.Name.Trim()] = v;
            }
            return map;
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
