// StingTools — Routing_PlaceSleeveConnectors.
//
// Authors conduit-connector stubs on connector-less fixtures so that
// manufacturer families (which lack a Domain.DomainCableTrayConduit
// connector after Seeds_SwapToManufacturer) become routable by
// AutoConduitDrop. See Core/Mep/SleeveConnectorEngine for the rationale.
//
// The command always offers a dry-run first (it cannot be Revit-tested
// from the dev box), reporting how many stubs WOULD be placed and where,
// before any geometry is created. The engine is idempotent, so a second
// live run over the same fixtures is a no-op.

using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Routing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceSleeveConnectorsCommand : IExternalCommand
    {
        // Electrical categories that carry final-circuit conduit terminals.
        private static readonly BuiltInCategory[] ElecCats =
        {
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_NurseCallDevices,
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // Target set: the selection when it contains electrical fixtures,
            // else every electrical fixture in the active view.
            var fixtures = CollectSelected(doc, uidoc);
            string scope;
            if (fixtures.Count > 0) scope = "current selection";
            else
            {
                fixtures = CollectFromActiveView(doc);
                scope = "active view";
            }

            if (fixtures.Count == 0)
            {
                TaskDialog.Show("STING — Sleeve Connectors",
                    "No electrical fixtures found in the selection or active view.\n\n" +
                    "Select the placed (manufacturer) fixtures, or open a plan view containing them, then re-run.");
                return Result.Cancelled;
            }

            // Always plan (dry-run) first so the user sees the impact.
            var engine = new SleeveConnectorEngine(doc);
            SleeveConnectorResult preview;
            try { preview = engine.Run(fixtures, dryRun: true); }
            catch (Exception ex)
            {
                StingLog.Error("PlaceSleeveConnectorsCommand dry-run failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            var td = new TaskDialog("STING — Sleeve Connectors")
            {
                MainInstruction = $"{preview.Sleeved} fixture(s) would get a conduit terminal.",
                MainContent =
                    $"Scanned {preview.Considered} fixture(s) in the {scope}.\n" +
                    $"• {preview.AlreadyRoutable} already have a free conduit connector (routable).\n" +
                    $"• {preview.AlreadySleeved} already carry a STING sleeve stub / real terminal.\n" +
                    $"• {preview.Sleeved} lack a conduit connector and would receive a " +
                    $"{engine.StubSizeMm:F0} mm × {engine.StubLengthMm:F0} mm stub.\n\n" +
                    "Each stub leaves a free, upward-facing conduit terminal for AutoConduitDrop to extend.",
                AllowCancellation = true
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Preview only (no geometry)",
                "Report the plan and stop — nothing is created.");
            if (preview.Sleeved > 0)
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    $"Place {preview.Sleeved} sleeve connector(s)",
                    "Author the conduit stubs now (idempotent — safe to re-run).");
            td.CommonButtons = TaskDialogCommonButtons.Close;

            var choice = td.Show();
            if (choice == TaskDialogResult.CommandLink1 || choice == TaskDialogResult.Close)
            {
                ShowResult(preview, scope, engine, previewOnly: true);
                return Result.Succeeded;
            }

            if (choice == TaskDialogResult.CommandLink2)
            {
                SleeveConnectorResult live;
                try { live = new SleeveConnectorEngine(doc).Run(fixtures, dryRun: false); }
                catch (Exception ex)
                {
                    StingLog.Error("PlaceSleeveConnectorsCommand live run failed", ex);
                    message = ex.Message;
                    return Result.Failed;
                }
                ShowResult(live, scope, engine, previewOnly: false);
                return Result.Succeeded;
            }

            return Result.Cancelled;
        }

        private static List<Element> CollectSelected(Document doc, UIDocument uidoc)
        {
            var list = new List<Element>();
            var ids = uidoc.Selection.GetElementIds();
            if (ids == null) return list;
            var elecSet = new HashSet<int>();
            foreach (var c in ElecCats) elecSet.Add((int)c);
            foreach (var id in ids)
            {
                var el = doc.GetElement(id);
                if (el?.Category == null) continue;
                if (elecSet.Contains((int)(BuiltInCategory)el.Category.Id.Value)) list.Add(el);
            }
            return list;
        }

        private static List<Element> CollectFromActiveView(Document doc)
        {
            var list = new List<Element>();
            if (doc.ActiveView == null) return list;
            try
            {
                var filter = new ElementMulticategoryFilter(ElecCats);
                var col = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WherePasses(filter).WhereElementIsNotElementType();
                foreach (var el in col) list.Add(el);
            }
            catch (Exception ex) { StingLog.Warn($"PlaceSleeveConnectorsCommand: active-view collect failed: {ex.Message}"); }
            return list;
        }

        private static void ShowResult(SleeveConnectorResult r, string scope,
            SleeveConnectorEngine engine, bool previewOnly)
        {
            var panel = StingResultPanel.Create("Sleeve Connectors");
            panel.SetSubtitle(previewOnly
                ? $"Dry-run over {scope} — no geometry created"
                : $"Placed over {scope}");

            panel.AddSection("SUMMARY")
                 .Metric("Considered",      r.Considered.ToString())
                 .Metric("Already routable", r.AlreadyRoutable.ToString())
                 .Metric("Already sleeved",  r.AlreadySleeved.ToString())
                 .Metric(previewOnly ? "Would place" : "Placed", r.Sleeved.ToString())
                 .Metric("Failed",          r.Failed.ToString())
                 .Metric("Stub",            $"{engine.StubSizeMm:F0}×{engine.StubLengthMm:F0} mm");

            int shown = 0;
            foreach (var it in r.Items)
            {
                if (shown++ >= 15) break;
                var p = it.TerminalFt;
                panel.Text(p == null
                    ? $"• {it.FixtureName} ({it.FixtureId})"
                    : $"• {it.FixtureName} ({it.FixtureId}) @ ({p.X * 304.8:F0}, {p.Y * 304.8:F0}, {p.Z * 304.8:F0}) mm");
            }
            if (r.Items.Count > 15) panel.Text($"(+{r.Items.Count - 15} more)");

            foreach (var w in Head(r.Warnings, 10)) panel.Text(w);
            if (r.Warnings.Count > 10) panel.Text($"(+{r.Warnings.Count - 10} more — see StingLog)");
            panel.Show();
        }

        private static IEnumerable<string> Head(List<string> src, int n)
        {
            for (int i = 0; i < src.Count && i < n; i++) yield return src[i];
        }
    }
}
