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

            // ── Compute grids (dry run) ──────────────────────────────
            var calc = new LightingGridCalculator();
            var plans = new List<RoomPlan>();
            int totalFixtures = 0;
            foreach (var room in rooms)
            {
                LightingGridResult gr;
                try { gr = calc.Compute(room); }
                catch (Exception ex)
                {
                    StingLog.Warn($"LightingGrid Compute room {room?.Id}: {ex.Message}");
                    continue;
                }
                plans.Add(new RoomPlan { Room = room, Result = gr });
                totalFixtures += gr.FixturesPlaced;
            }

            // ── Confirm ──────────────────────────────────────────────
            var confirm = new TaskDialog("STING Lighting Grid — Preview")
            {
                MainInstruction = $"Place {totalFixtures} luminaire(s) across {plans.Count} room(s)?",
                MainContent =
                    $"Family: {symbol.FamilyName} : {symbol.Name}\n\n" +
                    "Targets follow BS EN 12464-1 / CIBSE LG7 via LUX_TARGETS_EN12464.csv.\n" +
                    "Defaults: 4000 lm/luminaire, UF 0.60, MF 0.80 — adjust the calculator " +
                    "constants if your project uses different reflectances or maintenance regimes.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };
            if (confirm.Show() != TaskDialogResult.Yes)
            {
                ShowResult(plans, placed: 0, dryRun: true);
                return Result.Cancelled;
            }

            // ── Place ────────────────────────────────────────────────
            int placed = 0, failed = 0;
            using (var tx = new Transaction(doc, "STING Lighting Grid Placement"))
            {
                tx.Start();
                if (!symbol.IsActive) { try { symbol.Activate(); doc.Regenerate(); } catch { } }
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
                        catch (Exception ex)
                        {
                            failed++;
                            StingLog.Warn($"LightingGrid place at room {plan.Room.Id}: {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            try { ActionAuditLog.Record("Lighting_GridPlace",
                $"rooms={plans.Count} placed={placed} failed={failed}"); }
            catch (Exception ex) { StingLog.Warn($"audit: {ex.Message}"); }
            try { ComplianceScan.InvalidateCache(); } catch { }

            ShowResult(plans, placed, dryRun: false);
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

        private static void ShowResult(List<RoomPlan> plans, int placed, bool dryRun)
        {
            int totalRequired = plans.Sum(p => p.Result.FixturesRequired);
            int totalPlanned  = plans.Sum(p => p.Result.FixturesPlaced);

            var panel = StingResultPanel.Create(dryRun ? "Lighting Grid — Preview" : "Lighting Grid Placement");
            panel.SetSubtitle($"BS EN 12464-1 / CIBSE LG7 — {plans.Count} rooms, {totalPlanned} luminaires planned");
            panel.AddSection("SUMMARY")
                 .Metric("Rooms processed", plans.Count.ToString())
                 .Metric("Required (lumen-method)", totalRequired.ToString())
                 .MetricHighlight("Plan: planned points", totalPlanned.ToString());
            if (!dryRun) panel.Metric("Actually placed", placed.ToString());

            if (plans.Count > 0)
            {
                panel.AddSection("BY ROOM");
                foreach (var p in plans.OrderByDescending(x => x.Result.FixturesPlaced).Take(30))
                {
                    string nm = "";
                    try { nm = p.Room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? p.Room.Name; }
                    catch { nm = p.Room.Id.ToString(); }
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
                 .Text("Run 'Auto-assign Circuits' to bind these fixtures to panels.")
                 .Text("Run 'Validation_BS7671' to check conduit fill and bend counts feeding them.")
                 .Text("Tweak DefaultLumensPerLuminaire / UtilisationFactor in LightingGridCalculator if your luminaires differ.");
            panel.Show();
        }

        private class RoomPlan
        {
            public Room Room;
            public LightingGridResult Result;
        }
    }
}
