# LPS master — corrections only

**12 rows changed. Nothing else did.** Everything you have already entered is
still correct; only these need editing, and only the Formula field.

## Why

Revit has no number-to-string conversion in family formulas, so a Text
calculated value cannot reference a NUMBER, LENGTH or YESNO parameter -
`if(BOOL, <NUMBER>, "")` has two differently-typed branches and is rejected as
**Inconsistent Units**. Each of these now reads the parameter's TEXT twin,
which already existed.

Rows **42** and **70** are in the shared block, so they were wrong in the
universal sheet too - worth checking how they were authored in the universal
master, which is already built.

## What to change

For each row: open its Calculated Value (fx), replace the Formula, OK. The
Name, Prefix, Suffix and Break stay exactly as they are.

| # | Tier | Calc Value Name | Replace formula with | Prefix | Suffix |
|---|---|---|---|---|---|
| 11 | T2 | Show T2 - LPS Conductor Cross Sect | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_CONDUCTOR_CROSS_SECT_TXT, "")` | t: | mm |
| 13 | T2 | Show T2 - LPS Protection Angle | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_PROTECTION_ANGLE_TXT, "")` | α: | ° |
| 14 | T2 | Show T2 - LPS Air Terminal Count | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_AIR_TERMINAL_COUNT_TXT, "")` | N: |  |
| 15 | T2 | Show T2 - LPS Down Conductor Count | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_DOWN_CONDUCTOR_COUNT_TXT, "")` | N: |  |
| 16 | T2 | Show T2 - LPS Separation Distance | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_SEPARATION_DISTANCE_TXT, "")` | s: | mm |
| 18 | T2 | Show T2 - LPS Earth Resistance | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_EARTH_RESISTANCE_TXT, "")` | R: | Ω |
| 19 | T2 | Show T2 - LPS Earth Electrode Count | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_EARTH_ELECTRODE_COUNT_TXT, "")` | N: |  |
| 24 | T3 | Show T3 - LPS Rolling Sphere Radius | `if(TAG_PARA_STATE_3_BOOL, ELC_LPS_ROLLING_SPHERE_RADIUS_TXT, "")` | r: | m |
| 25 | T3 | Show T3 - LPS Mesh Size | `if(TAG_PARA_STATE_3_BOOL, ELC_LPS_MESH_SIZE_TXT, "")` | Mesh: | m |
| 26 | T3 | Show T3 - LPS Inspection Interval | `if(TAG_PARA_STATE_3_BOOL, ELC_LPS_INSPECTION_INTERVAL_MONTHS_TXT, "")` | Inspect: | mo |
| 42 | T5 | Show T5 - Cost - Stale Flag | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_STALE_TXT, "")` | Stale: |  |
| 70 | T8 | Show T8 - Coordination - ASS_CRITICALITY_RATING_NR | `if(TAG_PARA_STATE_8_BOOL, ASS_CRITICALITY_RATING_TXT, "")` | Crit: | /5 |

## Just the swaps, if you prefer to read it that way

```
#11  ELC_LPS_CONDUCTOR_CROSS_SECT_MM2     -> ELC_LPS_CONDUCTOR_CROSS_SECT_TXT
#13  ELC_LPS_PROTECTION_ANGLE_DEG         -> ELC_LPS_PROTECTION_ANGLE_TXT
#14  ELC_LPS_AIR_TERMINAL_COUNT_NR        -> ELC_LPS_AIR_TERMINAL_COUNT_TXT
#15  ELC_LPS_DOWN_CONDUCTOR_COUNT_NR      -> ELC_LPS_DOWN_CONDUCTOR_COUNT_TXT
#16  ELC_LPS_SEPARATION_DISTANCE_MM       -> ELC_LPS_SEPARATION_DISTANCE_TXT
#18  ELC_LPS_EARTH_RESISTANCE_OHM         -> ELC_LPS_EARTH_RESISTANCE_TXT
#19  ELC_LPS_EARTH_ELECTRODE_COUNT_NR     -> ELC_LPS_EARTH_ELECTRODE_COUNT_TXT
#24  ELC_LPS_ROLLING_SPHERE_RADIUS_M      -> ELC_LPS_ROLLING_SPHERE_RADIUS_TXT
#25  ELC_LPS_MESH_SIZE_M                  -> ELC_LPS_MESH_SIZE_TXT
#26  ELC_LPS_INSPECTION_INTERVAL_MONTHS   -> ELC_LPS_INSPECTION_INTERVAL_MONTHS_TXT
#42  ASS_CST_STALE_BOOL                   -> ASS_CST_STALE_TXT
#70  ASS_CRITICALITY_RATING_NR            -> ASS_CRITICALITY_RATING_TXT
```

## One thing to know

The `_TXT` twins are not written by anything yet. These rows will render BLANK
until each twin holds a value. That is expected, not a mistake in the rows -
the mirror that fills them from the numerics is a separate piece of work.
