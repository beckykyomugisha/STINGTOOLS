# MGS Family Library Stubs (Phase H-7)

Per the v4 MVP family-library policy, this directory ships **parameter
specs only** — the .rfa families themselves are not committed.

## Required families

| File | Role | Required parameters |
|---|---|---|
| `STING_MGS_Manifold.rfa`        | Cylinder manifold / plant feed | MGS_GAS_TYPE_TXT, MGS_NOM_PRESS_KPA_NR, MGS_DESIGN_FLOW_LPM_NR |
| `STING_MGS_VIE.rfa`             | Vacuum Insulated Evaporator (LOX) | MGS_GAS_TYPE_TXT (=O2), MGS_NOM_PRESS_KPA_NR |
| `STING_MGS_ZoneValveBox_4G.rfa` | Zone valve box (up to 4 gases) | MGS_ZVB_REF_TXT |
| `STING_MGS_AreaAlarmPanel.rfa`  | Area alarm panel | MGS_AAP_REF_TXT |
| `STING_MGS_MasterAlarmPanel.rfa`| Master alarm panel | MGS_AAP_REF_TXT (mirrors AAP) |
| `STING_MGS_TerminalUnit.rfa`    | BS 5682 terminal unit (gas-driven type catalog) | MGS_GAS_TYPE_TXT, MGS_TU_BS5682_BOOL, MGS_TU_INDEXED_BOOL |

Real `.rfa` families from manufacturers (Beaconmedaes, Pattons, GCE, Megasan,
Wandsworth, Static Systems) drop in by replacing the stub.
