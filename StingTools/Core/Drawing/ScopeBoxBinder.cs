using StingTools.Core;
// StingTools — Drawing Template Manager · Week 5
//
// ScopeBoxBinder parses a "magic name" convention on scope boxes
// to auto-bind them to a DrawingType. Pattern:
//
//   STING::<drawing-type-id>::<level-code?>::<tag?>
//
// Examples:
//   STING::arch-plan-A1-1to100::L02
//   STING::pipe-spool-A1-1to50::L01::HWS
//   STING::mep-coord-A1-1to50
//
// Effect: a single command (GenerateFromScopeBoxes) walks every
// scope box in the project, parses the name, creates a view for
// each match using the bound DrawingType + the scope box as the
// view's crop region, and places it on a sheet. The level-code
// optional; when present it filters which Level the plan uses as
// its associated level. The tag optional — free-form, stored on
// the view so downstream automation can group / filter.
//
// Idempotent: re-running does not create duplicates — it looks up
// existing views stamped with the same (dt.Id, scopeBox.Name) pair
// and re-applies the profile instead.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public sealed class ScopeBoxBinding
    {
        public Element ScopeBox { get; set; }
        public string DrawingTypeId { get; set; }
        public string LevelCode { get; set; }  // optional
        public string Tag { get; set; }         // optional free-form
    }

    public static class ScopeBoxBinder
    {
        // PERF-07: a strict prefix the collector can pre-filter on so
        // non-STING scope boxes never reach the regex.
        private const string NamePrefix = "STING::";

        // ACC-02: the only legal characters inside a token segment are
        // alphanumerics + dot + hyphen + underscore. Anything else is a
        // typo or a manual rename that doesn't survive the parser.
        private static readonly Regex _pattern =
            new Regex(@"^STING::([A-Za-z0-9_\-\.]+)(?:::([A-Za-z0-9_\-\.]+))?(?:::([A-Za-z0-9_\-\.]+))?$",
                      RegexOptions.Compiled);

        /// <summary>
        /// ACC-02: a scope-box name beginning with STING:: that fails the
        /// strict pattern is reported back to callers so the user can
        /// rename rather than seeing the box silently dropped from the
        /// generation list.
        /// </summary>
        public sealed class NameWarning
        {
            public ElementId ScopeBoxId { get; set; }
            public string    Name { get; set; }
            public string    Reason { get; set; }
        }

        /// <summary>
        /// The canonical rejection reason produced when a name carries the
        /// STING:: prefix but fails the strict pattern. Exposed so the
        /// Scope Box Manager renders the same wording the scan warnings do.
        /// </summary>
        public const string PatternReason =
            "name has STING:: prefix but does not match "
          + "STING::<id>[::<level>][::<tag>] (allowed chars: A-Z 0-9 . _ -)";

        /// <summary>The literal prefix every bindable scope-box name starts with.</summary>
        public static string Prefix => NamePrefix;

        /// <summary>
        /// True when <paramref name="segment"/> is legal inside one "::"-delimited
        /// token of the grammar. Lets a UI validate a drawing-type id, level code
        /// or tag as it is typed, without assembling a whole candidate name.
        /// </summary>
        public static bool IsValidSegment(string segment)
            => !string.IsNullOrEmpty(segment) && _segment.IsMatch(segment);

        private static readonly Regex _segment =
            new Regex(@"^[A-Za-z0-9_\-\.]+$", RegexOptions.Compiled);

        /// <summary>
        /// The single public entry point for "is this scope-box name legal?".
        /// Deliberately the ONLY parser: <see cref="ScanProject"/> and the
        /// batch-produce command both route through it, so the grammar lives
        /// in exactly one regex.
        ///
        /// Returns false in two distinct situations, told apart by
        /// <paramref name="reason"/>:
        ///   • reason == null  — the name is simply not a STING box (no prefix).
        ///                       Not an error; nothing to fix.
        ///   • reason != null  — the name claims to be a STING box and is
        ///                       malformed. Surface it to the operator.
        ///
        /// <paramref name="binding"/>.ScopeBox is left null — callers that
        /// have the Element assign it themselves.
        /// </summary>
        public static bool TryParseName(string name, out ScopeBoxBinding binding, out string reason)
        {
            binding = null;
            reason  = null;
            if (string.IsNullOrWhiteSpace(name)) return false;

            // PERF-07: cheap startswith filter before the regex.
            if (!name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase)) return false;

            var m = _pattern.Match(name);
            if (!m.Success)
            {
                // ACC-02: surface the rejection so the operator can
                // fix typos like "STING::arch plan" → "STING::arch-plan".
                reason = PatternReason;
                return false;
            }

            binding = new ScopeBoxBinding
            {
                DrawingTypeId = m.Groups[1].Value,
                LevelCode     = m.Groups[2].Success ? m.Groups[2].Value : null,
                Tag           = m.Groups[3].Success ? m.Groups[3].Value : null,
            };
            return true;
        }

        /// <summary>
        /// Compose a name that <see cref="TryParseName"/> will accept, from
        /// already-validated segments. Optional segments are dropped rather
        /// than emitted empty — "STING::id::::tag" is not legal grammar, so a
        /// blank level with a non-blank tag has no representation and the tag
        /// is dropped too (the caller should have flagged that as invalid).
        /// </summary>
        public static string ComposeName(string drawingTypeId, string levelCode, string tag)
        {
            if (string.IsNullOrWhiteSpace(drawingTypeId)) return string.Empty;
            var sb = new System.Text.StringBuilder(NamePrefix).Append(drawingTypeId.Trim());
            if (!string.IsNullOrWhiteSpace(levelCode))
            {
                sb.Append("::").Append(levelCode.Trim());
                if (!string.IsNullOrWhiteSpace(tag)) sb.Append("::").Append(tag.Trim());
            }
            return sb.ToString();
        }

        public static List<ScopeBoxBinding> ScanProject(Document doc)
            => ScanProject(doc, out _);

        public static List<ScopeBoxBinding> ScanProject(Document doc, out List<NameWarning> warnings)
        {
            var results = new List<ScopeBoxBinding>();
            warnings = new List<NameWarning>();
            if (doc == null) return results;

            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType())
                {
                    var name = el.Name ?? "";
                    if (!TryParseName(name, out var binding, out var reason))
                    {
                        // reason == null means "not a STING box at all" — skip
                        // silently. A non-null reason is a fixable typo and is
                        // reported so the box is not dropped without a word.
                        if (reason != null)
                            warnings.Add(new NameWarning
                            {
                                ScopeBoxId = el.Id,
                                Name       = name,
                                Reason     = reason,
                            });
                        continue;
                    }
                    binding.ScopeBox = el;
                    results.Add(binding);
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ScopeBoxBinder.ScanProject: {ex.Message}");
            }
            return results;
        }

        // GAP-F: per-Scan cache built lazily — ScanProject runs once at
        // command entry, then FindExistingView is called per binding;
        // building the index once is O(views) total instead of
        // O(views × bindings).
        [ThreadStatic] private static Dictionary<(string dtId, long sbId), ElementId> _existingByBinding;

        public static void PrimeExistingViewIndex(Document doc)
        {
            if (doc == null) { _existingByBinding = null; return; }
            var idx = new Dictionary<(string, long), ElementId>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                {
                    if (!(el is View v) || v.IsTemplate) continue;
                    var dtId = DrawingTypeStamper.Read(v);
                    if (string.IsNullOrEmpty(dtId)) continue;
                    var sbParam = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (sbParam == null) continue;
                    var sbId = sbParam.AsElementId();
                    if (sbId == null || sbId == ElementId.InvalidElementId) continue;
                    idx[(dtId, sbId.Value)] = v.Id;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ScopeBoxBinder.PrimeExistingViewIndex: {ex.Message}");
            }
            _existingByBinding = idx;
        }

        /// <summary>
        /// Find the existing view (if any) that was previously created
        /// by this binding — same DrawingType stamp + same scope-box
        /// assigned. Lets the command be idempotent.
        /// </summary>
        public static View FindExistingView(Document doc, ScopeBoxBinding b)
        {
            if (doc == null || b == null || b.ScopeBox == null) return null;
            // GAP-F: short-circuit via thread-local index when primed.
            if (_existingByBinding != null
                && _existingByBinding.TryGetValue((b.DrawingTypeId, b.ScopeBox.Id.Value), out var cachedId))
            {
                if (doc.GetElement(cachedId) is View vCached
                    && vCached.IsValidObject && !vCached.IsTemplate)
                    return vCached;
                // Stale entry — fall through to the slow path.
            }
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                {
                    if (!(el is View v) || v.IsTemplate) continue;
                    if (!string.Equals(DrawingTypeStamper.Read(v), b.DrawingTypeId, StringComparison.OrdinalIgnoreCase)) continue;
                    var sbParam = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (sbParam == null) continue;
                    if (sbParam.AsElementId() == b.ScopeBox.Id) return v;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ScopeBoxBinder.FindExistingView: {ex.Message}");
            }
            return null;
        }
    }
}
