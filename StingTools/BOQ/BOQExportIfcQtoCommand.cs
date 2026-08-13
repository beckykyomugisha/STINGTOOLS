// BOQExportIfcQtoCommand.cs — INT-1: QTO-conformant IFC export for estimators.
//
// Produces an IFC4 file carrying base quantities + the BOQ cost/classification
// property sets, so an external estimator (RIB iTWO / CostX / Candy) can ingest
// measured quantities directly. This is the BOQ-facing IFC path — distinct from
// the generic IFC2x3 export (ExLink/AutomationEngine) and the Tekla-facing
// IFC4 Reference View (Commands/Mep/ExportPfvIfcCommand); it does NOT change
// either of those.
//
// HONEST SCOPE (read before trusting for external interop):
//  • There is NO literal "Quantity Takeoff MVD" in Revit's IFCVersion enum.
//    The buildingSMART QTO MVD is delivered in practice as IFC4 (or IFC2x3 CV2)
//    WITH ExportBaseQuantities=true — Revit computes and emits the standard
//    Qto_*BaseQuantities, which is exactly what CostX/iTWO read. That is the
//    "QTO-conformant" claim here.
//  • NRM2 (and CSI/ICMS where present) travels as Pset_StingCost.NRM2Section,
//    stamped by IfcQuantitySetWriter and carried via the cost pset. A proper
//    IfcClassificationReference entity requires a Revit classification-mapping
//    file (Export → Modify Setup → Classification), which can't be set through
//    the headless IFCExportOptions API — TODO-VERIFY-API / future.
//  • TODO-VERIFY-API: confirm in Revit which Qto/pset options actually emit the
//    stamped Pset_StingCost (ExportUserDefinedPsets may need a Pset definition
//    file). The base quantities (the core QTO payload) come from
//    ExportBaseQuantities=true and do NOT depend on that.
//
// Output: <project>/_BIM_COORD/ifc/sting_boq_qto_<ts>.ifc
//
// Reference:
//   https://ifc43-docs.standards.buildingsmart.org/IFC/RELEASE/IFC4x3/HTML/lexical/IfcElementQuantity.htm
//   https://standards.buildingsmart.org/MVD/RELEASE/IFC4/ADD2_TC1/RV1_2/HTML/schema/templates/quantity-sets.htm
//   https://www.revitapidocs.com/2025/  → IFCExportOptions

using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BOQ
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQExportIfcQtoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            string outDir, path;
            try
            {
                outDir = StingPaths.Meta(doc, "_BIM_COORD", "ifc");
                Directory.CreateDirectory(outDir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                path = Path.Combine(outDir, $"sting_boq_qto_{stamp}.ifc");
            }
            catch (Exception ex)
            {
                StingLog.Error("BOQExportIfcQtoCommand: path resolve", ex);
                message = ex.Message;
                return Result.Failed;
            }

            // 1) Build the BOQ and stamp Qto_*.* + Pset_StingCost.* (incl.
            //    NRM2Section + unit rate) onto every priced element, so the IFC
            //    carries quantities + cost + classification proxy. This is a real
            //    param write and MUST commit so the export below sees it.
            int stamped;
            try
            {
                var boq = BOQCostManager.BuildBOQDocument(doc);
                using (var tx = new Transaction(doc, "STING BOQ — stamp IFC Qto + cost psets"))
                {
                    tx.Start();
                    stamped = IfcQuantitySetWriter.StampAllElements(doc, boq);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("BOQExportIfcQtoCommand: stamp", ex);
                message = ex.Message;
                return Result.Failed;
            }

            // 2) Export IFC4 with base quantities + the cost/classification psets.
            var opts = new IFCExportOptions
            {
                FileVersion          = IFCVersion.IFC4,
                SpaceBoundaryLevel   = 0,
                ExportBaseQuantities = true,    // QTO — emit Qto_*BaseQuantities
                WallAndColumnSplitting = false,
            };
            opts.AddOption("ExportUserDefinedPsets",       "true");   // carry Pset_StingCost (NRM2/ICMS + cost)
            opts.AddOption("ExportIFCCommonPropertySets",  "true");
            opts.AddOption("ExportSchedulesAsPsets",       "false");
            opts.AddOption("ExportPartsAsBuildingElements","false");
            opts.AddOption("ExportBoundingBox",            "false");
            opts.AddOption("ExportLinkedFiles",            "false");
            opts.AddOption("IncludeSiteElevation",         "false");
            opts.AddOption("UseActiveViewGeometry",        "false");

            using (var tx = new Transaction(doc, "STING BOQ — export QTO IFC"))
            {
                try
                {
                    tx.Start();
                    bool ok = doc.Export(outDir, Path.GetFileName(path), opts);
                    tx.RollBack(); // Export leaves no persistent changes; rollback defensively.
                    if (!ok)
                    {
                        message = "Document.Export returned false";
                        return Result.Failed;
                    }
                }
                catch (Exception ex2)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    StingLog.Error("BOQExportIfcQtoCommand: export", ex2);
                    message = ex2.Message;
                    return Result.Failed;
                }
            }

            var rp = StingResultPanel.Create("BOQ → QTO IFC (estimator feed)");
            rp.SetSubtitle($"IFC4 written: {path}");
            rp.AddSection("DETAILS")
                 .Metric("Schema",            "IFC4 + base quantities")
                 .Metric("Elements stamped",  stamped.ToString())
                 .Metric("Quantities",        "Qto_*BaseQuantities (Revit-computed)")
                 .Metric("Cost / class",      "Pset_StingCost (UnitRate, NRM2Section, …)");
            rp.AddSection("NEXT STEPS")
                 .Text("1. In CostX / iTWO: import the IFC4 file above as a BIM dimension source.")
                 .Text("2. Map Qto_*BaseQuantities → workbook quantities; rates from your rate library.")
                 .Text("3. Round-trip pricing back via the GUID-keyed XLSX (BOQ export → re-import).");
            rp.AddSection("LIMITATIONS")
                 .Text("• No literal Quantity-Takeoff MVD in Revit — IFC4 + base quantities is the equivalent.")
                 .Text("• NRM2/ICMS rides Pset_StingCost; a true IfcClassificationReference needs a Revit")
                 .Text("  classification-mapping file (not settable via headless IFCExportOptions) — future.")
                 .Text("• Verify in Revit that the cost pset emits (ExportUserDefinedPsets may need a Pset file).");

            // 5D-workspace inline convention (Slice 1): when invoked from the BOQ
            // Cost Manager panel the InlineHost=1 ExtraParam routes the result into
            // the panel's inline region instead of a popup window. Consume the flag
            // so a later ribbon/workflow invocation doesn't inherit it. If no live
            // panel sink is registered, fall back to the popup so ribbon callers
            // still see the result.
            bool inline = StingCommandHandler.GetExtraParam("InlineHost") == "1";
            if (inline)
            {
                StingCommandHandler.ClearExtraParam("InlineHost");
                if (!BOQInlineResults.Post(rp)) rp.Show();
            }
            else
            {
                rp.Show();
            }
            return Result.Succeeded;
        }
    }
}
