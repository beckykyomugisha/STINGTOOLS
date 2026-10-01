# Kibale NP — project data files

Authored against the **real** schemas read out of the code, not from documentation. Every file below binds to a verified loader; the citation is given so you can check it yourself.

You author the `.rvt`. These are everything else.

## Where each file goes

| File here | Copy to | Loader (verified) |
|---|---|---|
| `project_config.json` | **beside the `.rvt`** — `<rvtDir>/project_config.json` | `Temp/ProjectSetupCommand.cs:642-647` writes it here; `Core/TagConfig.cs:733` reads it |
| `rate_card.json` | `<project>/<CODE>/_data/coord/rate_card.json` | `BOQ/Rates/Providers/ProjectRateCardProvider.cs:50` — `StingPaths.MetaFile(doc,"_BIM_COORD","rate_card.json")` |
| `takeoff_rules.json` | same folder | `BOQ/Takeoff/TakeoffRule.cs:321` |
| `carbon_factors_ug.json` | same folder | `BOQ/UgCarbonFactors.cs:54` |
| `boq_links.json` | same folder | `BOQ/BOQCostManager.cs:336` |
| `boq_custom_templates.json` | `<project>/<CODE>/_data/coord/` — the `_bim_manager` bucket resolves to the same consolidated folder | `Temp/BOQTemplateLibrary.cs` project layer |
| `spatial_codes.json` | **nothing reads this yet** — it is the proposed corporate baseline for gap F-9 | — |

> **Do not hand-build the path.** `_BIM_COORD`, `STING_BIM_MANAGER`, `_bim_manager` and `.bimmanager` are all aliases that resolve to the single `_data/coord/` bucket. Use **BIM tab → Open Project Folder** to find the real location, then drop the files in the `coord` folder.

## Order to apply them

1. `project_config.json` — **before you tag anything.** The LOC vocabulary must exist before the first tag write, or you re-tag the project.
2. `carbon_factors_ug.json` and `takeoff_rules.json` — before the first BOQ run. Then **BIM tab → `Cost_ReloadRules`**.
3. `rate_card.json` — before the first BOQ run.
4. `boq_links.json` — after the cottage links are placed. Or set it through the UI: **BOQ Cost Manager → Linked models in takeoff**, which writes this file for you and is less error-prone.
5. `boq_custom_templates.json` — before the first bill you show anyone.

## What each one fixes

- **`project_config.json`** — extends the LOC vocabulary from `BLD1/BLD2/BLD3/EXT` to the eleven codes this site needs, through **all three** keys, because they are read by different consumers and do not talk to each other (gap F-1).
- **`takeoff_rules.json`** — gives the pool, the toposolid, the screed and the finish floors a rule, so they do not fall through to NRM2 §23 "building fabric sundries" with a fallback quantity of zero (gaps A-1, B-2).
- **`carbon_factors_ug.json`** — adds the East African materials that have no keyword at all, and uses the `byMaterial` exact-match tier, which the shipped file leaves completely empty even though the loader supports it (gap E-2/E-3).
- **`rate_card.json`** — bypasses the material library entirely, because its rates are ~3,700× low and win on priority (gap E-1).
- **`boq_links.json`** — turns on the ×7 multiplier for the cottage, without which your bill contains one cottage (gap A-3).
- **`boq_custom_templates.json`** — replaces the generic *"Supply, deliver and install…"* fallback with real lodge descriptions.

## A warning about `project_config.json`

Several keys this project needs are **not in `TagConfig`'s known-keys whitelist** (`Core/TagConfig.cs:694-719`): `LOC_CODES_EXTRA`, `UGX_PER_USD`, `FOLDER_CODE_SUFFIX`, `WRITE_COST_ON_TAG`, `COST_DEFAULT_WASTE_PCT`.

They **work** — `GetConfigValue` / `GetConfigDouble` read straight from the parsed dictionary — but every load logs:

```
TagConfig: unknown config key(s) in project_config.json: LOC_CODES_EXTRA, UGX_PER_USD … — check for typos
```

Expect that warning. It is the whitelist that is out of date, not your file.
