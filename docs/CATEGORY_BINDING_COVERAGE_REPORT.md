# Category-binding coverage report (P3)

Generated 2026-07-06 for branch `claude/tb-rest-autonomous`.

Audits `CATEGORY_BINDINGS.csv` + `BINDING_COVERAGE_MATRIX.csv` against the
shared parameters the placement / routing / sizing / BOQ / tag engines actually
**write** (via `ParameterHelpers.Set*`) with the HVC_/ELC_/PLM_/PEN_/ASS_
prefixes. A param an engine writes but that is unbound to the element category
silently drops its value at runtime, so these are real gaps.

Method: enumerate `Set*(el,"PREFIX_...")` literals in `StingTools/**/*.cs`,
intersect with real shared params in `MR_PARAMETERS.txt`, subtract params that
already carry >=1 binding. 81 gaps found.

- **28 auto-bound this pass** — a bound sibling in the same param
  family (e.g. `ELC_WIRE_*`, `HVC_DCT_*`, `ASS_TAG_*`) already defines the
  category set, so the new param is bound to the identical categories /
  binding-type. Low-risk: replicates the maintained schema pattern.
- **53 left for manual mapping** — no bound sibling exists, so the
  target category needs a per-engine decision (guessing risks wrong-category
  bindings). Suggested targets below; NOT written to the CSVs.

## Auto-bound this pass (sibling-derived)

| Param | Datatype | Categories (count) | Sibling stem |
|---|---|---|---|
| `ASS_LAST_TOKEN_HASH_TXT` | TEXT | 1 | `ASS_LAST_` |
| `ASS_REV_TXT` | TEXT | 1 | `ASS_REV_` |
| `ASS_TAG_MODIFIED_DT` | TEXT | 46 | `ASS_TAG_` |
| `ASS_TAG_PREV_TXT` | TEXT | 46 | `ASS_TAG_` |
| `ASS_WARRANTY_TXT` | TEXT | 19 | `ASS_WARRANTY_` |
| `ELC_CDT_FAB_METHOD_TXT` | TEXT | 4 | `ELC_CDT_` |
| `ELC_CDT_SOFFIT_OFFSET_MM` | TEXT | 4 | `ELC_CDT_` |
| `ELC_JB_UPSTREAM_REF_TXT` | TEXT | 1 | `ELC_JB_` |
| `ELC_PNL_IP_RATING_TXT` | TEXT | 22 | `ELC_PNL_` |
| `ELC_PNL_NAME_TXT` | TEXT | 22 | `ELC_PNL_` |
| `ELC_WIRE_AMPACITY_A` | NUMBER | 1 | `ELC_WIRE_` |
| `ELC_WIRE_CIRCUIT_BREAKER_A` | NUMBER | 1 | `ELC_WIRE_` |
| `ELC_WIRE_CIRCUIT_TYPE_TXT` | TEXT | 1 | `ELC_WIRE_` |
| `ELC_WIRE_COND_MAT_TXT` | TEXT | 1 | `ELC_WIRE_` |
| `ELC_WIRE_CORE_COUNT_INT` | INTEGER | 1 | `ELC_WIRE_` |
| `ELC_WIRE_CSA_MM2_NUM` | NUMBER | 1 | `ELC_WIRE_` |
| `ELC_WIRE_INSTALL_METHOD_TXT` | TEXT | 1 | `ELC_WIRE_` |
| `ELC_WIRE_MAX_DEMAND_A` | NUMBER | 1 | `ELC_WIRE_` |
| `ELC_WIRE_PHASE_TXT` | TEXT | 1 | `ELC_WIRE_` |
| `ELC_WIRE_PROFILE_ID_TXT` | TEXT | 1 | `ELC_WIRE_` |
| `ELC_WIRE_VD_PCT_NUM` | NUMBER | 1 | `ELC_WIRE_` |
| `HVC_DCT_FAB_METHOD_TXT` | TEXT | 8 | `HVC_DCT_` |
| `HVC_DCT_SEAM_TYPE_TXT` | TEXT | 8 | `HVC_DCT_` |
| `HVC_PRESSURE_DROP_PA` | TEXT | 2 | `HVC_PRESSURE_` |
| `PLM_PIPE_INS_THK_MM` | TEXT | 1 | `PLM_PIPE_` |
| `PLM_PPE_FAB_METHOD_TXT` | TEXT | 7 | `PLM_PPE_` |
| `PLM_PPE_HANGER_TYPE_TXT` | TEXT | 7 | `PLM_PPE_` |
| `PLM_VLV_BACKFLOW_TYPE_TXT` | TEXT | 7 | `PLM_VLV_` |

## Left for manual category mapping

| Param | Datatype | Writing engine | Suggested target category |
|---|---|---|---|
| `ASS_BARCODE_TXT` | TEXT | `BIMManager/GapFixCommands.cs` | all modelled categories (asset tag) — bind like ASS_ASSET_ID_TXT |
| `ASS_BMS_ADDRESS_TXT` | TEXT | `Temp/IoTMaintenanceCommands.cs` | Mechanical/Electrical Equipment (BMS-addressable plant) |
| `ASS_BOQ_LINE_REF` | TEXT | `BOQ/BOQCostManager.cs` | measured element categories (BOQ line ref) |
| `ASS_CDE_STATUS_TXT` | TEXT | `BIMManager/BIMManagerCommands.cs` | all modelled categories (CDE status is universal) — bind like ASS_STATUS_TXT |
| `ASS_CDE_SUITABILITY_TXT` | TEXT | `BIMManager/BIMManagerCommands.cs` | all modelled categories (CDE status is universal) — bind like ASS_STATUS_TXT |
| `ASS_CLASS_COD_TXT` | TEXT | `Temp/StandardsEngine.cs` | all modelled categories (classification) — bind like ASS_CAT_TXT |
| `ASS_CLASS_DESC_TXT` | TEXT | `Temp/StandardsEngine.cs` | all modelled categories (classification) — bind like ASS_CAT_TXT |
| `ASS_CONDITION_DATE_TXT` | TEXT | `Temp/IoTMaintenanceCommands.cs` | FM asset categories (Mechanical/Electrical/Plumbing Equipment) |
| `ASS_CONDITION_TXT` | TEXT | `Temp/IoTMaintenanceCommands.cs` | FM asset categories (Mechanical/Electrical/Plumbing Equipment) |
| `ASS_INSTALLATION_DATE_TXT` | TEXT | `Core/ParameterHelpers.cs` | FM asset categories |
| `ASS_MAINT_INTERVAL_TXT` | TEXT | `Temp/IoTMaintenanceCommands.cs` | FM asset categories (Mechanical/Electrical/Plumbing Equipment) |
| `ASS_MAINT_NEXT_TXT` | TEXT | `Temp/IoTMaintenanceCommands.cs` | FM asset categories (Mechanical/Electrical/Plumbing Equipment) |
| `ASS_NOTES_TXT` | TEXT | `Temp/DataPipelineCommands.cs` | all modelled categories |
| `ASS_NRM2_PARA_AUTHOR_TXT` | TEXT | `BOQ/BOQCostManager.cs` | Generic Models + measured element categories (BOQ paragraph carrier) |
| `ASS_NRM2_PARA_DATE_TXT` | TEXT | `BOQ/BOQCostManager.cs` | Generic Models + measured element categories (BOQ paragraph carrier) |
| `ASS_NRM2_PARA_PREV_TXT` | TEXT | `BOQ/BOQCostManager.cs` | Generic Models + measured element categories (BOQ paragraph carrier) |
| `ASS_NRM2_PARA_TXT` | TEXT | `BOQ/BOQCostManager.cs` | Generic Models + measured element categories (BOQ paragraph carrier) |
| `ASS_TIEIN_CONNECTED_BOOL` | TEXT | `UI/StingCommandHandler.cs` | Pipes, Ducts, Pipe Accessories (fabrication tie-in on MEP curves) |
| `ASS_TIEIN_REF_TXT` | TEXT | `UI/StingCommandHandler.cs` | Pipes, Ducts, Pipe Accessories (fabrication tie-in on MEP curves) |
| `ASS_TIEIN_SIZE_TXT` | TEXT | `UI/StingCommandHandler.cs` | Pipes, Ducts, Pipe Accessories (fabrication tie-in on MEP curves) |
| `ASS_TIEIN_STATUS_TXT` | TEXT | `UI/StingCommandHandler.cs` | Pipes, Ducts, Pipe Accessories (fabrication tie-in on MEP curves) |
| `ASS_TIEIN_TAG_1_TXT` | TEXT | `UI/StingCommandHandler.cs` | Pipes, Ducts, Pipe Accessories (fabrication tie-in on MEP curves) |
| `ELC_LIGHTING_GRID_SPC_MM` | TEXT | `Commands/Placement/LightingGridCommand.cs` | Lighting Fixtures + Spaces (grid design inputs) |
| `ELC_LIGHTING_ROOM_TYPE` | TEXT | `Commands/Placement/LightingGridCommand.cs` | Lighting Fixtures + Spaces (grid design inputs) |
| `ELC_LIGHTING_TARGET_LUX_TXT` | TEXT | `Commands/Placement/LightingGridCommand.cs` | Lighting Fixtures + Spaces (grid design inputs) |
| `ELC_PANEL_REF_TXT` | TEXT | `Commands/Electrical/BatchAssignCircuitsCommand.cs` | Electrical Equipment (panels) |
| `ELC_PANEL_SCHEDULE_REF_TXT` | TEXT | `Core/Panels/PanelScheduleApplyEngine.cs` | Electrical Equipment (panels) |
| `ELC_TAG_7_PARA_LPS_TXT` | TEXT | `Commands/Lightning/LpsWave4Commands.cs` | all modelled categories (TAG7 narrative) — bind like ELC_TAG_ siblings |
| `ELC_TRANSFER_LOAD_OK` | TEXT | `Commands/Electrical/Validation/DualSourceValidationCommand.cs` | Electrical Equipment |
| `HVC_FLOW_LS` | TEXT | `Commands/Hvac/HvacPropagateLoadsCommand.cs` | Ducts, Flex Ducts, Air Terminals, Mechanical Equipment |
| `HVC_LOAD_SOURCE_TXT` | TEXT | `Commands/Hvac/HvacImportGbxmlLoadsCommand.cs` | Spaces |
| `HVC_LOAD_STALE_BOOL` | YESNO | `Commands/Hvac/HvacBlockLoadCommand.cs` | Spaces |
| `HVC_LOAD_STALE_REASON_TXT` | TEXT | `Commands/Hvac/HvacBlockLoadCommand.cs` | Spaces |
| `HVC_OA_LS` | TEXT | `Commands/Hvac/HvacBlockLoadCommand.cs` | Spaces |
| `HVC_PEAK_HOUR` | TEXT | `Commands/Hvac/HvacBlockLoadCommand.cs` | Spaces (block-load results per space) |
| `HVC_PEAK_LAT_W` | TEXT | `Commands/Hvac/HvacBlockLoadCommand.cs` | Spaces (block-load results per space) |
| `HVC_PEAK_SENS_W` | TEXT | `Commands/Hvac/HvacBlockLoadCommand.cs` | Spaces (block-load results per space) |
| `HVC_SELECTED_IDU_ID_TXT` | TEXT | `Commands/Hvac/HvacSelectIdusCommand.cs` | Mechanical Equipment (VRF indoor units) |
| `HVC_SELECTED_IDU_LABEL_TXT` | TEXT | `Commands/Hvac/HvacSelectIdusCommand.cs` | Mechanical Equipment (VRF indoor units) |
| `PEN_BEAM_DEPTH_RATIO` | NUMBER | `Core/Routing/FrpPenetrationPlacer.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PEN_BEAM_OFFSET_PCT` | NUMBER | `Core/Routing/FrpPenetrationPlacer.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PEN_CERTIFICATION_TXT` | TEXT | `Commands/Symbols/SwapToManufacturerCommand.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PEN_CONTROL_NUMBER_TXT` | TEXT | `Core/Routing/FrpPenetrationPlacer.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PEN_FIRE_RATING_TXT` | TEXT | `Core/Routing/FrpPenetrationPlacer.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PEN_HOST_REF_TXT` | TEXT | `Core/Routing/FrpPenetrationPlacer.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PEN_HOST_TYPE_TXT` | TEXT | `Core/Routing/FrpPenetrationPlacer.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PEN_INSTALL_STATUS_TXT` | TEXT | `Core/Routing/FrpPenetrationPlacer.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PEN_MEMBER_ID_TXT` | TEXT | `Core/Routing/FrpPenetrationPlacer.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PEN_OD_MM` | NUMBER | `Core/Routing/FrpPenetrationPlacer.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PEN_PFV_UUID_TXT` | TEXT | `Core/Routing/FrpPenetrationPlacer.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PEN_STRUCTURAL_FLAG_TXT` | TEXT | `Core/Routing/FrpPenetrationPlacer.cs` | Generic Models (penetration seals) + host: Structural Framing/Walls/Floors |
| `PLM_FLOW_LS` | TEXT | `Core/Mep/MepCrossStampOrchestrator.cs` | Pipes, Pipe Fittings, Plumbing Fixtures |
| `PLM_FLUID_CATEGORY_TXT` | TEXT | `Core/Plumbing/BackflowClassifier.cs` | Pipes, Pipe Accessories, Plumbing Fixtures (backflow classification) |

