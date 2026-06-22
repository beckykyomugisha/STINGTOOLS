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

                    foreach (Element el in scope)
                    {
                        string full = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(full)) { skippedNoTag++; continue; }
                        string disp = TagConfig.ApplySegmentMask(full, mask);
                        if (ParameterHelpers.SetString(el, ParamRegistry.DISPLAY_TXT, disp, overwrite: true))
                            updated++;
                    }
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
        private static string NormalizeMask(string m)
        {
            if (string.IsNullOrWhiteSpace(m)) return null;
            m = m.Trim();
            if (m.Length != 8 || !m.All(c => c == '0' || c == '1')) return null;
            return m;
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
