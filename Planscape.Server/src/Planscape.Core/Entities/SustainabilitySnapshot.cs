// Phase 195 (WS A6) — EDGE/LEED sustainability snapshot pushed from the desktop
// plugin, mirroring HvacSnapshot. One row per Sustain_PublishToServer push so the
// mobile app / web dashboard can show "what was the model's sustainability state
// at HH:MM today" and trend it over time.
//
// The headline savings %, EDGE level and operational carbon ride in columns for
// cheap dashboard queries; the verbatim EdgeKpiSnapshot rides in PayloadJson so
// the schema can evolve without a migration.

namespace Planscape.Core.Entities;

public class SustainabilitySnapshot : ITenantScoped
{
    public Guid     Id         { get; set; } = Guid.NewGuid();
    public Guid     TenantId   { get; set; }
    public Guid     ProjectId  { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public string   CapturedBy { get; set; } = "";

    // ── Headline KPI columns (let SQL aggregate without parsing JSON) ──
    public double EnergyEuiKwhM2Yr        { get; set; }
    public double EnergySavingsPct         { get; set; }
    public double WaterLPersonDay          { get; set; }
    public double WaterSavingsPct          { get; set; }
    public double MaterialCarbonKgM2       { get; set; }
    public double MaterialEnergyMjM2       { get; set; }
    public double MaterialEnergySavingsPct { get; set; }
    public double GwpReductionPct          { get; set; }

    public string EdgeLevel  { get; set; } = "None";
    public bool   EdgePassed { get; set; }

    public double OperationalCarbonKgYr { get; set; }
    public int    Occupancy   { get; set; }
    public double FloorAreaM2 { get; set; }
    public string SupplyMode  { get; set; } = "";
    public string Country     { get; set; } = "";
    public string ClimateZone { get; set; } = "";
    public string Rag         { get; set; } = "G"; // R / A / G derived plugin-side

    // Verbatim EdgeKpiSnapshot JSON — anything else the dashboard wants to render.
    public string PayloadJson { get; set; } = "{}";
}
