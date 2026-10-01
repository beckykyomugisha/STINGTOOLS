using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// Tagging model/POCO types relocated out of the oversized TagConfig.cs.
// Same namespace (StingTools.Core) — transparent to all callers.

namespace StingTools.Core
{
    /// <summary>
    /// GAP-FIX: Per-discipline tagging profile — collision handling, token defaults and
    /// validation constraints for one discipline.
    /// Loaded from DISCIPLINE_PROFILES in project_config.json.
    ///
    /// TAGACC-25: SeqScheme, SeqPadWidth, SeqIncludeZone, DefaultZone and DefaultLoc were
    /// removed. SEQ format is deliberately global (counter rebuild, MergeSeqSidecar,
    /// NormaliseHeldSeq and the validator via EffectiveSeqPad all assume one format), and
    /// LOC/ZONE fallbacks belong only in STING_TAG_TOKEN_POLICY.json. A project that still
    /// carries one of those keys is warned by the loader — see <see cref="DisciplineProfileKeys"/>.
    /// </summary>
    public class DisciplineProfile
    {
        /// <summary>
        /// Collision mode for this discipline (Skip/Overwrite/AutoIncrement). Null = use the
        /// global DEFAULT_COLLISION_MODE. An explicit choice the user made in a dialog always
        /// wins — see TagConfig.ResolveCollisionMode.
        /// </summary>
        public TagCollisionMode? CollisionMode { get; set; }

        /// <summary>Default DISC code for this profile (e.g., "M").</summary>
        public string DefaultDisc { get; set; }

        /// <summary>Allowed SYS codes for this discipline. Empty set means no restriction.</summary>
        public HashSet<string> AllowedSysCodes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Allowed FUNC codes for this discipline. Empty set means no restriction.</summary>
        public HashSet<string> AllowedFuncCodes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Default PROD code when family-aware detection yields a generic result.</summary>
        public string DefaultProd { get; set; }

        /// <summary>Default STATUS value for this discipline.</summary>
        public string DefaultStatus { get; set; }


        /// <summary>When true, SYS/FUNC must be in AllowedSysCodes/AllowedFuncCodes.</summary>
        public bool ValidationStrictness { get; set; }

        /// <summary>Tokens that must be non-empty for compliant tags (e.g., ["DISC","SYS","FUNC","PROD","SEQ"]).</summary>
        public HashSet<string> RequiredTokens { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Default paragraph depth (1-10) for this discipline's TAG7 tier
        /// visibility. Null = use the global ParaDepth value. Used by
        /// SetParagraphDepthCommand's "By discipline" scope and read by
        /// WriteTag7All when no per-element override is set.
        /// </summary>
        public int? DefaultParagraphDepth { get; set; }
    }

    /// <summary>TAGACC-25: what a key found inside one DISCIPLINE_PROFILES entry is.</summary>
    public enum DisciplineProfileKeyKind
    {
        /// <summary>Binds to a <see cref="DisciplineProfile"/> property (Newtonsoft matches case-insensitively).</summary>
        Known,
        /// <summary>A setting that was deliberately removed; it has a project-wide replacement.</summary>
        Retired,
        /// <summary>Binds to nothing — Newtonsoft drops it without a word.</summary>
        Unknown,
    }

    /// <summary>TAGACC-25: a per-discipline setting that was retired, and where its job lives now.</summary>
    public sealed class RetiredDisciplineProfileKey
    {
        public RetiredDisciplineProfileKey(string name, string snakeCase, string replacement)
        {
            Name = name; SnakeCase = snakeCase; Replacement = replacement;
        }

        /// <summary>The PascalCase spelling the DisciplineProfile property had.</summary>
        public string Name { get; }
        /// <summary>The snake_case spelling the removed FromDict parser accepted.</summary>
        public string SnakeCase { get; }
        /// <summary>The project-wide setting to use instead.</summary>
        public string Replacement { get; }
    }

    /// <summary>TAGACC-25: one key in a project's DISCIPLINE_PROFILES that is not applied.</summary>
    public sealed class DisciplineProfileKeyFinding
    {
        public string Discipline { get; set; }
        /// <summary>The key exactly as the project wrote it.</summary>
        public string Key { get; set; }
        public DisciplineProfileKeyKind Kind { get; set; }
        /// <summary>Retired: the replacement. Unknown: null.</summary>
        public string Replacement { get; set; }
        /// <summary>Unknown only: the property the key looks like a misspelling of (e.g. collision_mode → CollisionMode).</summary>
        public string DidYouMean { get; set; }

        public string Message
        {
            get
            {
                string where = $"DISCIPLINE_PROFILES.{Discipline}.{Key}";
                if (Kind == DisciplineProfileKeyKind.Retired)
                    return $"{where} is retired (TAGACC-25) and has no effect — use {Replacement}.";
                return DidYouMean != null
                    ? $"{where} is not a discipline-profile setting and has no effect — did you mean \"{DidYouMean}\"?"
                    : $"{where} is not a discipline-profile setting and has no effect.";
            }
        }
    }

    /// <summary>
    /// TAGACC-25: classifies the keys of a raw DISCIPLINE_PROFILES entry so a setting that is
    /// retired or misspelt is warned about instead of being dropped by the deserialiser in
    /// silence. Revit-free; unit-tested in StingTools.Tags.Tests.
    /// </summary>
    public static class DisciplineProfileKeys
    {
        /// <summary>
        /// The retired per-discipline settings. SEQ format must stay project-wide — the counter
        /// rebuild, MergeSeqSidecar, NormaliseHeldSeq and the validator (EffectiveSeqPad) all
        /// assume one format — and LOC/ZONE fallbacks belong only in the token policy.
        /// </summary>
        public static readonly IReadOnlyList<RetiredDisciplineProfileKey> Retired = new[]
        {
            new RetiredDisciplineProfileKey("SeqScheme", "seq_scheme",
                "the project-wide SEQ_SCHEME key in project_config.json"),
            new RetiredDisciplineProfileKey("SeqPadWidth", "seq_pad_width",
                "the project-wide TAG_FORMAT.num_pad in project_config.json"),
            new RetiredDisciplineProfileKey("SeqIncludeZone", "seq_include_zone",
                "the project-wide SEQ_INCLUDE_ZONE key in project_config.json"),
            new RetiredDisciplineProfileKey("DefaultZone", "default_zone",
                "the ZONE fallback in STING_TAG_TOKEN_POLICY.json (project override: _BIM_COORD/tag_token_policy.json)"),
            new RetiredDisciplineProfileKey("DefaultLoc", "default_loc",
                "the LOC fallback in STING_TAG_TOKEN_POLICY.json (project override: _BIM_COORD/tag_token_policy.json)"),
        };

        /// <summary>The settings a DISCIPLINE_PROFILES entry can carry — DisciplineProfile's public properties.</summary>
        public static IReadOnlyCollection<string> KnownSettings { get; } =
            typeof(DisciplineProfile).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Select(p => p.Name).ToList();

        /// <summary>The retired entry a key names, in either spelling and any case; null when it is not retired.</summary>
        public static RetiredDisciplineProfileKey FindRetired(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            string k = key.Trim();
            return Retired.FirstOrDefault(r =>
                string.Equals(r.Name, k, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.SnakeCase, k, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Known if it binds to a property, Retired if it names a retired setting, else Unknown.</summary>
        public static DisciplineProfileKeyKind Classify(string key)
        {
            if (FindRetired(key) != null) return DisciplineProfileKeyKind.Retired;
            if (!string.IsNullOrWhiteSpace(key)
                && KnownSettings.Any(n => string.Equals(n, key.Trim(), StringComparison.OrdinalIgnoreCase)))
                return DisciplineProfileKeyKind.Known;
            return DisciplineProfileKeyKind.Unknown;
        }

        /// <summary>
        /// Every key in one profile that is not applied — retired or unknown — in the order
        /// the project wrote them. Known keys produce nothing.
        /// </summary>
        public static List<DisciplineProfileKeyFinding> Inspect(string discipline, IEnumerable<string> keys)
        {
            var findings = new List<DisciplineProfileKeyFinding>();
            if (keys == null) return findings;
            foreach (string key in keys)
            {
                var kind = Classify(key);
                if (kind == DisciplineProfileKeyKind.Known) continue;
                var f = new DisciplineProfileKeyFinding { Discipline = discipline, Key = key, Kind = kind };
                if (kind == DisciplineProfileKeyKind.Retired)
                    f.Replacement = FindRetired(key).Replacement;
                else
                {
                    string squashed = (key ?? "").Replace("_", "").Replace("-", "").Trim();
                    f.DidYouMean = KnownSettings.FirstOrDefault(n =>
                        string.Equals(n, squashed, StringComparison.OrdinalIgnoreCase));
                }
                findings.Add(f);
            }
            return findings;
        }

        /// <summary>
        /// TAGACC-25 precedence for a per-discipline setting: an explicit choice (the user picked
        /// it in a dialog) &gt; the discipline profile &gt; the project-wide setting &gt; the
        /// built-in fallback. Generic so it carries no dependency on the setting's type.
        /// </summary>
        public static T ResolvePrecedence<T>(T? explicitChoice, T? profileValue, T? projectValue, T fallback)
            where T : struct
            => explicitChoice ?? profileValue ?? projectValue ?? fallback;
    }

    /// <summary>
    /// Tracks tagging operation statistics across a batch for rich post-operation reporting.
    /// Captures per-category counts, collision details, skipped elements, warnings, and
    /// discipline/system/level breakdowns. Thread-safe for single-transaction use.
    /// </summary>
    public class TaggingStats
    {
        public int TotalTagged { get; private set; }
        public int TotalSkipped { get; private set; }
        public int TotalOverwritten { get; private set; }
        public int TotalCollisions { get; private set; }
        public int MaxCollisionDepth { get; private set; }
        public readonly Dictionary<string, int> TaggedByCategory = new Dictionary<string, int>();
        public readonly Dictionary<string, int> TaggedByDisc = new Dictionary<string, int>();
        public readonly Dictionary<string, int> TaggedBySys = new Dictionary<string, int>();
        public readonly Dictionary<string, int> TaggedByLevel = new Dictionary<string, int>();
        public readonly Dictionary<string, int> SkippedByCategory = new Dictionary<string, int>();
        public readonly Dictionary<string, int> OverwrittenByCategory = new Dictionary<string, int>();
        public readonly Dictionary<string, int> OverwrittenByDisc = new Dictionary<string, int>();
        public readonly Dictionary<string, int> OverwrittenBySys = new Dictionary<string, int>();
        public readonly Dictionary<string, int> OverwrittenByLevel = new Dictionary<string, int>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<(string tag, int depth)> CollisionDetails = new List<(string, int)>();

        /// <summary>DTW-144 — the scope-box names the latest tagging population context found
        /// refused (SpatialAutoDetect.AuditScopeBoxNames sets it when the context is built,
        /// just before a tagging run creates its stats).</summary>
        public static IReadOnlyList<string> CurrentScopeBoxNameProblems { get; set; } = new List<string>();

        /// <summary>DTW-144 — scope boxes whose STING-LOC / ZONE / AREA name does not parse,
        /// "'name' — reason". Elements inside them took the fallback LOC / ZONE; the report
        /// names the boxes so the refusal is not only a log line.</summary>
        public readonly List<string> ScopeBoxNameProblems =
            new List<string>(CurrentScopeBoxNameProblems ?? new List<string>());

        /// <summary>PERF-02: Inline count of elements with empty FUNC after pipeline.</summary>
        public int EmptyFuncCount { get; private set; }
        /// <summary>PERF-02: Inline count of elements with empty PROD after pipeline.</summary>
        public int EmptyProdCount { get; private set; }
        /// <summary>PERF-R13: Count of elements that defaulted to LOC=BLD1 (throttled from per-element warnings).</summary>
        public int DefaultLocCount { get; set; }
        /// <summary>PERF-R13: Count of elements that defaulted to ZONE=Z01 (throttled from per-element warnings).</summary>
        public int DefaultZoneCount { get; set; }

        // ── G-42: tag completeness at WRITE time ────────────────────────────
        //
        // TagConfig.TagIsComplete already existed and was called from eight sites —
        // ComplianceScan, BOQSupportCommands, BIMManagerCommands. Every one is a
        // READER. None was in the write path, which is why a tag rendering two blank
        // segments reached a drawing with no warning at all.
        //
        // Counted here so the number appears beside TagsPlaced in the result dialog,
        // not only in the log. A count nobody sees is the same as no count.

        /// <summary>Tags written that failed TagConfig.TagIsComplete — a MANDATORY token was blank.</summary>
        public int IncompleteTagCount { get; private set; }

        /// <summary>
        /// Tags that are complete but reached completeness through a FALLBACK.
        /// G-27's distinction: complete-but-assumed is not the same as complete, and
        /// conflating them is what made a defaulted quantity read as a measured one.
        /// </summary>
        public int AssumedTokenTagCount { get; private set; }

        /// <summary>First few incomplete tags, for the result dialog. Bounded — a
        /// thousand-element batch should not build a thousand-line message.</summary>
        public readonly List<string> IncompleteSamples = new List<string>();

        public void RecordTagCompleteness(bool complete, bool anyFallback, string tag, long elementId)
        {
            if (!complete)
            {
                IncompleteTagCount++;
                if (IncompleteSamples.Count < 10)
                    IncompleteSamples.Add($"{tag}  (element {elementId})");
            }
            else if (anyFallback) AssumedTokenTagCount++;
        }

        // ── Per-token attribution (STING_TAG_TOKEN_POLICY.json) ─────────────
        //
        // AssumedTokenTagCount above says HOW MANY tags were assumed. It cannot say
        // WHICH token was assumed, so "312 assumed" gave no clue whether the project
        // is missing LOC codes or tagging un-catalogued families. These do.

        /// <summary>How many times each token was substituted from the policy's fallback.</summary>
        public readonly Dictionary<string, int> AssumedByToken =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Substitutions of a token the policy marks MANDATORY. Counted apart
        /// from DERIVED ones: falling back on SYS for an architectural element is a real
        /// answer, falling back on DISC is a project that has not been set up.</summary>
        public int MandatoryAssumedCount { get; private set; }

        /// <summary>Elements not tagged because a token was blank and the policy offers
        /// no fallback. These are REFUSALS, not failures — writing a tag with a bare
        /// separator would have been worse.</summary>
        public int RefusedTagCount { get; private set; }

        public readonly List<string> RefusedSamples = new List<string>();

        public void RecordTokenSubstitution(string token, bool mandatory)
        {
            if (string.IsNullOrEmpty(token)) return;
            AssumedByToken.TryGetValue(token, out int n);
            AssumedByToken[token] = n + 1;
            if (mandatory) MandatoryAssumedCount++;
        }

        public void RecordTokenRefusal(string reason, long elementId)
        {
            RefusedTagCount++;
            if (RefusedSamples.Count < 10)
                RefusedSamples.Add($"element {elementId}: {reason}");
        }

        /// <summary>PERF-02: Track empty FUNC/PROD inline during tagging loop to avoid post-loop re-scan.</summary>
        public void RecordEmptyTokens(string func, string prod)
        {
            if (string.IsNullOrEmpty(func)) EmptyFuncCount++;
            if (string.IsNullOrEmpty(prod)) EmptyProdCount++;
        }

        public void RecordTagged(string category, string disc, string sys, string lvl)
        {
            TotalTagged++;
            Increment(TaggedByCategory, category);
            if (!string.IsNullOrEmpty(disc)) Increment(TaggedByDisc, disc);
            if (!string.IsNullOrEmpty(sys)) Increment(TaggedBySys, sys);
            if (!string.IsNullOrEmpty(lvl)) Increment(TaggedByLevel, lvl);
        }

        public void RecordSkipped(string category)
        {
            TotalSkipped++;
            Increment(SkippedByCategory, category);
        }

        public void RecordOverwritten(string category, string disc = null, string sys = null, string lvl = null)
        {
            TotalOverwritten++;
            Increment(OverwrittenByCategory, category);
            if (!string.IsNullOrEmpty(disc)) Increment(OverwrittenByDisc, disc);
            if (!string.IsNullOrEmpty(sys)) Increment(OverwrittenBySys, sys);
            if (!string.IsNullOrEmpty(lvl)) Increment(OverwrittenByLevel, lvl);
        }

        public void RecordCollision(string tag, int depth)
        {
            TotalCollisions++;
            if (depth > MaxCollisionDepth) MaxCollisionDepth = depth;
            // Keep the top 20 collisions by depth (deepest = most concerning)
            if (CollisionDetails.Count < 20)
            {
                CollisionDetails.Add((tag, depth));
            }
            else
            {
                // Replace the shallowest collision if this one is deeper
                int minIdx = 0;
                for (int i = 1; i < CollisionDetails.Count; i++)
                    if (CollisionDetails[i].depth < CollisionDetails[minIdx].depth)
                        minIdx = i;
                if (depth > CollisionDetails[minIdx].depth)
                    CollisionDetails[minIdx] = (tag, depth);
            }
        }

        /// <summary>
        /// Elements whose token PARAMETERS could not be written even though the tag
        /// assembled correctly. The parameter is not reachable on the instance — bound
        /// to the TYPE, or not bound to the element's category. Counted rather than
        /// warned per element: at batch volume the per-row version is the noise that
        /// hid the one real fault in TAGLOG-1.
        /// </summary>
        public int TokenWriteFailureCount { get; private set; }

        /// <summary>Distinct token parameters that failed to write, with a count each.</summary>
        public readonly Dictionary<string, int> TokenWriteFailuresByParam =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Categories the failures fell in, with an element count each.</summary>
        public readonly Dictionary<string, int> TokenWriteFailuresByCategory =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// category -> (parameter -> element count). The pair is what an operator acts
        /// on: a binding is repaired for one parameter ON one category, so a report that
        /// names only the parameter still leaves them hunting for where.
        /// </summary>
        public readonly Dictionary<string, Dictionary<string, int>> TokenWriteFailureDetail =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        public void RecordTokenWriteFailure(long elementId, string categoryName, IEnumerable<string> paramNames)
        {
            TokenWriteFailureCount++;
            string cat = string.IsNullOrWhiteSpace(categoryName) ? "(unknown category)" : categoryName;
            Increment(TokenWriteFailuresByCategory, cat);
            if (paramNames == null) return;

            if (!TokenWriteFailureDetail.TryGetValue(cat, out var byParam))
            {
                byParam = new Dictionary<string, int>(StringComparer.Ordinal);
                TokenWriteFailureDetail[cat] = byParam;
            }
            foreach (string p in paramNames)
            {
                if (string.IsNullOrEmpty(p)) continue;
                Increment(TokenWriteFailuresByParam, p);
                Increment(byParam, p);
            }
        }

        /// <summary>
        /// Elements whose CONTAINER write threw. Distinct from a token write failing:
        /// the tag and its tokens are fine, but the 53 discipline containers assembled
        /// from them are not. Counted so it reaches the report instead of living only
        /// in the log, where 175 of them were sitting uninvestigated.
        /// </summary>
        public int ContainerWriteFailureCount { get; private set; }

        public readonly Dictionary<string, int> ContainerWriteFailuresByCategory =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>First distinct exception messages seen, for the report.</summary>
        public readonly List<string> ContainerWriteFailureSamples = new List<string>();

        public void RecordContainerWriteFailure(string categoryName, string message)
        {
            ContainerWriteFailureCount++;
            Increment(ContainerWriteFailuresByCategory,
                string.IsNullOrWhiteSpace(categoryName) ? "(unknown category)" : categoryName);
            if (string.IsNullOrEmpty(message)) return;
            if (ContainerWriteFailureSamples.Count < 3
                && !ContainerWriteFailureSamples.Contains(message))
                ContainerWriteFailureSamples.Add(message);
        }

        public void RecordWarning(string warning)
        {
            if (Warnings.Count < 100)
                Warnings.Add(warning);
        }

        /// <summary>Build a multi-line report string for TaskDialog display.</summary>
        public string BuildReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"  Tagged:       {TotalTagged:N0}");
            sb.AppendLine($"  Skipped:      {TotalSkipped:N0}");

            // G-42 — beside TagsPlaced, not buried in the log. An incomplete tag is a
            // defect; a complete-but-assumed one is a decision the operator should know
            // they are relying on (G-27's distinction, same vocabulary).
            if (IncompleteTagCount > 0)
            {
                sb.AppendLine($"  INCOMPLETE:   {IncompleteTagCount:N0} — a mandatory segment is blank");
                foreach (var sample in IncompleteSamples)
                    sb.AppendLine($"                  {sample}");
                if (IncompleteTagCount > IncompleteSamples.Count)
                    sb.AppendLine($"                  … +{IncompleteTagCount - IncompleteSamples.Count:N0} more");
            }
            if (ContainerWriteFailureCount > 0)
            {
                sb.AppendLine($"  CONTAINERS:   {ContainerWriteFailureCount:N0} element(s) threw while writing the discipline containers");
                foreach (var kv in ContainerWriteFailuresByCategory.OrderByDescending(k => k.Value))
                    sb.AppendLine($"                  {kv.Key} x {kv.Value:N0}");
                foreach (var m in ContainerWriteFailureSamples)
                    sb.AppendLine($"                  \"{m}\"");
                sb.AppendLine("                  The tag is written; the containers are not. Full stack trace");
                sb.AppendLine("                  is in the StingTools log.");
            }
            if (TokenWriteFailureCount > 0)
            {
                sb.AppendLine($"  NOT WRITTEN:  {TokenWriteFailureCount:N0} element(s) whose token PARAMETER could not be written");
                // Grouped by CATEGORY, because that is the unit of repair: a binding is
                // fixed for one parameter ON one category. Naming only the parameter
                // leaves the operator hunting for where, which is the same half-answer
                // the old "run FamilyStagePopulate" line gave.
                foreach (var cat in TokenWriteFailuresByCategory.OrderByDescending(c => c.Value))
                {
                    sb.AppendLine($"                  {cat.Key} — {cat.Value:N0} element(s)");
                    if (!TokenWriteFailureDetail.TryGetValue(cat.Key, out var byParam)) continue;
                    foreach (var kv in byParam.OrderByDescending(k => k.Value))
                        sb.AppendLine($"                    {kv.Key} × {kv.Value:N0}");
                }
                sb.AppendLine("                  A tag with a blank segment is refused, never written. Fix per category in");
                sb.AppendLine("                  Manage > Project Parameters: each must be an INSTANCE parameter");
                sb.AppendLine("                  and must include that category.");
            }
            if (RefusedTagCount > 0)
            {
                sb.AppendLine($"  REFUSED:      {RefusedTagCount:N0} — a token was blank and STING_TAG_TOKEN_POLICY.json gives it no fallback");
                foreach (var sample in RefusedSamples)
                    sb.AppendLine($"                  {sample}");
                if (RefusedTagCount > RefusedSamples.Count)
                    sb.AppendLine($"                  … +{RefusedTagCount - RefusedSamples.Count:N0} more");
            }
            if (ScopeBoxNameProblems.Count > 0)
            {
                sb.AppendLine($"  SCOPE BOXES:  {ScopeBoxNameProblems.Count:N0} STING-LOC / ZONE / AREA box name(s) do not parse — elements inside took the fallback LOC / ZONE");
                foreach (var p in ScopeBoxNameProblems.Take(5))
                    sb.AppendLine($"                  {p}");
                if (ScopeBoxNameProblems.Count > 5)
                    sb.AppendLine($"                  … +{ScopeBoxNameProblems.Count - 5:N0} more (Drawing Doctor lists them all)");
                sb.AppendLine("                  Rename them (e.g. STING-LOC::BLOCK-A) and re-tag.");
            }
            if (AssumedTokenTagCount > 0)
            {
                sb.AppendLine($"  assumed:      {AssumedTokenTagCount:N0} complete, but one or more segments fell back to a default");
                if (AssumedByToken.Count > 0)
                {
                    // Which token, not just how many — the number is only actionable if you
                    // know whether to go and declare LOC codes or catalogue a family.
                    var byToken = AssumedByToken.OrderByDescending(kv => kv.Value)
                                                .Select(kv => $"{kv.Key} ×{kv.Value:N0}");
                    sb.AppendLine($"                  {string.Join(", ", byToken)}");
                }
                if (MandatoryAssumedCount > 0)
                    sb.AppendLine($"                  {MandatoryAssumedCount:N0} of these assumed a MANDATORY token — check the project setup");
            }
            if (TotalOverwritten > 0)
                sb.AppendLine($"  Overwritten:  {TotalOverwritten:N0}");
            if (TotalCollisions > 0)
            {
                sb.AppendLine($"  Collisions:   {TotalCollisions:N0} resolved (max depth: {MaxCollisionDepth})");
                // Show top 5 deepest collisions (most concerning)
                foreach (var (tag, depth) in CollisionDetails.OrderByDescending(c => c.depth).Take(5))
                    sb.AppendLine($"    • {tag} (bumped {depth}×)");
                if (TotalCollisions > 5)
                    sb.AppendLine($"    ... and {TotalCollisions - 5} more");
            }

            if (TaggedByDisc.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  By Discipline:");
                foreach (var kvp in TaggedByDisc.OrderByDescending(x => x.Value))
                    sb.AppendLine($"    {kvp.Key,-6} {kvp.Value,5}");
            }
            if (TaggedBySys.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  By System:");
                foreach (var kvp in TaggedBySys.OrderByDescending(x => x.Value).Take(8))
                    sb.AppendLine($"    {kvp.Key,-8} {kvp.Value,5}");
            }
            if (TaggedByLevel.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  By Level:");
                foreach (var kvp in TaggedByLevel.OrderBy(x => x.Key))
                    sb.AppendLine($"    {kvp.Key,-6} {kvp.Value,5}");
            }
            if (TotalOverwritten > 0 && OverwrittenByDisc.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Overwritten By Discipline:");
                foreach (var kvp in OverwrittenByDisc.OrderByDescending(x => x.Value))
                    sb.AppendLine($"    {kvp.Key,-6} {kvp.Value,5}");
            }
            if (Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"  Warnings ({Warnings.Count}):");
                foreach (string w in Warnings.Take(10))
                    sb.AppendLine($"    ⚠ {w}");
                if (Warnings.Count > 10)
                    sb.AppendLine($"    ... and {Warnings.Count - 10} more");
            }

            return sb.ToString();
        }

        private static void Increment(Dictionary<string, int> dict, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            dict.TryGetValue(key, out int count);
            dict[key] = count + 1;
        }
    }

    /// <summary>Categorizes ISO 19650 validation errors for separate counting.</summary>
    public enum ValidationErrorType
    {
        /// <summary>Token value does not match allowed code list (e.g., DISC 'X' not valid).</summary>
        TokenFormat,
        /// <summary>Token value is empty/missing.</summary>
        TokenEmpty,
        /// <summary>Cross-validation mismatch between related tokens or element category.</summary>
        CrossValidation
    }

    /// <summary>Structured validation error with message and categorized type.</summary>
    public class ValidationError
    {
        public string Message { get; set; }
        public ValidationErrorType Type { get; set; }

        public ValidationError(string message, ValidationErrorType type)
        {
            Message = message;
            Type = type;
        }

        public override string ToString() => Message;
    }
}
