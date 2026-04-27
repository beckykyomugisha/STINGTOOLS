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
                    // PERF-07: cheap startswith filter before the regex.
                    if (!name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var m = _pattern.Match(name);
                    if (!m.Success)
                    {
                        // ACC-02: surface the rejection so the operator can
                        // fix typos like "STING::arch plan" → "STING::arch-plan".
                        warnings.Add(new NameWarning
                        {
                            ScopeBoxId = el.Id,
                            Name       = name,
                            Reason     = "name has STING:: prefix but does not match "
                                       + "STING::<id>[::<level>][::<tag>] (allowed chars: A-Z 0-9 . _ -)",
                        });
                        continue;
                    }
                    results.Add(new ScopeBoxBinding
                    {
                        ScopeBox = el,
                        DrawingTypeId = m.Groups[1].Value,
                        LevelCode = m.Groups[2].Success ? m.Groups[2].Value : null,
                        Tag       = m.Groups[3].Success ? m.Groups[3].Value : null,
                    });
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ScopeBoxBinder.ScanProject: {ex.Message}");
            }
            return results;
        }

        /// <summary>
        /// Find the existing view (if any) that was previously created
        /// by this binding — same DrawingType stamp + same scope-box
        /// assigned. Lets the command be idempotent.
        /// </summary>
        public static View FindExistingView(Document doc, ScopeBoxBinding b)
        {
            if (doc == null || b == null) return null;
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
