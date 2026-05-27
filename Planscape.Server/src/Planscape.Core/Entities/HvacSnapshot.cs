// Phase 188 (Tier 3) — HVAC snapshot pushed from the desktop plugin.
//
// One row per push covers the four HVAC dashboards the mobile app
// surfaces (loads / balance / drift / carbon). The plugin pushes
// after each completed sizing / balancing / scan run so the mobile
// app can show "what was the model's state at HH:MM today".
//
// Body is JSON-encoded so the schema can evolve without migrations.
// Aggregate counts are extracted into columns for cheap dashboard
// queries (RAG calc, "X drifted of Y", etc).

namespace Planscape.Core.Entities;

public class HvacSnapshot : ITenantScoped
{
    public Guid   Id        { get; set; } = Guid.NewGuid();
    public Guid   TenantId  { get; set; }
    public Guid   ProjectId { get; set; }
    public string Kind      { get; set; } = ""; // "loads" | "balance" | "drift" | "carbon" | "sizing"
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    // Common KPI columns — let SQL aggregate without parsing JSON.
    public int    Inspected  { get; set; }
    public int    Pass       { get; set; }
    public int    Warn       { get; set; }
    public int    Fail       { get; set; }
    public double TotalKw    { get; set; }     // load totals, plant carbon, etc.
    public double WorstValue { get; set; }     // worst-NC / worst-ΔP / worst-aspect, etc.
    public string Rag        { get; set; } = "G"; // R / A / G derived plugin-side

    // Verbatim JSON payload — anything else the mobile app wants to render.
    public string PayloadJson { get; set; } = "{}";
}
