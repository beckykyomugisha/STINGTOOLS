// StingTools — Lighting grid placement command.
//
// For every selected (or all) Room, runs LightingGridCalculator to:
//   1. Classify the room via ROOM_TYPE_CLASSIFIER.csv.
//   2. Look up the BS EN 12464-1 / CIBSE LG7 target lux from
//      LUX_TARGETS_EN12464.csv.
//   3. Compute fixture count + grid spacing using the lumen-method
//      with utilisation + maintenance factors (UF × MF).
//   4. Place LightingFixtures family instances on the grid points.
//   5. Stamp ELC_LIGHTING_TARGET_LUX_TXT and ELC_LIGHTING_GRID_SPC_MM
//      on each placed fixture so the audit/QA tooling can re-verify
//      what design intent each fixture serves.
//
// The command first asks the user for placement scope (selection vs
// project) and shows a dry-run preview before any writes.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.UI;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LightingGridCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // ── Resolve target rooms ─────────────────────────────────
            var rooms = ResolveRooms(uidoc, doc);
            if (rooms.Count == 0)
            {
                TaskDialog.Show("STING Lighting Grid",
                    "No rooms found. Place rooms in the model or select them first.");
                return Result.Cancelled;
            }

            // ── Resolve a luminaire family symbol ────────────────────
            var symbol = ResolveLuminaire(doc);
            if (symbol == null)
            {
                TaskDialog.Show("STING Lighting Grid",
                    "No Lighting Fixtures family loaded. Load a luminaire family before running this command.");
                return Result.Cancelled;
            }

            // ── Tune calculator from family-level photometrics ───────
            // ELC_PHOTO_LUMENS is the canonical lumen output param
            // (see ParamRegistry.cs:573). When the chosen luminaire
            // carries a real value the lumen-method count will be
            // honest; otherwise we fall back to the 4000 lm default
            // and surface that fact in the result panel.
            var calc = new LightingGridCalculator();
            string photoFilePath = null;
            bool photoUsed = false;
            bool ufFromFamily = false, mfFromFamily = false;
            try
            {
                double famLumens = ReadDoubleParam(symbol, ParamRegistry.ELC_PHOTO_LUMENS);
                if (famLumens > 0)
                {
                    calc.DefaultLumensPerLuminaire = famLumens;
                    photoUsed = true;
                }
                photoFilePath = ParameterHelpers.GetString(symbol, ParamRegistry.ELC_PHOTO_FILE_PATH);

                // Family-level utilisation factor (UF) and maintenance factor (MF)
                // — when set on the type, override the calculator defaults so
                // designers can encode reflectance/maintenance assumptions per
                // luminaire family without editing C# constants.
                double famUf = ReadDoubleParam(symbol, "ELC_LIGHTING_UF_FACTOR");
                if (famUf > 0 && famUf <= 1.0)
                { calc.UtilisationFactor = famUf; ufFromFamily = true; }
                double famMf = ReadDoubleParam(symbol, "ELC_LIGHTING_MF_FACTOR");
                if (famMf > 0 && famMf <= 1.0)
                { calc.MaintenanceFactor = famMf; mfFromFamily = true; }
            }
            catch (Exception ex) { StingLog.Warn($"LightingGrid photo lookup: {ex.Message}"); }

            // ── Compute grids (rule-aware, dry run) ──────────────────
            // Synthesised rule pipes the placement engine's safety nets
            // (ceiling-tile snap, structural fixing check, sprinkler
            // separation, min-spacing, uniformity ratio) through the
            // calculator. Without a rule those passes are silently
            // skipped — defeating the point of using LightingGridCalculator.
            var rule = new PlacementRule
            {
                RuleId                = "lighting-grid-cmd",
                RuleKind              = PlacementRuleKind.Density,
                CategoryFilter        = "Lighting Fixtures",
                CeilingTileSnap       = true,
                StructuralFixingCheck = true,
                MinSpacingMm          = 1200.0,
                MinUniformityRatio    = 0.40,
            };

            var plans = new List<RoomPlan>();
            int totalFixtures = 0;
            int totalBlocked = 0;
            foreach (var room in rooms)
            {
                LightingGridResult gr;
                try { gr = calc.Compute(room, rule); }
                catch (Exception ex2)
                {
                    StingLog.Warn($"LightingGrid Compute room {room?.Id}: {ex2.Message}");
                    continue;
                }

                // Reject any computed point whose XY collides with a ceiling
                // obstruction (diffuser / sprinkler / detector / speaker /
                // furniture / casework / suspended duct or pipe). Buffer
                // defaults to CIBSE Guide B4 §3.6 350 mm. Without this the
                // calculator may site a luminaire directly under a sprinkler
                // head — which violates BS 5306 ≥ 600 mm separation and
                // makes the fixture impossible to commission.
                int blocked = 0;
                try
                {
                    var exclusions = ObstructionIndex.BuildForRoom(room.Document, room);
                    if (exclusions.Count > 0)
                    {
                        int before = gr.Points.Count;
                        var kept = ObstructionIndex.FilterPoints(gr.Points, exclusions);
                        blocked = before - kept.Count;
                        if (blocked > 0)
                        {
                            gr.Points.Clear();
                            foreach (var p in kept) gr.Points.Add(p);
                            gr.FixturesPlaced = gr.Points.Count;
                            gr.Warnings.Add($"ObstructionIndex rejected {blocked} of {before} grid points (sprinklers / diffusers / casework).");
                        }
                    }
                }
                catch (Exception ex3) { StingLog.Warn($"ObstructionIndex room {room?.Id}: {ex3.Message}"); }

                plans.Add(new RoomPlan { Room = room, Result = gr });
                totalFixtures += gr.FixturesPlaced;
                totalBlocked  += blocked;
            }

            // ── Confirm ──────────────────────────────────────────────
            string lumensLine = photoUsed
                ? $"Lumens: {calc.DefaultLumensPerLuminaire:F0} lm (from family ELC_PHOTO_LUMENS)"
                : $"Lumens: {calc.DefaultLumensPerLuminaire:F0} lm (default — luminaire has no ELC_PHOTO_LUMENS; lumen-method count is approximate)";
            string photoLine = !string.IsNullOrEmpty(photoFilePath) && File.Exists(photoFilePath)
                ? $"Photometric file: {Path.GetFileName(photoFilePath)} (.ies/.ldt resolved)"
                : "Photometric file: not set — run 'Assign Photometric' before final design";
            string ufmfLine =
                $"UF {calc.UtilisationFactor:F2} ({(ufFromFamily ? "family ELC_LIGHTING_UF_FACTOR" : "default")}), " +
                $"MF {calc.MaintenanceFactor:F2} ({(mfFromFamily ? "family ELC_LIGHTING_MF_FACTOR" : "default")})";

            var confirm = new TaskDialog("STING Lighting Grid — Preview")
            {
                MainInstruction = $"Place {totalFixtures} luminaire(s) across {plans.Count} room(s)?",
                MainContent =
                    $"Family: {symbol.FamilyName} : {symbol.Name}\n" +
                    $"{lumensLine}\n{photoLine}\n{ufmfLine}\n\n" +
                    "Targets follow BS EN 12464-1 / CIBSE LG7 via LUX_TARGETS_EN12464.csv.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (confirm.Show() != TaskDialogResult.Yes)
            {
                ShowResult(plans, placed: 0, dryRun: true, doc, totalBlocked);
                return Result.Cancelled;
            }

            // ── Place ────────────────────────────────────────────────
            int placed = 0, failed = 0;
            using (var tx = new Transaction(doc, "STING Lighting Grid Placement"))
            {
                tx.Start();
                if (!symbol.IsActive) { try { symbol.Activate(); doc.Regenerate(); } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); } }
                foreach (var plan in plans)
                {
                    var lvl = doc.GetElement(plan.Room.LevelId) as Level;
                    string lux = plan.Result.TargetLux.ToString("F0");
                    string spc = plan.Result.SpacingXMm > 0
                        ? $"{plan.Result.SpacingXMm:F0}x{plan.Result.SpacingYMm:F0}"
                        : "";
                    foreach (var pt in plan.Result.Points)
                    {
                        try
                        {
                            FamilyInstance fi;
                            if (lvl != null)
                            {
                                fi = doc.Create.NewFamilyInstance(pt, symbol, lvl,
                                    StructuralType.NonStructural);
                            }
                            else
                            {
                                fi = doc.Create.NewFamilyInstance(pt, symbol,
                                    StructuralType.NonStructural);
                            }
                            if (fi != null)
                            {
                                placed++;
                                ParameterHelpers.SetString(fi, "ELC_LIGHTING_TARGET_LUX_TXT", lux, overwrite: true);
                                if (!string.IsNullOrEmpty(spc))
                                    ParameterHelpers.SetString(fi, "ELC_LIGHTING_GRID_SPC_MM", spc, overwrite: true);
                                ParameterHelpers.SetString(fi, "ELC_LIGHTING_ROOM_TYPE", plan.Result.RoomTypeCode, overwrite: false);
                            }
                        }
                        catch (Exception ex3)
                        {
                            failed++;
                            StingLog.Warn($"LightingGrid place at room {plan.Room.Id}: {ex2.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            try { ActionAuditLog.Record("Lighting_GridPlace",
                $"rooms={plans.Count} placed={placed} failed={failed}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            ShowResult(plans, placed, dryRun: false, doc, totalBlocked);
            return Result.Succeeded;
        }

        private static List<Room> ResolveRooms(UIDocument uidoc, Document doc)
        {
            var sel = uidoc?.Selection?.GetElementIds() ?? new List<ElementId>();
            var selRooms = sel
                .Select(id => doc.GetElement(id))
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .ToList();
            if (selRooms.Count > 0) return selRooms;

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .ToList();
        }

        private static FamilySymbol ResolveLuminaire(Document doc)
        {
            var symbols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsElementType()
                .OfType<FamilySymbol>()
                .ToList();
            if (symbols.Count == 0) return null;

            // Prefer a 600x600 office panel if present — that's what the
            // calculator's default lumen value (4000 lm) most closely matches.
            var preferred = symbols.FirstOrDefault(s =>
                (s.FamilyName ?? "").IndexOf("600", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (s.Name       ?? "").IndexOf("LED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (s.FamilyName ?? "").IndexOf("Recessed", StringComparison.OrdinalIgnoreCase) >= 0);
            return preferred ?? symbols[0];
        }

        // Audit every loaded LightingFixture symbol. Returns counts so
        // the result panel can warn when the project's fixture library
        // isn't ready for a Dialux/Relux radiosity round-trip.
        private static (int total, int withLumens, int withPhotoFile) AuditFixtureLibrary(Document doc)
        {
            int total = 0, lumens = 0, photo = 0;
            try
            {
                foreach (var fs in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .WhereElementIsElementType()
                    .OfType<FamilySymbol>())
                {
                    total++;
                    if (ReadDoubleParam(fs, ParamRegistry.ELC_PHOTO_LUMENS) > 0) lumens++;
                    string p = ParameterHelpers.GetString(fs, ParamRegistry.ELC_PHOTO_FILE_PATH);
                    if (!string.IsNullOrEmpty(p)) photo++;
                }
            }
            catch (Exception ex) { StingLog.Warn($"AuditFixtureLibrary: {ex.Message}"); }
            return (total, lumens, photo);
        }

        private static void ShowResult(List<RoomPlan> plans, int placed, bool dryRun, Document doc, int blockedByObstructions)
        {
            int totalRequired = plans.Sum(p => p.Result.FixturesRequired);
            int totalPlanned  = plans.Sum(p => p.Result.FixturesPlaced);
            var audit = AuditFixtureLibrary(doc);

            var panel = StingResultPanel.Create(dryRun ? "Lighting Grid — Preview" : "Lighting Grid Placement");
            panel.SetSubtitle($"BS EN 12464-1 / CIBSE LG7 — {plans.Count} rooms, {totalPlanned} luminaires planned");
            panel.AddSection("SUMMARY")
                 .Metric("Rooms processed", plans.Count.ToString())
                 .Metric("Required (lumen-method)", totalRequired.ToString())
                 .MetricHighlight("Plan: planned points", totalPlanned.ToString());
            if (blockedByObstructions > 0)
                panel.MetricWarn("Blocked by obstructions", blockedByObstructions.ToString(),
                    "diffusers / sprinklers / detectors / casework — CIBSE Guide B4 §3.6 350 mm clearance");
            if (!dryRun) panel.Metric("Actually placed", placed.ToString());

            // Photometric library audit — surfaces the gap that
            // turns a lumen-method estimate into a real BS EN 12464-1
            // proof. Empty values block a Dialux round-trip.
            panel.AddSection("PHOTOMETRIC LIBRARY")
                 .Metric("Lighting families loaded", audit.total.ToString())
                 .Metric("With ELC_PHOTO_LUMENS",     audit.withLumens.ToString(),
                          audit.withLumens < audit.total ? "missing — UF/MF tuning approximate" : "OK")
                 .Metric("With ELC_PHOTO_FILE_PATH",  audit.withPhotoFile.ToString(),
                          audit.withPhotoFile < audit.total ? "missing — Dialux export will warn" : "OK");

            if (plans.Count > 0)
            {
                panel.AddSection("BY ROOM");
                foreach (var p in plans.OrderByDescending(x => x.Result.FixturesPlaced).Take(30))
                {
                    string nm = "";
                    try { nm = p.Room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? p.Room.Name; }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); nm = p.Room.Id.ToString(); }
                    panel.Metric(nm,
                        p.Result.FixturesPlaced.ToString(),
                        $"{p.Result.RoomTypeCode} @ {p.Result.TargetLux:F0} lx  ({p.Result.RoomAreaM2:F1} m²)");
                }
                if (plans.Count > 30) panel.Text($"… {plans.Count - 30} more rooms.");
            }

            var allWarn = plans.SelectMany(p => p.Result.Warnings).Distinct().Take(15).ToList();
            if (allWarn.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in allWarn) panel.Text(w);
            }

            panel.AddSection("NEXT STEPS")
                 .Text("Run 'Assign Photometric' to bind .ies/.ldt files to luminaire families that lack them.")
                 .Text("Run 'Auto-assign Circuits' to bind these fixtures to panels.")
                 .Text("Run 'Validation_BS7671' to check conduit fill and bend counts feeding them.")
                 .Text("For final compliance, run 'Dialux Round-trip' to export to Dialux for radiosity, then re-import results.");
            panel.Show();
        }

        private static double ReadDoubleParam(Element el, string paramName)
        {
            try
            {
                string s = ParameterHelpers.GetString(el, paramName);
                if (!string.IsNullOrEmpty(s) && double.TryParse(s,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out double d) && d > 0)
                    return d;
                var p = el?.LookupParameter(paramName);
                if (p != null && p.StorageType == StorageType.Double && p.AsDouble() > 0)
                    return p.AsDouble();
                if (p != null && p.StorageType == StorageType.Integer && p.AsInteger() > 0)
                    return p.AsInteger();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }

        private class RoomPlan
        {
            public Room Room;
            public LightingGridResult Result;
        }
    }
}
