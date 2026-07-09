using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// RefreshTagDisplay — re-apply the current token-depth mask to already-placed
    /// tags WITHOUT re-tagging.
    ///
    /// Model (non-destructive, fully reversible):
    ///   • ASS_TAG_1_TXT  = canonical full 8-segment tag — the source of truth for
    ///     collision detection, schedules, BOQ, COBie and exports. NEVER masked.
    ///   • ASS_DISPLAY_TXT = the presentational string the tag families read. This
    ///     command recomputes it as ApplySegmentMask(ASS_TAG_1_TXT, mask).
    ///   • STING_VIEW_TOKEN_MASK_TXT = the per-view 8-char depth mask (DISC LOC ZONE
    ///     LVL SYS FUNC PROD SEQ). "11111111" = show everything.
    ///
    /// Workflow: place tags once → change token depth (dock Tokens &amp; Depth, or a
    /// view mask) → run Refresh → the placed tags update on the next regen because
    /// they read ASS_DISPLAY_TXT. Re-running with an all-on mask restores the full
    /// tag. No tokens are re-derived, no SEQ is reassigned, no annotations are
    /// recreated.
    ///
    /// Tag families must bind their Label to ASS_DISPLAY_TXT (one-time). Until then
    /// the command still populates the param; it just isn't shown.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RefreshTagDisplayCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;
                UIDocument uidoc = ctx.UIDoc;
                View av = doc.ActiveView;

                // ── Resolve the active depth mask ────────────────────────────
                // Precedence: dock Tokens & Depth (this session) → active view's
                // persisted mask → all-on (full tag).
                string mask = NormalizeMask(StingTools.UI.StingCommandHandler.GetExtraParam("TokenMask"));
                bool fromDock = mask != null;
                if (mask == null) mask = NormalizeMask(ParameterHelpers.GetString(av, ParamRegistry.VIEW_TOKEN_MASK));
                if (mask == null) mask = "11111111";

                // ── Scope ────────────────────────────────────────────────────
                var pick = new TaskDialog("STING — Refresh Tag Display")
                {
                    MainInstruction = $"Apply token-depth mask {mask} to placed tags",
                    MainContent = "Re-masks ASS_DISPLAY_TXT from the canonical ASS_TAG_1_TXT. " +
                                  "No re-tagging — placed tags update on regen.\n\n" +
                                  "1 = segment shown, order is DISC LOC ZONE LVL SYS FUNC PROD SEQ.",
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };
                pick.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Active view", "Refresh tags in the current view");
                pick.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Current selection", "Refresh only selected elements");
                pick.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Whole project", "Refresh every tagged element");
                var choice = pick.Show();

                List<Element> scope;
                bool scopeIsView = false;
                switch (choice)
                {
                    case TaskDialogResult.CommandLink1:
                        scope = CollectInView(doc, av); scopeIsView = true; break;
                    case TaskDialogResult.CommandLink2:
                        scope = (uidoc?.Selection?.GetElementIds() ?? new List<ElementId>())
                            .Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
                        break;
                    case TaskDialogResult.CommandLink3:
                        scope = CollectInProject(doc); break;
                    default:
                        return Result.Cancelled;
                }

                int updated = 0, skippedNoTag = 0;
                using (var t = new Transaction(doc, "STING Refresh Tag Display"))
                {
                    t.Start();

                    // Persist the dock mask onto the view so the depth sticks per
                    // view and the next Refresh reproduces it without the dock.
                    if (fromDock && scopeIsView && av != null)
                        ParameterHelpers.SetString(av, ParamRegistry.VIEW_TOKEN_MASK, mask, overwrite: true);

                    var res = RefreshDisplayInScope(doc, scope, mask, TagConfig.EffectiveSeqPad);
                    updated = res.updated; skippedNoTag = res.skippedNoTag;
                    t.Commit();
                }

                StingLog.Info($"RefreshTagDisplay: mask={mask} updated={updated} skippedNoTag={skippedNoTag}");
                new TaskDialog("STING — Refresh Tag Display")
                {
                    MainInstruction = $"Refreshed {updated} tag display value(s)",
                    MainContent = $"Mask applied: {mask}\n" +
                                  $"Elements with no tag (skipped): {skippedNoTag}\n\n" +
                                  (updated > 0
                                      ? "If the placed tags didn't change, set each tag family's Label to " +
                                        "ASS_DISPLAY_TXT (Edit Label) — that's the container this depth control drives.\n\n" +
                                        "Re-run with mask 11111111 to restore the full tag."
                                      : "Nothing to update in this scope.")
                }.Show();
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("RefreshTagDisplayCommand crashed", ex);
                try { TaskDialog.Show("STING Tools", $"Refresh Tag Display failed:\n{ex.Message}"); } catch { }
                return Result.Failed;
            }
        }

        /// <summary>Return a valid 8-char 0/1 mask, or null when absent/invalid.</summary>
        internal static string NormalizeMask(string m)
        {
            if (string.IsNullOrWhiteSpace(m)) return null;
            m = m.Trim();
            if (m.Length != 8 || !m.All(c => c == '0' || c == '1')) return null;
            return m;
        }

        /// <summary>
        /// Reusable, dialog-free display refresh. For each element: re-pad the SEQ
        /// segment of the canonical ASS_TAG_1_TXT to <paramref name="seqPad"/>, apply
        /// the segment <paramref name="mask"/>, and write the result to ASS_DISPLAY_TXT.
        /// Display-only — ASS_TAG_1_TXT is never touched, no tokens are re-derived and
        /// no SEQ number is reassigned (only its zero-pad width is presented). Caller
        /// supplies the open Transaction. Shared by the standalone RefreshTagDisplay
        /// command and the live "Set depth" apply path.
        /// </summary>
        internal static (int updated, int skippedNoTag) RefreshDisplayInScope(
            Document doc, IList<Element> scope, string mask, int seqPad)
        {
            int updated = 0, skippedNoTag = 0;
            if (scope == null) return (0, 0);
            foreach (Element el in scope)
            {
                string full = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(full)) { skippedNoTag++; continue; }
                // Build from the source TOKENS so the CURRENT separator + SEQ pad +
                // mask all apply live. (ASS_TAG_1 has a baked separator; masking it
                // after the user switches separator would split on the wrong char and
                // silently no-op — the "only hyphen works" bug.) Fall back to the
                // baked tag when the element carries no source tokens.
                string disp = BuildMaskedDisplayFromTokens(el, mask, seqPad)
                              ?? TagConfig.ApplySegmentMask(RepadSeqSegment(full, seqPad), mask);
                if (ParameterHelpers.SetString(el, ParamRegistry.DISPLAY_TXT, disp, overwrite: true))
                    updated++;
            }
            return (updated, skippedNoTag);
        }

        /// <summary>
        /// Assemble the presentational display from the 8 source tokens (DISC LOC ZONE
        /// LVL SYS FUNC PROD SEQ) using the CURRENT separator, SEQ zero-pad and 8-char
        /// mask — independent of how ASS_TAG_1_TXT was baked, so separator + pad + mask
        /// changes all apply live. Keeps mask=1 positions (matching ApplySegmentMask
        /// semantics). Returns null when the element carries no source tokens (caller
        /// falls back to the canonical tag).
        /// </summary>
        internal static string BuildMaskedDisplayFromTokens(Element el, string mask, int seqPad)
        {
            string[] tp = ParamRegistry.AllTokenParams;
            if (tp == null || tp.Length < 8) return null;
            var vals = new string[8];
            bool any = false;
            for (int i = 0; i < 8; i++)
            {
                vals[i] = ParameterHelpers.GetString(el, tp[i]) ?? string.Empty;
                if (vals[i].Length > 0) any = true;
            }
            if (!any) return null;
            // Re-pad SEQ (slot 7), numeric only — presentational, never renumbers.
            if (seqPad > 0 && vals[7].Length > 0 && vals[7].All(char.IsDigit))
            {
                string d = vals[7].TrimStart('0');
                if (d.Length == 0) d = "0";
                vals[7] = d.PadLeft(seqPad, '0');
            }
            string sep = !string.IsNullOrEmpty(ParamRegistry.Separator) ? ParamRegistry.Separator : "-";
            string m = (!string.IsNullOrEmpty(mask) && mask.Length >= 8) ? mask : "11111111";
            var visible = new List<string>();
            for (int i = 0; i < 8; i++)
                if (m[i] == '1') visible.Add(vals[i]);
            return visible.Count > 0 ? string.Join(sep, visible) : null;
        }

        /// <summary>
        /// Re-pad the SEQ (last) segment of a separator-joined tag to <paramref name="width"/>
        /// zero-padded digits — display-only, presentational. Leaves a non-numeric SEQ
        /// (scheme-rendered / alphanumeric) untouched, and returns the input unchanged
        /// when width &lt;= 0 or the tag has no separable segments.
        /// </summary>
        internal static string RepadSeqSegment(string full, int width)
        {
            if (string.IsNullOrEmpty(full) || width <= 0) return full;
            string sep = ParamRegistry.Separator;
            if (string.IsNullOrEmpty(sep)) return full;
            string[] parts = full.Split(new[] { sep }, StringSplitOptions.None);
            if (parts.Length < 2) return full;
            string seq = parts[parts.Length - 1];
            if (seq.Length == 0 || !seq.All(char.IsDigit)) return full;
            string digits = seq.TrimStart('0');
            if (digits.Length == 0) digits = "0";
            parts[parts.Length - 1] = digits.PadLeft(width, '0');
            return string.Join(sep, parts);
        }

        /// <summary>
        /// Collect the elements to refresh for a scope token ("View" / "Selection" /
        /// "Project"). Defaults to the active view. Matches the Tokens &amp; Depth
        /// "Scope" combo (TokenScope ExtraParam).
        /// </summary>
        internal static List<Element> CollectScope(Document doc, UIDocument uidoc, string scopeToken)
        {
            switch (scopeToken)
            {
                case "Selection":
                    return (uidoc?.Selection?.GetElementIds() ?? new List<ElementId>())
                        .Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
                case "Project":
                    return CollectInProject(doc);
                default:
                    return CollectInView(doc, doc.ActiveView);
            }
        }

        private static List<Element> CollectInView(Document doc, View view)
        {
            if (view == null) return new List<Element>();
            var c = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType();
            c = ApplyCatFilter(c);
            return c.ToList();
        }

        private static List<Element> CollectInProject(Document doc)
        {
            var c = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            c = ApplyCatFilter(c);
            return c.ToList();
        }

        private static FilteredElementCollector ApplyCatFilter(FilteredElementCollector c)
        {
            var cats = SharedParamGuids.AllCategoryEnums;
            if (cats != null && cats.Length > 0)
                c.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(cats)));
            return c;
        }
    }
}
