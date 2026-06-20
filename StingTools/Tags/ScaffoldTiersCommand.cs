using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    // ─────────────────────────────────────────────────────────────────────────
    // Scaffold Tiers — one-click "ready-to-fill" model for colleagues.
    //
    // Sets up the full tier framework so a coordinator can hand the model to the
    // team to complete labels manually while the job proceeds:
    //   1. Bind every tier/container (8 tokens + ASS_TAG_2..7 + the discipline
    //      containers + TAG7 A-F) via LoadSharedParams — the fields now exist on
    //      every element, empty, ready to fill.
    //   2. Reveal all 10 paragraph tiers (SetParagraphDepth T10, warnings off) so
    //      colleagues can see and complete every tier.
    //   3. Leave the segment mask at its default (all 8 segments visible).
    //
    // Pairs with TAG1_ONLY=true in project_config.json: the coordinator's own
    // tagging pass writes only ASS_TAG_1 (the ISO 19650 first line) and leaves the
    // tiers for the team. Read/written only through the existing, verified commands.
    // ─────────────────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScaffoldTiersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // 1. Bind all tiers/containers (reuse the verified shared-parameter binder).
            string bindMsg = "";
            Result bind;
            try { bind = new LoadSharedParamsCommand().Execute(commandData, ref bindMsg, elements); }
            catch (Exception ex)
            {
                StingLog.Error("ScaffoldTiers bind", ex);
                TaskDialog.Show("Scaffold Tiers", "Parameter binding failed:\n" + ex.Message);
                return Result.Failed;
            }
            if (bind == Result.Cancelled) return Result.Cancelled;

            // 2. Reveal all 10 paragraph tiers on every type (warnings off).
            int typesUpdated = 0;
            try
            {
                using (var t = new Transaction(doc, "STING Scaffold Tiers — depth T10"))
                {
                    t.Start();
                    typesUpdated = TagStyleEngine.SetParagraphDepth(doc, 10, warnVisible: false);
                    t.Commit();
                }
            }
            catch (Exception ex) { StingLog.Warn("ScaffoldTiers depth: " + ex.Message); }

            // 3. Segment mask left at default (all 8 segments visible) — no write needed.

            new TaskDialog("Scaffold Tiers")
            {
                MainInstruction = "Model scaffolded — ready for colleagues to fill labels",
                MainContent =
                    "1. All tag tiers/containers bound (8 tokens + ASS_TAG_2..7 + discipline containers + TAG7 A-F).\n" +
                    $"2. Paragraph depth set to T10 (all tiers visible) on {typesUpdated} type(s).\n" +
                    "3. Segment mask left at default (all 8 segments).\n\n" +
                    "Colleagues complete labels via element Properties or the Excel round-trip " +
                    "(Export to Excel → fill → Import from Excel).\n\n" +
                    "Tip: set TAG1_ONLY = true via the Configure command (project_config.json) for your own " +
                    "coordination pass — your tagging then writes only ASS_TAG_1 and leaves the tiers for the team."
            }.Show();
            StingLog.Info($"ScaffoldTiers: bound params + paragraph depth T10 on {typesUpdated} type(s).");
            return Result.Succeeded;
        }
    }
}
