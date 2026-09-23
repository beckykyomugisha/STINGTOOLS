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
                var ov = StingTools.Tags.TokenDepthOverrides.Resolve(doc, cat);
                if (ov != null && ov.Depth.HasValue) cap = Math.Max(1, Math.Min(MaxTier, ov.Depth.Value));
            }
            catch (Exception ex) { StingLog.Warn($"TagDoctor: depth override for '{cat}': {ex.Message}"); }

            sb.AppendLine(cap < MaxTier
                ? $"CAP      {cat} is capped at T{cap} by STING_TOKEN_DEPTH_OVERRIDES.json — "
                  + $"tiers above T{cap} can never show here, whatever the global depth."
                : "CAP      none — this category follows the global depth.");

            // What parameters can this tag family actually label? Resolved once,
            // from the family DOCUMENT - see TagFamilyParams for why the symbol
            // is the wrong place to ask.
            HashSet<string> famParams = TagFamilyParams(doc, tagType);
            sb.AppendLine(famParams == null
                ? "FAMILY   could not open the tag family to read its parameters — param@tag shows '?'"
                : $"FAMILY   tag family exposes {famParams.Count} shared parameter(s) to its labels");

            sb.AppendLine(GateReport(doc, tagType));
            sb.AppendLine(HostGateDetail(doc, host, hostType));

            sb.AppendLine();
            sb.AppendLine("tier  gate@host  gate@hostType  gate@tagType  cap    value     param@tag  verdict");
            sb.AppendLine("----  ---------  -------------  ------------  -----  --------  ---------  --------------------------------");

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

                // Does the TAG FAMILY carry the value parameter at all?
                string inTag = "?";
                if (valParam != null && famParams != null)
                    inTag = famParams.Contains(valParam) ? "yes" : "NO";

                sb.AppendLine(string.Format("T{0,-3}  {1,-9}  {2,-13}  {3,-12}  {4,-5}  {5,-8}  {6,-9}  {7}",
                    t, atHost, atHostType, atTagType, capped ? "BLOCK" : "ok", val, inTag,
                    Verdict(atHost, atHostType, atTagType, capped, val, inTag)));
            }

            sb.AppendLine();
            sb.AppendLine("HOW TO READ IT");
            sb.AppendLine("  A gate shown as '-' is NOT BOUND on that element. Not false — absent.");
            sb.AppendLine("  'param@tag' is read from the tag family's own DOCUMENT, not from the");
            sb.AppendLine("  placed symbol — the symbol only carries the TAG's parameters, never the");
            sb.AppendLine("  host ones its labels read. NO means the family cannot label that");
            sb.AppendLine("  parameter at all; yes means it can, not that a row does.");
            sb.AppendLine();
            sb.AppendLine("  WHAT THIS CANNOT SEE: Revit exposes no API for listing a tag family's");
            sb.AppendLine("  label rows, and none for telling which rows actually drew. So compare");
            sb.AppendLine("  this table against the tag on the drawing:");
            sb.AppendLine("    · a tier that DRAWS and reads clean here — nothing is wrong with it;");
            sb.AppendLine("    · a tier that is BLANK and reads clean here — the label row is the");
            sb.AppendLine("      only remaining suspect, and must be hand-authored in the Family");
            sb.AppendLine("      Editor, which no API can do for you.");
        }

        /// <summary>
        /// Name the first broken link, or say what is left when none are.
        ///
        /// <para>Deliberately never says "should render", and equally never says
        /// the label row IS missing. Revit exposes no API for enumerating a tag
        /// family's LABEL ROWS, so whether a row exists is the one link this
        /// command cannot see - and asserting either side of an unmeasured fact
        /// is the same error twice.</para>
        ///
        /// <para>Both wordings were tried and both were falsified by the same
        /// observation. "should render" was wrong because rows were blank.
        /// "the LABEL ROW is missing" was wrong because T1 was rendering while
        /// the command declared its row absent. The only defensible phrasing is
        /// conditional: everything measurable is fine, so IF the row is blank,
        /// the label row is what is left. The user can see which rows are blank;
        /// this command cannot.</para>
        /// </summary>
        private static string Verdict(string host, string hostType, string tagType,
                                      bool capped, string val, string inTag)
        {
            if (capped) return "blocked by the category cap";
            bool anyOn = host == "ON" || hostType == "ON" || tagType == "ON";
            bool anyBound = host != "-" || hostType != "-" || tagType != "-";
            if (!anyBound) return "gate NOT BOUND anywhere — row can never open";
            if (!anyOn) return "gate off everywhere — raise the depth";
            if (val == "EMPTY") return "gate open, VALUE empty — re-run Tag+Combine";
            if (inTag == "NO") return "tag family cannot label this parameter — re-run Propagate";
            if (val == "?") return "gate open; no single value source to check for this tier";
            return "all measurable links OK — if blank, only the label row is left";
        }

        /// <summary>
        /// For every tier gate that IS bound to the tagged element, why it reads
        /// the way it does: read-only or not, how it is stored, its raw value,
        /// and whether the project bound it to Instances or to Types.
        ///
        /// <para>WHY. On 2026-09-23 a gate was bound to Air Terminals, ticked in
        /// Type Properties, and still read 'off' on the next run - twice. A
        /// checkbox that will not hold a value and a checkbox nobody ticked look
        /// the same afterwards, and so does a Type binding when the value was set
        /// on an Instance. This prints the three apart instead of leaving them to
        /// be argued about.</para>
        /// </summary>
        private static string HostGateDetail(Document doc, Element host, ElementType hostType)
        {
            var sb = new StringBuilder();
            int shown = 0;

            for (int t = 1; t <= MaxTier; t++)
            {
                string gp = "TAG_PARA_STATE_" + t + "_BOOL";
                Parameter pInst = null, pType = null;
                try { pInst = host?.LookupParameter(gp); } catch { }
                try { pType = hostType?.LookupParameter(gp); } catch { }
                if (pInst == null && pType == null) continue;

                if (shown == 0)
                {
                    sb.AppendLine("HOSTGATE why the bound gate(s) on this element read as they do");
                    sb.AppendLine("  tier  on        read-only  stored as  raw       project binding");
                    sb.AppendLine("  ----  --------  ---------  ---------  --------  ---------------");
                }
                shown++;

                Parameter p = pType ?? pInst;
                string on = pType != null ? "type" : "instance";
                string ro = "?";
                string stored = "?";
                string raw = "?";
                try { ro = p.IsReadOnly ? "YES" : "no"; } catch { }
                try { stored = p.StorageType.ToString(); } catch { }
                try
                {
                    if (p.StorageType == StorageType.Integer) raw = p.AsInteger().ToString();
                    else if (p.StorageType == StorageType.String) raw = "\"" + (p.AsString() ?? "") + "\"";
                    else raw = p.AsValueString() ?? "(none)";
                }
                catch { }

                sb.AppendLine(string.Format("  T{0,-3}  {1,-8}  {2,-9}  {3,-9}  {4,-8}  {5}",
                    t, on, ro, stored, raw, BindingKind(doc, gp)));
            }

            if (shown == 0)
                return "HOSTGATE no tier gate is bound to this element — nothing to detail.";

            sb.AppendLine("  read-only YES means the checkbox cannot hold a value, so a tick never sticks.");
            sb.Append("  A Type binding set on an Instance (or the reverse) also reads back unchanged.");
            return sb.ToString();
        }

        /// <summary>Instance or Type, as the PROJECT bound it — not as it was asked for.</summary>
        private static string BindingKind(Document doc, string paramName)
        {
            try
            {
                var it = doc.ParameterBindings.ForwardIterator();
                while (it.MoveNext())
                {
                    var def = it.Key;
                    if (def == null || !string.Equals(def.Name, paramName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var b = it.Current as Binding;
                    if (b is TypeBinding) return "Type";
                    if (b is InstanceBinding) return "Instance";
                    return "(bound, kind unknown)";
                }
            }
            catch (Exception ex) { StingLog.Warn($"TagDoctor.BindingKind '{paramName}': {ex.Message}"); }
            return "not a project parameter";
        }

        /// <summary>
        /// What the TAG FAMILY itself holds for each tier gate: whether the
        /// parameter exists, how it is STORED, and its value in the placed type.
        ///
        /// <para>WHY STORAGE TYPE IS THE POINT. A label calculated value gates on
        /// <c>if(TAG_PARA_STATE_n_BOOL, X, "")</c>. That is only a boolean test if
        /// the parameter is stored as an INTEGER. If the family was hand-authored
        /// with a TEXT variant holding "Yes"/"No" - a case TagTypeVariantWriter
        /// explicitly carries a fallback for - the if() has a string where it
        /// wants a condition, and falls to the else branch every time, whatever
        /// is ticked anywhere. That failure is invisible on a drawing: the row
        /// simply does not draw, exactly like an unbound gate or a missing label
        /// row.</para>
        ///
        /// <para>Measured here rather than in the Family Editor because three
        /// round trips through Edit Label produced three silences, and a silence
        /// does not say which of its causes it is.</para>
        /// </summary>
        private static string GateReport(Document doc, ElementType tagType)
        {
            var sym = tagType as FamilySymbol;
            Family fam = sym?.Family;
            if (fam == null) return "GATES    tag type is not a family symbol — cannot inspect.";

            Document famDoc = null;
            try
            {
                famDoc = doc.EditFamily(fam);
                if (famDoc == null) return "GATES    could not open the tag family.";

                var fm = famDoc.FamilyManager;

                // Point CurrentType at the type that is actually placed, so the
                // values read are the ones the drawing uses - not whichever type
                // the family happened to open on.
                FamilyType placed = null;
                foreach (FamilyType ft in fm.Types)
                    if (string.Equals(ft.Name, tagType.Name, StringComparison.OrdinalIgnoreCase))
                    { placed = ft; break; }
                if (placed != null) { try { fm.CurrentType = placed; } catch { } }

                var byName = new Dictionary<string, FamilyParameter>(StringComparer.OrdinalIgnoreCase);
                foreach (FamilyParameter fp in fm.Parameters)
                {
                    string nm = fp?.Definition?.Name;
                    if (!string.IsNullOrEmpty(nm)) byName[nm] = fp;
                }

                var sb = new StringBuilder();
                sb.AppendLine("GATES    as the TAG FAMILY holds them, in type '"
                              + (placed != null ? placed.Name : "(not matched — values below are another type's)") + "'");
                sb.AppendLine("  tier  in family  stored as  shared  value");
                sb.AppendLine("  ----  ---------  ---------  ------  -----");

                for (int t = 1; t <= MaxTier; t++)
                {
                    string gp = "TAG_PARA_STATE_" + t + "_BOOL";
                    if (!byName.TryGetValue(gp, out FamilyParameter fp))
                    {
                        sb.AppendLine(string.Format("  T{0,-3}  {1,-9}  {2,-9}  {3,-6}  {4}",
                            t, "NO", "-", "-", "absent — if() on it cannot evaluate"));
                        continue;
                    }

                    string stored = fp.StorageType.ToString();
                    string shared = fp.IsShared ? "yes" : "NO";
                    string val = "?";
                    try
                    {
                        var ct = fm.CurrentType;
                        if (ct != null)
                        {
                            if (fp.StorageType == StorageType.Integer)
                            { int? i = ct.AsInteger(fp); val = i.HasValue ? (i.Value != 0 ? "1 (on)" : "0 (off)") : "(unset)"; }
                            else if (fp.StorageType == StorageType.String)
                            { string sv = ct.AsString(fp); val = string.IsNullOrEmpty(sv) ? "(empty)" : "\"" + sv + "\""; }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"TagDoctor gate {gp}: {ex.Message}"); }

                    string note = fp.StorageType == StorageType.Integer
                        ? ""
                        : "  <-- NOT Integer: if() cannot use it as a condition";
                    sb.AppendLine(string.Format("  T{0,-3}  {1,-9}  {2,-9}  {3,-6}  {4}{5}",
                        t, "yes", stored, shared, val, note));
                }

                sb.Append("  A gate stored as String holds \"Yes\"/\"No\" text. if() wants a condition, "
                          + "so it takes the else branch every time — whatever is ticked.");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagDoctor.GateReport '{fam.Name}': {ex.Message}");
                return "GATES    could not read the tag family: " + ex.Message;
            }
            finally { try { famDoc?.Close(false); } catch { } }
        }

        /// <summary>
        /// The shared parameters a tag family's labels can read, by name, or null
        /// when the family could not be opened.
        ///
        /// <para>WHY IT OPENS THE FAMILY DOCUMENT. The first version of this
        /// asked the loaded FamilySymbol - <c>tagType.LookupParameter(name)</c> -
        /// which returns the TAG's own parameters. A label that reads the tagged
        /// element's shared parameter is not one of those; it lives in the family
        /// document. So the symbol returned null for all ten tiers whatever the
        /// truth, and the command turned a guaranteed null into a confident
        /// "re-run Propagate".</para>
        ///
        /// <para>It was caught because it contradicted the drawing: it reported
        /// T1's parameter missing while the T1 line was visibly rendering. A
        /// probe that cannot return "yes" is not a probe, and a verdict built on
        /// one is worse than no column at all - it is actionable and wrong.</para>
        ///
        /// <para>HONEST LIMIT: a parameter being present means the family CAN
        /// label it, not that a visible label row does. Revit exposes no API for
        /// enumerating label rows, so "yes" narrows the suspects without
        /// clearing the last one.</para>
        /// </summary>
        private static HashSet<string> TagFamilyParams(Document doc, ElementType tagType)
        {
            var sym = tagType as FamilySymbol;
            Family fam = sym?.Family;
            if (fam == null) return null;

            Document famDoc = null;
            try
            {
                // EditFamily cannot run inside an open transaction; this command
                // is ReadOnly, so none is open.
                famDoc = doc.EditFamily(fam);
                if (famDoc == null) return null;

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FamilyParameter fp in famDoc.FamilyManager.Parameters)
                {
                    string nm = fp?.Definition?.Name;
                    if (!string.IsNullOrEmpty(nm)) names.Add(nm);
                }
                return names;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagDoctor: opening tag family '{fam.Name}': {ex.Message}");
                return null;
            }
            finally
            {
                try { famDoc?.Close(false); } catch { }
            }
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
