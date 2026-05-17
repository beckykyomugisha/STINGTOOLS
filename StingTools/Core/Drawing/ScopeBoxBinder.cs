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
        private static readonly Regex _pattern =
            new Regex(@"^STING::([A-Za-z0-9_\-]+)(?:::([A-Za-z0-9_\-]+))?(?:::([A-Za-z0-9_\-]+))?$",
                      RegexOptions.Compiled);

        public static List<ScopeBoxBinding> ScanProject(Document doc)
        {
            var results = new List<ScopeBoxBinding>();
            if (doc == null) return results;

            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType())
                {
                    var name = el.Name ?? "";
                    var m = _pattern.Match(name);
                    if (!m.Success) continue;
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
