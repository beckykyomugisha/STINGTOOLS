// StingTools — Drawing Template Manager
//
// DrawingRouteRequests — the (discipline, docType) keys code asks the routing
// table for, in one Revit-free place.
//
// Routing is how a command asks "which drawing type for discipline X / docType Y"
// (DrawingDispatcher.Resolve). ~45 routing docTypes in STING_DRAWING_TYPES.json
// were never asked for: the schematic and schedule commands hard-coded their
// drawing-type ids instead, so the rules did nothing and a project override that
// re-pointed them was ignored. The commands now route through the keys below
// (with the shipped id as the fall-back when a project's table routes the key
// nowhere), and StingTools.Tags.Tests checks every shipped routing docType is
// either asked for here or listed in Unrequested with the reason it is kept.
//
// Revit-free on purpose: the test compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>One routing key a command asks for, and the id it falls back to.</summary>
    public sealed class DrawingRouteRequest
    {
        public DrawingRouteRequest(string discipline, string docType, string fallbackDrawingTypeId,
            string caller, string contextTag = null)
        {
            Discipline = discipline;
            DocType = docType;
            FallbackDrawingTypeId = fallbackDrawingTypeId;
            Caller = caller;
            ContextTag = contextTag;
        }

        public string Discipline { get; }
        public string DocType { get; }
        /// <summary>The shipped type the key routes to; used when the table routes it nowhere.</summary>
        public string FallbackDrawingTypeId { get; }
        /// <summary>The command tag(s) that ask — shown in messages.</summary>
        public string Caller { get; }
        /// <summary>Sheet context tag that keeps this sheet apart from the type's other sheets.</summary>
        public string ContextTag { get; }
    }

    public static class DrawingRouteRequests
    {
        // ── Diagram generators: each draws a drafting view and places it on its type's sheet ──
        public static readonly DrawingRouteRequest Sld =
            new DrawingRouteRequest("E", "SLD", "elec-sld-A1-NTS", "SLD_Generate");
        public static readonly DrawingRouteRequest Riser =
            new DrawingRouteRequest("E", "RISER", "elec-riser-A3-1to200", "SLD_RiserDiagram", "RISER-DIAGRAM");
        public static readonly DrawingRouteRequest FireAlarmSchematic =
            new DrawingRouteRequest("E", "FIRE_ALARM_SCHEMATIC", "elec-fire-alarm-schematic-A1", "FireAlarm_Schematic");
        public static readonly DrawingRouteRequest MgpsSchematic =
            new DrawingRouteRequest("MG", "SCHEMATIC", "health-medgas-schem-A1", "MGPS_Schematic");
        public static readonly DrawingRouteRequest LpsSchematic =
            new DrawingRouteRequest("E", "LPS_SCHEMATIC", "elec-lps-schematic-A1-NTS", "LPS_Schematic");
        public static readonly DrawingRouteRequest EarthingSchematic =
            new DrawingRouteRequest("E", "EARTHING_SCHEMATIC", "elec-earthing-schematic-A1-NTS", "Earthing_Diagram");
        public static readonly DrawingRouteRequest PanelDoorDiagram =
            new DrawingRouteRequest("E", "PANEL_DOOR_DIAGRAM", "elec-panel-door-diagram-A3-NTS", "Panel_DoorDiagram");
        // Plumb_SupplySchematic draws the domestic cold water network only (the pipes
        // whose system is classified Domestic Cold Water), so it is the DCW schematic;
        // DHW_SCHEMATIC has no generator.
        public static readonly DrawingRouteRequest DcwSchematic =
            new DrawingRouteRequest("P", "DCW_SCHEMATIC", "plumb-dcw-schematic-A1-NTS", "Plumb_SupplySchematic");
        public static readonly DrawingRouteRequest DrainageSchematic =
            new DrawingRouteRequest("P", "DRAINAGE_SCHEMATIC", "plumb-drainage-schematic-A1", "Plumb_DrainageSchematic");
        // Stamped "elec-arc-flash-labels" before any drawing type had that id.
        public static readonly DrawingRouteRequest ArcFlashLabels =
            new DrawingRouteRequest("E", "ARC_FLASH_LABELS", "elec-arc-flash-labels", "Elec_ArcFlashLabels");

        /// <summary>The diagram generators, in the order a production run draws them.</summary>
        public static readonly IReadOnlyList<DrawingRouteRequest> DiagramGenerators = new[]
        {
            Sld, Riser, FireAlarmSchematic, MgpsSchematic, LpsSchematic, EarthingSchematic, PanelDoorDiagram,
            DcwSchematic, DrainageSchematic, ArcFlashLabels,
        };

        // ── Drawn and stamped, but not placed: the view is in model coordinates, so a
        //    person chooses its scale and puts it on a sheet of the type ──
        // Stamped "elec-lps-coverage-A3" before any drawing type had that id.
        public static readonly DrawingRouteRequest LpsCoverage =
            new DrawingRouteRequest("E", "LPS_COVERAGE", "elec-lps-coverage-A3", "LPS_PlanVisualise");

        /// <summary>Generators that draw and stamp a view but leave placing it to a person.</summary>
        public static readonly IReadOnlyList<DrawingRouteRequest> UnplacedGenerators = new[] { LpsCoverage };

        // ── Schedules: an engine makes the schedule view; it is stamped with, and placed on, its type's sheet ──
        // The panel-schedule sheets are found again by their stamp, so every command that
        // stamps or places a panel schedule asks for this one key (see StampIds).
        public static readonly DrawingRouteRequest PanelSchedule =
            new DrawingRouteRequest("E", "ELEC_PANEL_SCHEDULE", "elec-panel-schedule-A3",
                "Panel_PlaceOnSheets / Panel_BatchSchedules / FaultCurrentSchedule / VoltageDropSchedule");

        // Stamped "elec-arc-flash-schedule" before any drawing type had that id.
        public static readonly DrawingRouteRequest ArcFlashSchedule =
            new DrawingRouteRequest("E", "ARC_FLASH_SCHEDULE", "elec-arc-flash-schedule", "Elec_ArcFlashSched");

        /// <summary>The schedule requests (not diagram generators: production never skips their types).</summary>
        public static readonly IReadOnlyList<DrawingRouteRequest> Schedules = new[] { PanelSchedule, ArcFlashSchedule };

        /// <summary>Every request this file declares.</summary>
        public static IEnumerable<DrawingRouteRequest> All => DiagramGenerators.Concat(UnplacedGenerators).Concat(Schedules);

        /// <summary>
        /// The drawing-type ids a sheet or view made for <paramref name="req"/> may carry:
        /// the id routing gives now, then the shipped fall-back when that differs. A sheet
        /// stamped before a project re-routed the key carries the shipped id; looking up
        /// only the routed id would miss it and mint a duplicate.
        /// </summary>
        public static IReadOnlyList<string> StampIds(string routedId, DrawingRouteRequest req)
        {
            var ids = new List<string>();
            if (!string.IsNullOrWhiteSpace(routedId)) ids.Add(routedId);
            string fb = req?.FallbackDrawingTypeId;
            if (!string.IsNullOrWhiteSpace(fb) && !ids.Any(i => string.Equals(i, fb, StringComparison.OrdinalIgnoreCase)))
                ids.Add(fb);
            return ids;
        }

        /// <summary>
        /// docTypes other callers pass to DrawingDispatcher.Resolve (literal or a
        /// DrawingPurpose value, matched case-insensitively): per-level production
        /// (PLAN / RCP), Doc Automation (SECTION / ELEVATION), the fabrication
        /// composer (SPOOL), MEP coordination and MepViewProducer (COORD / MEP /
        /// DRAINAGE / POWER), Structural post-processing (PLAN).
        /// </summary>
        public static readonly IReadOnlyCollection<string> OtherCallerDocTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PLAN", "RCP", "SECTION", "ELEVATION", "SPOOL", "COORD", "COORDINATION", "MEP", "DRAINAGE", "POWER",
        };

        /// <summary>
        /// Routing docTypes the shipped table carries that no command asks for yet, with
        /// why each rule is kept rather than deleted: a project override or a person's
        /// own routing call may rely on it, and each names a real drawing type a future
        /// generator or producer should ask for through this file.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Unrequested =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // No generator draws these schematic types yet (production skips them — see SchematicNotProducedReason).
                { "HVAC_SCHEMATIC", "mep-hvac-schematic-A1 — no generator draws it yet." },
                { "DHW_SCHEMATIC", "plumb-dhw-schematic-A1-NTS — no generator draws it yet (Plumb_SupplySchematic draws cold water only)." },
                { "LTHW_SCHEMATIC", "plumb-lthw-schematic-A1-NTS — no generator draws it yet." },
                { "VENT_RISER", "plumb-vent-riser-A3-NTS — no generator draws it yet." },
                { "RISER_SCHEMATIC", "fire-riser-schematic-A1 — no generator draws it yet." },
                { "ESS_RISER", "health-ess-power-riser-A1 — no generator draws it yet." },
                { "EES-RISER", "health-ess-power-riser-A1 (ISO code form) — no generator draws it yet." },
                { "MGS-SCH", "health-medgas-schem-A1 (ISO code form of MG / SCHEMATIC, which MGPS_Schematic asks for)." },
                { "NUC-MED", "health-nuclear-medicine-A1 (ISO code form)." },
                { "NUCLEAR_MEDICINE", "health-nuclear-medicine-A1." },
                { "FIRE_ALARM", "elec-fire-alarm-A1-1to100 — a plan type; per-level production picks plans by purpose." },
                // Plan / layout types chosen by drawing-type id in production dialogs and presets.
                { "HVAC_PIPE", "mep-hvac-pipe-A1-1to100 — chosen by id in production." },
                { "HVAC_DUCT", "mep-hvac-duct-A1-1to100 — chosen by id in production." },
                { "WATER_SUPPLY", "plumb-water-supply-A1-1to100 — chosen by id in production." },
                { "CONTAINMENT", "elec-containment-A1-1to100 — chosen by id in production." },
                { "DATA_COMMS", "elec-data-comms-A1-1to100 — chosen by id in production." },
                { "SECURITY", "elec-security-A1-1to100 — chosen by id in production." },
                { "EMERGENCY_LIGHTING", "elec-emergency-lighting-A1-1to100 — chosen by id in production." },
                { "LIGHTING", "elec-lighting-A1-1to100 — chosen by id in production." },
                { "PLANTROOM", "mep-plantroom-A1-1to50 — chosen by id in production." },
                { "SPRINKLER", "fire-sprinkler-layout-A1-1to100 — chosen by id in production." },
                { "AG_DRAINAGE", "plumb-ag-drainage-A1-1to100 — chosen by id in production." },
                { "RWD_LAYOUT", "plumb-rwd-layout-A1-1to100 — chosen by id in production." },
                { "SUDS", "plumb-suds-A1-1to500 — chosen by id in production." },
                { "WATER_TREATMENT", "plumb-water-treatment-A1-1to50 — chosen by id in production." },
                { "DETAIL", "Discipline detail types — chosen by id in production." },
                { "LEGEND", "legend-A3 — legends are made in Revit (the API cannot create one)." },
                // Schedules no command places yet.
                { "MECH_EQUIP_SCHEDULE", "mech-equip-schedule-A3 — no command places the schedule yet." },
                { "VALVE_SCHEDULE", "valve-schedule-A3 — no command places the schedule yet." },
                { "PENETRATION_REGISTER", "penetration-register-A1 — no command places the register yet." },
                { "PRESSURE_SCHEDULE", "plumb-pressure-schedule-A3 — no command places the schedule yet." },
                { "SCHEDULE", "door-schedule-A3 — chosen by id." },
                { "WIN_SCHEDULE", "arch-window-schedule-A3 — chosen by id." },
                // Architectural / structural / general types chosen by id or by phase.
                { "SITE", "Chosen by id in production." },
                { "ROOF_PLAN", "Chosen by id in production." },
                { "FLOOR_FINISHES", "Chosen by id in production." },
                { "FIRE_STRATEGY", "Chosen by id in production." },
                { "ACCESSIBILITY", "Chosen by id in production." },
                { "INTERIOR_ELEVATION", "Chosen by id in production." },
                { "FOUNDATION", "Chosen by id in production." },
                { "REBAR_DETAIL", "Chosen by id in production." },
                { "SETTING_OUT", "Chosen by id in production." },
                { "PARTITION", "Chosen by id in production." },
                { "SANITARY", "Chosen by id in production." },
                { "RAISED_FLOOR", "Chosen by id in production." },
                { "FLOOR_DETAIL", "Chosen by id in production." },
                { "AREA_PLAN", "Chosen by id in production." },
                { "ASSET_LOCATION", "Chosen by id in production." },
                { "CLASH", "Chosen by id in production." },
                { "CLARIFICATION", "Chosen by id." },
                { "RFI", "Chosen by id." },
                // Presentation keys (phase PRESENTATION) — chosen deliberately, by id.
                { "NARRATIVE", "Presentation — chosen by id." },
                { "3D", "Presentation — chosen by id." },
                { "PERSPECTIVE", "Presentation — chosen by id." },
                { "RENDER_BOARD", "Presentation — chosen by id." },
                { "CONTEXT", "Presentation — chosen by id." },
                { "DESIGN_INTENT", "Presentation — chosen by id." },
                { "EXTERIOR_3D", "Presentation — chosen by id." },
                { "INTERIOR_CUTAWAY", "Presentation — chosen by id." },
                { "COMPOSITE", "Presentation — chosen by id." },
                // Healthcare pack (both the ISO code form and the word form) — chosen by id.
                { "RDS", "Healthcare — chosen by id." }, { "EQP", "Healthcare — chosen by id." },
                { "EQP_LAYOUT", "Healthcare — chosen by id." }, { "MEP-COORD-CLN", "Healthcare — chosen by id." },
                { "HEALTHCARE_COORD", "Healthcare — chosen by id." }, { "MGS-LAY", "Healthcare — chosen by id." },
                { "PRESS-REGIME", "Healthcare — chosen by id." }, { "PRESSURE", "Healthcare — chosen by id." },
                { "IPS", "Healthcare — chosen by id." }, { "DECON-FLOW", "Healthcare — chosen by id." },
                { "DECON_FLOW", "Healthcare — chosen by id." }, { "MORTUARY", "Healthcare — chosen by id." },
                { "FIRE-COMP-CLN", "Healthcare — chosen by id." }, { "FIRE_COMPARTMENT", "Healthcare — chosen by id." },
                { "RAD-SHIELD", "Healthcare — chosen by id." }, { "SHIELDING", "Healthcare — chosen by id." },
                { "MRI-ZONING", "Healthcare — chosen by id." }, { "MRI_ZONING", "Healthcare — chosen by id." },
                { "LIGATURE", "Healthcare — chosen by id." }, { "BEDHEAD-ELEV", "Healthcare — chosen by id." },
                { "BEDHEAD", "Healthcare — chosen by id." }, { "OR-RCP", "Healthcare — chosen by id." },
                { "OR_CEILING", "Healthcare — chosen by id." }, { "WATER-SAFETY", "Healthcare — chosen by id." },
                { "WATER_SAFETY", "Healthcare — chosen by id." }, { "ACOUSTIC-STRAT", "Healthcare — chosen by id." },
                { "ACOUSTIC", "Healthcare — chosen by id." }, { "IMAGING-LOADS", "Healthcare — chosen by id." },
                { "HEALTHCARE_LOADS", "Healthcare — chosen by id." }, { "RTLS", "Healthcare — chosen by id." },
                { "WASTE-FLOW", "Healthcare — chosen by id." }, { "WASTE_FLOW", "Healthcare — chosen by id." },
            };

        /// <summary>True when some command asks the routing table for <paramref name="docType"/>.</summary>
        public static bool IsRequested(string docType)
        {
            if (string.IsNullOrWhiteSpace(docType)) return false;
            return OtherCallerDocTypes.Contains(docType)
                || All.Any(r => string.Equals(r.DocType, docType, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The generator whose shipped type is <paramref name="drawingTypeId"/>, or null.</summary>
        public static DrawingRouteRequest GeneratorFor(string drawingTypeId)
            => DiagramGenerators.Concat(UnplacedGenerators).FirstOrDefault(r =>
                string.Equals(r.FallbackDrawingTypeId, drawingTypeId, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Why production does not make a Schematic drawing type that declares no
        /// productionRules. It used to: an empty drafting view on a numbered sheet,
        /// reported as produced. A schematic is drawn by its generator; a type with no
        /// generator is drafted by a person.
        /// </summary>
        public static string SchematicNotProducedReason(string drawingTypeId)
        {
            var gen = GeneratorFor(drawingTypeId);
            if (gen != null && UnplacedGenerators.Contains(gen))
                return $"Drawing type '{drawingTypeId}' is a diagram drawn by {gen.Caller}; production does not make an " +
                       $"empty drafting view for it. Run {gen.Caller} — it draws and stamps the view — then set its scale " +
                       "and place it on a sheet of this type.";
            return gen != null
                ? $"Drawing type '{drawingTypeId}' is a schematic drawn by {gen.Caller}; production does not make an " +
                  $"empty drafting view for it. Run {gen.Caller} — it draws the diagram and places it on this type's sheet."
                : $"Drawing type '{drawingTypeId}' is a schematic no STING generator draws; production skipped it rather " +
                  "than make an empty drafting view and sheet. Draft it in Revit, then place it on a sheet of this type.";
        }
    }
}
