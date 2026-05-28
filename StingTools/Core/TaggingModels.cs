using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// Tagging model/POCO types relocated out of the oversized TagConfig.cs.
// Same namespace (StingTools.Core) — transparent to all callers.

namespace StingTools.Core
{
    /// <summary>
    /// GAP-FIX: Per-discipline tagging profile. Allows each discipline to have different
    /// collision handling, SEQ scheme, and default token values.
    /// Loaded from DISCIPLINE_PROFILES in project_config.json.
    /// </summary>
    public class DisciplineProfile
    {
        // ── v1 properties (collision/SEQ/token defaults) ──

        /// <summary>Collision mode override for this discipline (Skip/Overwrite/AutoIncrement). Null = use global.</summary>
        public TagCollisionMode? CollisionMode { get; set; }

        /// <summary>SEQ scheme override (Numeric/Alpha/ZonePrefix/DiscPrefix). Null = use global.</summary>
        public SeqScheme? SeqScheme { get; set; }

        /// <summary>Default ZONE code for this discipline. Null = use auto-detect.</summary>
        public string DefaultZone { get; set; }

        /// <summary>Default LOC code for this discipline. Null = use auto-detect.</summary>
        public string DefaultLoc { get; set; }

        /// <summary>Whether to include zone in SEQ key for this discipline. Null = use global.</summary>
        public bool? SeqIncludeZone { get; set; }

        /// <summary>Custom SEQ pad width for this discipline (e.g., 3 for 001, 5 for 00001). Null = use global.</summary>
        public int? SeqPadWidth { get; set; }

        // ── v2 properties (validation constraints) ──

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

        /// <summary>Parse a DisciplineProfile from a JSON dictionary.</summary>
        public static DisciplineProfile FromDict(Dictionary<string, object> dict)
        {
            var p = new DisciplineProfile();
            if (dict == null) return p;

            if (dict.TryGetValue("collision_mode", out object cm) && cm is string cms)
            {
                if (Enum.TryParse<TagCollisionMode>(cms, true, out var parsed)) p.CollisionMode = parsed;
            }
            if (dict.TryGetValue("seq_scheme", out object ss) && ss is string sss)
            {
                if (Enum.TryParse<SeqScheme>(sss, true, out var parsed)) p.SeqScheme = parsed;
            }
            if (dict.TryGetValue("default_zone", out object dz) && dz is string dzs && !string.IsNullOrWhiteSpace(dzs))
                p.DefaultZone = dzs;
            if (dict.TryGetValue("default_loc", out object dl) && dl is string dls && !string.IsNullOrWhiteSpace(dls))
                p.DefaultLoc = dls;
            if (dict.TryGetValue("default_status", out object ds) && ds is string dss && !string.IsNullOrWhiteSpace(dss))
                p.DefaultStatus = dss;
            if (dict.TryGetValue("seq_include_zone", out object siz))
            {
                if (siz is bool b) p.SeqIncludeZone = b;
                else if (siz is string sizs) p.SeqIncludeZone = sizs.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            if (dict.TryGetValue("seq_pad_width", out object spw))
            {
                if (spw is long l) p.SeqPadWidth = (int)l;
                else if (int.TryParse(spw?.ToString(), out int iv)) p.SeqPadWidth = iv;
            }
            // v2 properties from dict
            if (dict.TryGetValue("default_disc", out object dd) && dd is string dds && !string.IsNullOrWhiteSpace(dds))
                p.DefaultDisc = dds;
            if (dict.TryGetValue("default_prod", out object dprod) && dprod is string dprods && !string.IsNullOrWhiteSpace(dprods))
                p.DefaultProd = dprods;
            if (dict.TryGetValue("validation_strictness", out object vs))
            {
                if (vs is bool vsb) p.ValidationStrictness = vsb;
                else if (vs is string vss) p.ValidationStrictness = vss.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            if (dict.TryGetValue("default_paragraph_depth", out object dpd))
            {
                if (dpd is long dpdl && dpdl >= 1 && dpdl <= 10) p.DefaultParagraphDepth = (int)dpdl;
                else if (int.TryParse(dpd?.ToString(), out int dpdi) && dpdi >= 1 && dpdi <= 10)
                    p.DefaultParagraphDepth = dpdi;
            }
            return p;
        }
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

        /// <summary>PERF-02: Inline count of elements with empty FUNC after pipeline.</summary>
        public int EmptyFuncCount { get; private set; }
        /// <summary>PERF-02: Inline count of elements with empty PROD after pipeline.</summary>
        public int EmptyProdCount { get; private set; }
        /// <summary>PERF-R13: Count of elements that defaulted to LOC=BLD1 (throttled from per-element warnings).</summary>
        public int DefaultLocCount { get; set; }
        /// <summary>PERF-R13: Count of elements that defaulted to ZONE=Z01 (throttled from per-element warnings).</summary>
        public int DefaultZoneCount { get; set; }

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
