// StingTools v4 MVP — Phase I.5 Provision-for-Voids IFC exporter.
//
// Writes an IFC4 Reference View of the document with the
// ExportProvisionForVoids option enabled, so every sleeve /
// provision-for-void family is materialised as an IfcProvisionForVoid
// element carrying Pset_ProvisionForVoid properties. Tekla Structures'
// Hole Reservation Manager consumes this exact format to build
// matching cuts on the structural side and returns an
// approved/rejected state via IFC.
//
// Output: <project>/_BIM_COORD/ifc/<date>_pfv.ifc
//
// Reference:
//   https://knowledge.autodesk.com/support/revit → IFC options
//   https://support.tekla.com/hole-reservation-manager
//   https://ifc43-docs.standards.buildingsmart.org/IFC/RELEASE/IFC4x3/HTML/lexical/Pset_ProvisionForVoid.htm

using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportPfvIfcCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            string outDir, path;
            try
            {
                var projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? Path.GetTempPath();
                outDir = Path.Combine(projDir, "_BIM_COORD", "ifc");
                Directory.CreateDirectory(outDir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                path = Path.Combine(outDir, $"sting_pfv_{stamp}.ifc");
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportPfvIfcCommand: path resolve", ex);
                message = ex.Message;
                return Result.Failed;
            }

            var opts = new IFCExportOptions
            {
                FileVersion      = IFCVersion.IFC4,
                SpaceBoundaryLevel = 0,
                ExportBaseQuantities = false,
                WallAndColumnSplitting = false,
            };
            // Key option — enables IfcProvisionForVoid emission.
            opts.AddOption("ExportProvisionForVoids", "true");
            // IFC4 Reference View MVD — the one Tekla accepts.
            opts.AddOption("ExportPartsAsBuildingElements", "false");
            opts.AddOption("ExportUserDefinedPsets",        "false");
            opts.AddOption("ExportIFCCommonPropertySets",   "true");
            opts.AddOption("ExportBoundingBox",             "false");
            opts.AddOption("ExportLinkedFiles",             "false");
            opts.AddOption("ExportSchedulesAsPsets",        "false");
            opts.AddOption("IncludeSiteElevation",          "false");
            opts.AddOption("UseActiveViewGeometry",         "false");

            using (var tx = new Transaction(doc, "STING v4 Export Provisions for Voids IFC"))
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
                    StingLog.Error("ExportPfvIfcCommand", ex);
                    message = ex.Message;
                    return Result.Failed;
                }
            }

            var panel = StingResultPanel.Create("v4 Provisions for Voids IFC");
            panel.SetSubtitle($"IFC4 written: {path}");
            panel.AddSection("DETAILS")
                 .Metric("Schema",  "IFC4 Reference View")
                 .Metric("Pset",    "Pset_ProvisionForVoid")
                 .Metric("Key",     "STING_SLEEVE_PFV_UUID");
            panel.AddSection("NEXT STEPS")
                 .Text("1. Open Tekla Structures → Manage Bulk Operations → Hole Reservation Manager")
                 .Text("2. Import IFC → file path above")
                 .Text("3. Approve/reject reservations; export returned IFC back to _BIM_COORD/ifc/")
                 .Text("4. Re-run STING v4 Place Sleeves to sync Tekla decisions");
            panel.Show();
            return Result.Succeeded;
        }
    }
}
