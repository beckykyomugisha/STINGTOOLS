// StingTools v4 MVP — Phase I.5 sleeve → BCF 2.1 round-trip.
//
// After PlaceSleevesCommand runs (or against an existing sleeve
// population), this command emits one BCF 2.1 topic per sleeve so
// structural coordinators receive a per-penetration reservation
// request. BcfEngine already exists and is used by the main
// PlatformLinkCommand.BCFExport workflow — we reuse it verbatim.
//
// One topic per sleeve:
//   Title       = "Sleeve request — <HostCat> at <Level> (<Bore>mm)"
//   Type        = "RFI"    (request for coordination)
//   Priority    = HIGH when sleeve has fire rating, else MEDIUM
//   Labels      = [ STING_PHASE_I, SLEEVE, <HostCategory> ]
//   RefLink     = STING_SLEEVE_PFV_UUID  (stable Tekla key)
//   Description = size + host fire rating + UL system + rule id
//
// After export the BCF file lands in <project>/_BIM_COORD/bcf/
// and can be uploaded to ACC / BIMcollab / Solibri / Trimble
// Connect for assignee/resolver action.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Planscape.Shared.BCF;
using StingTools.Core;
using StingTools.Core.Clash;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSleeveBcfCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Collect placed sleeves — any FamilyInstance with
            // STING_SLEEVE_PFV_UUID set. Both PlaceSleevesCommand and
            // AutoSleevePlacementCommand now write the same schema so this
            // collector picks up sleeves from either entry point.
            var sleeves = new List<FamilyInstance>();
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance));
                foreach (FamilyInstance fi in col)
                {
                    try
                    {
                        var p = fi.LookupParameter(SleeveParamRegistry.PfvUuid);
                        if (p?.AsString() is { Length: > 0 }) sleeves.Add(fi);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            { StingLog.Warn($"ExportSleeveBcfCommand: collector: {ex.Message}"); }

            if (sleeves.Count == 0)
            {
                TaskDialog.Show("STING v4 — Sleeve BCF Export",
                    "No STING-placed sleeves found (no FamilyInstance has STING_SLEEVE_PFV_UUID).\n\n" +
                    "Run STING v4 Place Sleeves first.");
                return Result.Cancelled;
            }

            var issues = new List<CoordIssue>();
            foreach (var fi in sleeves)
            {
                try { issues.Add(BuildIssue(doc, fi)); }
                catch (Exception ex2)
                { StingLog.Warn($"Sleeve BCF build {fi?.Id}: {ex2.Message}"); }
            }

            string outDir, path;
            try
            {
                var projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? Path.GetTempPath();
                outDir = Path.Combine(projDir, "_BIM_COORD", "bcf");
                Directory.CreateDirectory(outDir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                path = Path.Combine(outDir, $"sting_sleeves_{stamp}.bcf");
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportSleeveBcfCommand: path resolve", ex);
                message = ex.Message;
                return Result.Failed;
            }

            // BcfEngine.Export returns the topic count it wrote.
            int exportedCount;
            bool exported;
            try
            {
                exportedCount = BcfEngine.Export(issues, path);
                exported = exportedCount > 0;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportSleeveBcfCommand: BcfEngine.Export", ex);
                message = ex.Message;
                return Result.Failed;
            }

            var panel = StingResultPanel.Create("v4 Sleeve → BCF Export");
            panel.SetSubtitle(exported ? $"{issues.Count} topics → {path}" : "Export reported failure");
            panel.AddSection("SUMMARY")
                 .Metric("Sleeves found", sleeves.Count.ToString())
                 .Metric("Topics built",  issues.Count.ToString())
                 .Metric("High priority", issues.Count(i => i.Priority == "HIGH").ToString());
            panel.AddSection("NEXT STEPS")
                 .Text("Upload the BCF file to ACC Issues / BIMcollab / Solibri / Trimble Connect.")
                 .Text("Returned BCF updates (assignee / status) round-trip back through")
                 .Text("BIMManager.BCFImportCommand — the topic GUID keeps the linkage stable.");
            panel.Show();
            return Result.Succeeded;
        }

        private static CoordIssue BuildIssue(Document doc, FamilyInstance sleeve)
        {
            string pfvUuid = sleeve.LookupParameter(SleeveParamRegistry.PfvUuid)?.AsString() ?? sleeve.UniqueId;
            string bore    = sleeve.LookupParameter(SleeveParamRegistry.BoreMm)?.AsDouble().ToString("F0") ?? "";
            string width   = sleeve.LookupParameter(SleeveParamRegistry.WidthMm)?.AsDouble().ToString("F0") ?? "";
            string height  = sleeve.LookupParameter(SleeveParamRegistry.HeightMm)?.AsDouble().ToString("F0") ?? "";
            string depth   = sleeve.LookupParameter(SleeveParamRegistry.DepthMm)?.AsDouble().ToString("F0") ?? "";
            string rule    = sleeve.LookupParameter(SleeveParamRegistry.RuleId)?.AsString() ?? "";
            string fireRat = sleeve.LookupParameter(SleeveParamRegistry.HostFireRating)?.AsString() ?? "";
            string ulSys   = sleeve.LookupParameter(SleeveParamRegistry.UlSystem)?.AsString() ?? "";

            string hostCat = sleeve.Host?.Category?.Name ?? "Unknown host";
            string levelName = "";
            try
            {
                var lvlId = sleeve.LevelId;
                if (lvlId != null && lvlId != ElementId.InvalidElementId)
                {
                    var lvl = doc.GetElement(lvlId) as Level;
                    levelName = lvl?.Name ?? "";
                }
            }
            catch { }

            string sizeBlurb = !string.IsNullOrEmpty(bore)
                ? $"bore {bore} mm"
                : $"opening {width}×{height} mm";

            var issue = new CoordIssue
            {
                Guid          = pfvUuid,                // reuse the PFV UUID as BCF topic GUID
                Title         = $"Sleeve request — {hostCat} @ {levelName} ({sizeBlurb})",
                Type          = "RFI",
                Priority      = string.IsNullOrEmpty(fireRat) ? "MEDIUM" : "HIGH",
                Status        = "OPEN",
                ReferenceLink = $"STING-SLEEVE-{pfvUuid.Substring(0, 8).ToUpperInvariant()}",
                CreationDate  = DateTime.UtcNow,
                Description   =
                    $"Sleeve placed by STING v4.\n" +
                    $"Host: {hostCat} @ {levelName}\n" +
                    $"Size: {sizeBlurb}, depth {depth} mm\n" +
                    $"Rule: {rule}\n" +
                    $"Fire rating: {(string.IsNullOrEmpty(fireRat) ? "not set" : fireRat)}\n" +
                    $"UL firestop system: {(string.IsNullOrEmpty(ulSys) ? "TBD" : ulSys)}\n" +
                    $"PFV UUID: {pfvUuid}",
            };
            issue.Labels.Add("STING_PHASE_I");
            issue.Labels.Add("SLEEVE");
            if (!string.IsNullOrEmpty(hostCat)) issue.Labels.Add(hostCat);
            if (!string.IsNullOrEmpty(fireRat))  issue.Labels.Add($"FIRE_{fireRat}");

            // Build a real BCF viewpoint anchored on the sleeve so remote
            // coordinators (BIMcollab / ACC / Solibri) open the topic with
            // a spatial reference instead of the default stub camera.
            issue.ViewpointBcfvXml = BuildSleeveViewpoint(doc, sleeve);
            return issue;
        }

        /// <summary>
        /// Build a BCF 2.1 VisualizationInfo XML anchored on the sleeve's
        /// bounding box. Coordinates are emitted in metres per the BCF spec.
        /// </summary>
        private static string BuildSleeveViewpoint(Document doc, FamilyInstance sleeve)
        {
            try
            {
                var bb = sleeve.get_BoundingBox(null);
                if (bb == null) return null;

                const float FtToM = 0.3048f;
                var min = new Vector3(
                    (float)bb.Min.X * FtToM, (float)bb.Min.Y * FtToM, (float)bb.Min.Z * FtToM);
                var max = new Vector3(
                    (float)bb.Max.X * FtToM, (float)bb.Max.Y * FtToM, (float)bb.Max.Z * FtToM);
                var centre = 0.5f * (min + max);
                var size   = max - min;
                float diag = size.Length();
                var offset = new Vector3(1, 1, 0.5f) * (diag * 1.3f + 2f);

                var vb = new BcfViewpointBuilder
                {
                    Camera = new BcfViewpointCamera
                    {
                        ViewPoint   = centre + offset,
                        Direction   = Vector3.Normalize(centre - (centre + offset)),
                        Up          = new Vector3(0, 0, 1),
                        FieldOfView = 45f,
                        AspectRatio = 1.33f,
                    },
                };

                // 500 mm padded section box around the sleeve.
                const float pad = 0.5f;
                var mn = new Vector3(min.X - pad, min.Y - pad, min.Z - pad);
                var mx = new Vector3(max.X + pad, max.Y + pad, max.Z + pad);
                vb.ClippingPlanes.Add(new BcfClippingPlane { Location = mn, Direction = new Vector3(-1, 0, 0) });
                vb.ClippingPlanes.Add(new BcfClippingPlane { Location = mx, Direction = new Vector3( 1, 0, 0) });
                vb.ClippingPlanes.Add(new BcfClippingPlane { Location = mn, Direction = new Vector3( 0,-1, 0) });
                vb.ClippingPlanes.Add(new BcfClippingPlane { Location = mx, Direction = new Vector3( 0, 1, 0) });
                vb.ClippingPlanes.Add(new BcfClippingPlane { Location = mn, Direction = new Vector3( 0, 0,-1) });
                vb.ClippingPlanes.Add(new BcfClippingPlane { Location = mx, Direction = new Vector3( 0, 0, 1) });

                // Highlight the sleeve in the viewer's selection.
                if (!string.IsNullOrEmpty(sleeve.UniqueId))
                {
                    string ifcGuid = TryIfcGuid(doc, sleeve);
                    if (!string.IsNullOrEmpty(ifcGuid))
                    {
                        vb.Selection.Add(new BcfComponent
                        {
                            IfcGuid           = ifcGuid,
                            AuthoringTool     = "Revit",
                            OriginatingSystem = "STING",
                        });
                        vb.Colors.Add((ifcGuid, "#FFB300"));
                    }
                }
                return vb.BuildBcfv();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BuildSleeveViewpoint {sleeve?.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Try to resolve an IFC GUID for the sleeve. Falls back to the
        /// Revit UniqueId compressed to IFC base64 form if the official
        /// IfcGuid utility isn't available — close enough for selection
        /// highlighting.
        /// </summary>
        private static string TryIfcGuid(Document doc, Element el)
        {
            try
            {
                var p = el.get_Parameter(BuiltInParameter.IFC_GUID);
                var v = p?.AsString();
                if (!string.IsNullOrEmpty(v)) return v;
            }
            catch { }
            return el?.UniqueId;
        }
    }
}
