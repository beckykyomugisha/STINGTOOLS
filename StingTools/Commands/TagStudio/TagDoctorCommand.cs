// TagDoctorCommand — why is this tag showing one line?
//
// WHY THIS EXISTS
//
// A tag row renders nothing when ANY link in a four-link chain is missing, and
// all four failures look identical on a drawing: a blank line.
//
//   1. the tier gate parameter is not bound to the host at all
//   2. it is bound but false
//   3. a per-category depth override caps the tier below this one
//   4. the gate is open and the VALUE parameter is empty
//
// Diagnosing that by hand means three dialogs and a guess. On 2026-09-23 a duct
// tag showed one line while TAG_PARA_STATE_1..10_BOOL were all ticked on the
// tag TYPE and ASS_TAG_2_TXT held "M-DU-0005" on the element - two of the four
// links visibly fine, and no way to see the other two.
//
// It also answers the question the propagation test has carried as OPEN since
// 2026-09-17: the gates exist on BOTH the tag type and the host, and nobody
// established which copy the label reads. This prints both, side by side, so
// the answer is read off rather than argued about.
//
// Read-only. It writes nothing, so it can be run on anything at any time.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.TagStudio
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagDoctorCommand : IExternalCommand
    {
        private const int MaxTier = 10;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("Tag Doctor", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var picked = new List<Element>();
            try
            {
                foreach (ElementId id in ctx.UIDoc.Selection.GetElementIds())
                {
                    Element e = doc.GetElement(id);
                    if (e != null && !(e is ElementType)) picked.Add(e);
                }
            }
            catch (Exception ex) { StingLog.Warn($"TagDoctor: reading selection: {ex.Message}"); }

            if (picked.Count == 0)
            {
                TaskDialog.Show("Tag Doctor",
                    "Select the ELEMENT (or its tag) and run again.\n\n" +
                    "It reports, per tier: whether the gate is bound, whether it is on, " +
                    "whether a category cap blocks it, and whether the value is there — " +
                    "so a blank row says which of the four it is.");
                return Result.Cancelled;
            }

            var sb = new StringBuilder();
            int reported = 0;

            foreach (Element sel in picked.Take(3))     // three is plenty to see a pattern
            {
                Element host = sel;
                IndependentTag tagInst = sel as IndependentTag;
                if (tagInst != null)
                {
                    // Selected the TAG - report on what it points at.
                    host = TaggedHost(doc, tagInst);
                    if (host == null)
                    {
                        sb.AppendLine("A tag was selected but its host could not be resolved; "
                                      + "select the element instead.").AppendLine();
                        continue;
                    }
                }
                else
                {
                    tagInst = FindTagFor(doc, sel);     // may legitimately be null
                }

                Report(doc, host, tagInst, sb);
                sb.AppendLine();
                reported++;
            }

            if (reported == 0)
            {
                TaskDialog.Show("Tag Doctor", "Nothing diagnosable in the selection.");
                return Result.Succeeded;
            }

            StingLog.Info("TagDoctor:\n" + sb);
            var panel = StingResultPanel.Create("Tag Doctor")
                .SetSubtitle("Why a tag row does or does not draw, per tier");
            panel.AddSection("DIAGNOSIS");
            // Split on the newline CHARACTER and trim any carriage return.
            // Writing the two-string overload here means embedding escapes
            // that shell heredocs keep eating.
            foreach (string line in sb.ToString().Split('\n'))
                panel.Text(line.TrimEnd('\r'));
            panel.Show();
            return Result.Succeeded;
        }

        // ── the report ───────────────────────────────────────────────────────

        private static void Report(Document doc, Element host, IndependentTag tag, StringBuilder sb)
        {
            ElementType hostType = doc.GetElement(host.GetTypeId()) as ElementType;
            string cat = host.Category?.Name ?? "(no category)";

            sb.AppendLine("ELEMENT  " + Describe(host) + "   [" + cat + "]");
            sb.AppendLine("TYPE     " + (hostType != null ? hostType.Name : "(none)"));

            if (tag == null)
            {
                sb.AppendLine("TAG      none found in this view — the element is untagged, so nothing can render.");
                return;
            }

            var tagType = doc.GetElement(tag.GetTypeId()) as ElementType;
            sb.AppendLine("TAG      " + (tagType != null ? tagType.FamilyName + " / " + tagType.Name : "(unknown)"));

            // The category cap, which silently overrides the global depth.
            int cap = MaxTier;
            try
            {
                var ov = StingTools.Tags.TokenDepthOverrides.Resolve(cat);
                if (ov != null && ov.Depth.HasValue) cap = Math.Max(1, Math.Min(MaxTier, ov.Depth.Value));
            }
            catch (Exception ex) { StingLog.Warn($"TagDoctor: depth override for '{cat}': {ex.Message}"); }

            sb.AppendLine(cap < MaxTier
                ? $"CAP      {cat} is capped at T{cap} by STING_TOKEN_DEPTH_OVERRIDES.json — "
                  + $"tiers above T{cap} can never show here, whatever the global depth."
                : "CAP      none — this category follows the global depth.");

            sb.AppendLine();
            sb.AppendLine("tier  gate@host  gate@hostType  gate@tagType  cap   value        verdict");
            sb.AppendLine("----  ---------  -------------  ------------  ----  -----------  ----------------------------");

            for (int t = 1; t <= MaxTier; t++)
            {
                string gp = "TAG_PARA_STATE_" + t + "_BOOL";
                string atHost = YesNo(host, gp);
                string atHostType = hostType != null ? YesNo(hostType, gp) : "-";
                string atTagType = tagType != null ? YesNo(tagType, gp) : "-";
                bool capped = t > cap;

                // Is there anything for this tier to draw? T1 is the tag string;
                // T4-T10 come through the TAG7 narrative containers.
                string valParam = ValueParamForTier(t);
                string val = valParam == null ? "?" :
                    (string.IsNullOrWhiteSpace(ParameterHelpers.GetString(host, valParam)) ? "EMPTY" : "present");

                sb.AppendLine(string.Format("T{0,-3}  {1,-9}  {2,-13}  {3,-12}  {4,-4}  {5,-11}  {6}",
                    t, atHost, atHostType, atTagType, capped ? "BLOCK" : "ok", val,
                    Verdict(atHost, atHostType, atTagType, capped, val)));
            }

            sb.AppendLine();
            sb.AppendLine("HOW TO READ IT");
            sb.AppendLine("  A gate shown as '-' is NOT BOUND on that element. Not false — absent.");
            sb.AppendLine("  The three gate columns exist because the label may read any of them, and");
            sb.AppendLine("  which one it reads has never been established. If a tier is ON at the tag");
            sb.AppendLine("  type, its value is present, nothing caps it, and the row still does not");
            sb.AppendLine("  draw, then the label is reading the HOST copy — record that in");
            sb.AppendLine("  docs/UNIVERSAL_TAG_FAMILY_PARAM_HYGIENE.md and the question is closed.");
        }

        private static string Verdict(string host, string hostType, string tagType, bool capped, string val)
        {
            if (capped) return "blocked by the category cap";
            bool anyOn = host == "ON" || hostType == "ON" || tagType == "ON";
            bool anyBound = host != "-" || hostType != "-" || tagType != "-";
            if (!anyBound) return "gate NOT BOUND anywhere — row can never open";
            if (!anyOn) return "gate off everywhere — raise the depth";
            if (val == "EMPTY") return "gate open, VALUE empty — re-run Tag+Combine";
            if (val == "?") return "gate open; value source not checked for this tier";
            return "should render";
        }

        /// <summary>The parameter a tier draws from, where there is a single obvious one.</summary>
        private static string ValueParamForTier(int tier)
        {
            switch (tier)
            {
                case 1: return ParamRegistry.TAG1;
                case 2: return "ASS_TAG_2_TXT";
                case 3: return "ASS_TAG_3_TXT";
                case 4: return "ASS_TAG_7D_TXT";   // T4-T10 render through the TAG7 narrative
                case 5: return "ASS_TAG_7E_TXT";
                case 6: return "ASS_TAG_7F_TXT";
                default: return null;              // no single source — reported as '?'
            }
        }

        private static string YesNo(Element el, string paramName)
        {
            try
            {
                Parameter p = el?.LookupParameter(paramName);
                if (p == null) return "-";                       // absent, not false
                if (p.StorageType != StorageType.Integer) return "?";
                return p.AsInteger() != 0 ? "ON" : "off";
            }
            catch { return "?"; }
        }

        private static string Describe(Element e)
        {
            string n = "";
            try { n = e.Name; } catch { }
            return $"{(string.IsNullOrEmpty(n) ? "(unnamed)" : n)}  id={e.Id}";
        }

        private static Element TaggedHost(Document doc, IndependentTag tag)
        {
            try
            {
                foreach (var r in tag.GetTaggedReferences())
                {
                    Element e = doc.GetElement(r.ElementId);
                    if (e != null) return e;
                }
            }
            catch (Exception ex) { StingLog.Warn($"TagDoctor.TaggedHost: {ex.Message}"); }
            return null;
        }

        private static IndependentTag FindTagFor(Document doc, Element host)
        {
            try
            {
                var view = doc.ActiveView;
                if (view == null) return null;
                foreach (IndependentTag t in new FilteredElementCollector(doc, view.Id)
                             .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                {
                    foreach (var r in t.GetTaggedReferences())
                        if (r.ElementId == host.Id) return t;
                }
            }
            catch (Exception ex) { StingLog.Warn($"TagDoctor.FindTagFor: {ex.Message}"); }
            return null;
        }
    }
}
