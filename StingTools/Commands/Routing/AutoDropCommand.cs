// StingTools v4 MVP — AutoDropCommand.
//
// Single IExternalCommand that inspects the current selection, groups
// elements by discipline (Electrical / Plumbing / HVAC) based on
// their Category and dispatches each group to the matching drop
// engine. Shows an aggregate result via StingResultPanel.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Routing;
using StingTools.UI;

namespace StingTools.Commands.Routing
{
    /// <summary>
    /// Shared options surface for Auto-drop, populated by the Routing
    /// tab UI before Execute is invoked. Static — read-only session
    /// singleton, mirrors the pattern used elsewhere in StingTools
    /// (e.g. TagConfig) so that UI wiring can be incremental.
    /// </summary>
    public static class AutoDropOptions
    {
        public static bool   SnapToCorridorBand { get; set; } = true;
        public static bool   EnforceSeparation  { get; set; } = true;
        public static double MaxSearchRadiusMm  { get; set; } = 3000.0;
        public static bool   IncludeElectrical  { get; set; } = true;
        public static bool   IncludePlumbing    { get; set; } = true;
        public static bool   IncludeHvac        { get; set; } = true;
        public static string ConduitInstallMethod { get; set; } = "CLIPPED";
        public static string DuctSeamType       { get; set; } = "A";
        public static string PipeHangerType     { get; set; } = "CLEVIS_ROD";

        /// <summary>When true the auto-drop engines emit hanger / support
        /// families after routing, per BS 5572 / MSS SP-58 spacing rules.</summary>
        public static bool   EmitSupports       { get; set; } = false;

        /// <summary>Mounting context passed to the support placer so it can
        /// choose the right hanger family variant (e.g. "SUSPENDED",
        /// "SURFACE_MOUNT", "CHASED").</summary>
        public static string MountingContext    { get; set; } = string.Empty;
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoDropCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            var selIds = uidoc.Selection.GetElementIds();
            if (selIds == null || selIds.Count == 0)
            {
                TaskDialog.Show("STING v4 — Auto-drop",
                    "Select one or more fixtures before running Auto-drop.");
                return Result.Cancelled;
            }

            var byDisc = new Dictionary<string, List<Element>>
            {
                { "Electrical", new List<Element>() },
                { "Plumbing",   new List<Element>() },
                { "HVAC",       new List<Element>() }
            };

            foreach (var id in selIds)
            {
                var el = doc.GetElement(id);
                if (el?.Category == null) continue;
                string disc = DisciplineFor((BuiltInCategory)el.Category.Id.Value);
                if (disc != null && byDisc.ContainsKey(disc)) byDisc[disc].Add(el);
            }

            if (byDisc.Values.All(v => v.Count == 0))
            {
                TaskDialog.Show("STING v4 — Auto-drop",
                    "Selection contains no electrical / plumbing / HVAC fixtures.");
                return Result.Cancelled;
            }

            var allResults = new List<DropResult>();
            try
            {
                if (AutoDropOptions.IncludeElectrical && byDisc["Electrical"].Count > 0)
                {
                    var eng = new AutoConduitDrop(doc)
                    {
                        SnapToCorridorBand = AutoDropOptions.SnapToCorridorBand,
                        EnforceSeparation  = AutoDropOptions.EnforceSeparation,
                        SearchRadiusMm     = AutoDropOptions.MaxSearchRadiusMm,
                        InstallMethod      = AutoDropOptions.ConduitInstallMethod,
                    };
                    allResults.Add(eng.Execute(byDisc["Electrical"]));
                }
                if (AutoDropOptions.IncludePlumbing && byDisc["Plumbing"].Count > 0)
                {
                    var eng = new AutoPipeDrop(doc)
                    {
                        SnapToCorridorBand = AutoDropOptions.SnapToCorridorBand,
                        EnforceSeparation  = AutoDropOptions.EnforceSeparation,
                        SearchRadiusMm     = AutoDropOptions.MaxSearchRadiusMm,
                        HangerType         = AutoDropOptions.PipeHangerType,
                    };
                    allResults.Add(eng.Execute(byDisc["Plumbing"]));
                }
                if (AutoDropOptions.IncludeHvac && byDisc["HVAC"].Count > 0)
                {
                    var eng = new AutoDuctDrop(doc)
                    {
                        SnapToCorridorBand = AutoDropOptions.SnapToCorridorBand,
                        EnforceSeparation  = AutoDropOptions.EnforceSeparation,
                        SearchRadiusMm     = AutoDropOptions.MaxSearchRadiusMm,
                        SeamType           = AutoDropOptions.DuctSeamType,
                    };
                    allResults.Add(eng.Execute(byDisc["HVAC"]));
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("AutoDropCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            // Phase 178d — auto-stamp penetrations on every newly-routed
            // segment (slab + wall + beam hosts). Mirrors the path
            // ConduitAutoRouteCommand has had since the FRP wiring
            // landed, but generalised so plumbing pipes and HVAC ducts
            // also pick up firestop placement + structural review.
            try
            {
                var routedIds = new List<ElementId>();
                foreach (var dr in allResults)
                    if (dr?.CreatedIds != null) routedIds.AddRange(dr.CreatedIds);
                if (routedIds.Count > 0)
                {
                    using (var tx = new Transaction(doc, "STING Penetration Sweep"))
                    {
                        tx.Start();
                        var slab = StingTools.Core.Routing.SlabPenetrationDetector.Detect(doc, routedIds);
                        var wall = StingTools.Core.Routing.WallPenetrationDetector.Detect(doc, routedIds);
                        var beam = StingTools.Core.Routing.BeamPenetrationDetector.Detect(doc, routedIds);
                        var all = new List<StingTools.Core.Routing.PenetrationRecord>(slab.Count + wall.Count + beam.Count);
                        all.AddRange(slab); all.AddRange(wall); all.AddRange(beam);
                        var place = StingTools.Core.Routing.FrpPenetrationPlacer.Place(doc, all);
                        tx.Commit();
                        if (allResults.Count > 0)
                        {
                            allResults[0].Warnings.Add(
                                $"Penetrations: slab={slab.Count} wall={wall.Count} beam={beam.Count} → " +
                                $"placed={place.Placed} updated={place.Stamped} skipped={place.Skipped} errors={place.Errors}.");
                            int sFail = beam.FindAll(b => b.StructuralFlag == "STRUCT_FAIL").Count;
                            if (sFail > 0)
                                allResults[0].Warnings.Add(
                                    $"⚠ {sFail} beam penetration(s) violate AISC DG2 / BS EN 1992 — reroute before fabrication.");
                        }
                    }
                }
            }
            catch (Exception pex) { StingLog.Warn($"AutoDrop penetration sweep: {pex.Message}"); }

            // Phase 139.28 — auto-emit supports per BS 5572 / MSS SP-58.
            // Each engine returns a DropResult with CreatedIds; we pass
            // those into RoutingSupportPlacer with a synthetic SUSPENDED
            // rule so HangerPlacementEngine + HangerFamilyResolver can
            // place real FamilyInstances inside the same transaction.
            if (AutoDropOptions.EmitSupports)
            {
                using (var sx = new Transaction(doc, "STING v4 Auto-drop supports"))
                {
                    try
                    {
                        sx.Start();
                        int totalPlaced = 0, totalMissed = 0;
                        var aggregateWarnings = new List<string>();
                        foreach (var dr in allResults)
                        {
                            if (dr?.CreatedIds == null || dr.CreatedIds.Count == 0) continue;
                            var syntheticRule = new StingTools.Core.Placement.PlacementRule
                            {
                                RuleId           = "auto-drop-supports",
                                CategoryFilter   = "",
                                MountingContext  = "SUSPENDED",
                                EmitSupports     = true,
                                // Material / diameter left empty — RoutingSupportPlacer
                                // falls back to family-side detection in
                                // HangerPlacementEngine.BuildQuery.
                            };
                            var supportRes = StingTools.Core.Calc.RoutingSupportPlacer.PlaceForRoute(
                                doc, syntheticRule, dr.CreatedIds);
                            totalPlaced += supportRes.SupportsPlaced;
                            totalMissed += supportRes.FamilyMissCount;
                            foreach (var w in supportRes.Warnings)
                                if (!aggregateWarnings.Contains(w)) aggregateWarnings.Add(w);
                        }
                        sx.Commit();
                        if (totalPlaced > 0 && allResults.Count > 0)
                        {
                            allResults[0].Warnings.Add(
                                $"Auto-drop supports: emitted {totalPlaced} hanger(s) per BS 5572 / MSS SP-58.");
                        }
                        if (totalMissed > 0 && allResults.Count > 0)
                        {
                            allResults[0].Warnings.Add(
                                $"Auto-drop supports: {totalMissed} support(s) planned but no hanger family loaded — " +
                                "load STING_HANGER_GENERIC.rfa or any Anvil/B-Line/Unistrut family.");
                        }
                        if (allResults.Count > 0)
                        {
                            foreach (var w in aggregateWarnings)
                                if (!allResults[0].Warnings.Contains(w)) allResults[0].Warnings.Add(w);
                        }
                    }
                    catch (Exception sex)
                    {
                        if (sx.HasStarted() && !sx.HasEnded()) sx.RollBack();
                        StingLog.Warn($"AutoDropCommand support emit: {sex.Message}");
                        if (allResults.Count > 0)
                            allResults[0].Warnings.Add($"Auto-drop supports failed: {sex.Message}");
                    }
                }
            }

            ShowResult(allResults);
            return Result.Succeeded;
        }

        private static string DisciplineFor(BuiltInCategory bic)
        {
            switch (bic)
            {
                case BuiltInCategory.OST_ElectricalFixtures:
                case BuiltInCategory.OST_ElectricalEquipment:
                case BuiltInCategory.OST_LightingFixtures:
                case BuiltInCategory.OST_LightingDevices:
                case BuiltInCategory.OST_CommunicationDevices:
                case BuiltInCategory.OST_DataDevices:
                case BuiltInCategory.OST_SecurityDevices:
                case BuiltInCategory.OST_FireAlarmDevices:
                case BuiltInCategory.OST_NurseCallDevices:
                    return "Electrical";

                case BuiltInCategory.OST_PlumbingFixtures:
                case BuiltInCategory.OST_PlumbingEquipment:
                case BuiltInCategory.OST_PipeAccessory:
                case BuiltInCategory.OST_Sprinklers:
                    return "Plumbing";

                case BuiltInCategory.OST_DuctTerminal:
                case BuiltInCategory.OST_DuctAccessory:
                case BuiltInCategory.OST_MechanicalEquipment:
                    return "HVAC";
            }
            return null;
        }

        private void ShowResult(List<DropResult> results)
        {
            var panel = StingResultPanel.Create("v4 Auto-drop");
            panel.SetSubtitle("Auto-drop across Electrical / Plumbing / HVAC");

            foreach (var r in results)
            {
                panel.AddSection(r.Discipline.ToUpperInvariant())
                     .Metric("Created",       r.CreatedIds.Count.ToString())
                     .Metric("Connected",     r.ConnectedCount.ToString())
                     .Metric("Takeoffs",      r.TakeoffCount.ToString())
                     .Metric("Skipped",       r.SkippedCount.ToString())
                     .Metric("Failed",        r.FailedCount.ToString());
                if (r.Warnings.Count > 0)
                {
                    foreach (var w in r.Warnings.Take(10)) panel.Text(w);
                    if (r.Warnings.Count > 10) panel.Text($"(+{r.Warnings.Count - 10} more — see StingLog)");
                }
            }
            panel.Show();
        }
    }
}
