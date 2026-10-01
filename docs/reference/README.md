# Reference data — not shipped with the plugin

Files that used to sit in `StingTools/Data/` but that **no plugin code reads**
(found by the data-schema review, ROADMAP DSCH-15). They are kept here because
their content documents a design, not because anything loads them. Moving them
out of `Data/` stops them shipping as if they were live configuration.

| File | Why it is here |
|---|---|
| `HEALTHCARE_ALERT_ROUTING.json` | Alert-routing table designed for H-30 (`docs/HEALTHCARE_PACK_DESIGN.md` §19.3). No server or plugin code raises these alerts yet; move it back to wherever the H-30 service loads it when that is built. |
| `STING_US_PRESET_OVERLAY.json` | Documented example overlay for `docs/US_STANDARDS_PRESET.md`; its own comment says it is not auto-loaded. |
| `LEGIONELLA_REPORT_TEMPLATE.json` | Token/section outline for the legionella risk report. The command looks for `legionella_risk_assessment.docx` (which does not exist) and falls back to built-in text; this outline is the starting point for authoring that template. Its token names must be reconciled with `PlumbingWaterSafetyCommands.BuildTokenDictionary` first. |
