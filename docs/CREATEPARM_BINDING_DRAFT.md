# CREATEPARM-1 — implemented (this was a draft)

Superseded by implementation on 2026-09-17. Kept because the corrections matter more than
the result.

## What the first draft got wrong

The draft derived each category from the `BuiltInCategory` references in the file that writes
the parameter. That is better than guessing from the name, and it was still wrong twice:

| Draft said | Actually | Evidence |
|---|---|---|
| `ELC_WIRE_*` → **Wires** | **Conduits** | The write receiver is a variable literally named `conduit`; the collectors are `OfCategory(OST_Conduit)`. `WireConfigurationDialog` mentions `Wire` 23 times because it edits wire *data*, not because it writes to wires. |
| `ELC_LIGHTING_*` → **Rooms** | **Lighting Fixtures** | `LightingGridCommand:214` writes to `fi`, the fixture. The `OfCategory(OST_Rooms)` at line 250 is the *iteration source*, not the target. |

`WireElementAnnotationCommands.cs:16-19` settles the wire question outright:

> "…**no ELC_WIRE_\* parameters are written to wires**. Persistence / refresh (Phase 2) is
> deferred pending the OST_Wire binding question."

Binding `OST_Wire` would have pre-empted a deferred design decision — and it was the draft's
single largest prerequisite. It turns out not to be needed at all.

**A file-level category census is evidence. The write receiver is proof.**

## The bigger thing the draft missed

`CATEGORY_BINDINGS.csv` is not the last word. `tools/param_binding_resolver.py` classifies by
prefix rule and only falls back to that CSV for parameters its rules do not claim, and
`LoadSharedParamsCommand` reads the RESOLVED spec when one exists. So adding rows was
**necessary but not sufficient** — the resolver overrode them for two groups:

- `HVC_PEAK_*` / `HVC_OA_LS` / `HVC_LOAD_*` / `HVC_SELECTED_*` fell into `MEP_ALL`
  (equipment and ductwork). They are stamped **per space** by `BlockLoadEngine`, so they
  landed nowhere. Fixed with an `HVAC_SPACE` group → `MEP Spaces|Rooms`.
- `PEN_*` resolved to the **host** elements a penetration passes through, but
  `FrpPenetrationPlacer` writes them onto the **placed seal**. `Specialty Equipment` added to
  the `PEN` group, keeping the hosts.

Regenerating changed **64 lines — all of them the two intended groups plus their `_TXT`
mirrors.** The blast radius was diffed, not assumed.

## The three decisions

**1. `BLE_ROOM_NUM_TXT` — not universal.** It is written beside `BLE_ROOM_NAME_TXT` by the
same helper, so it is bound to the **same ten categories as its sibling**. Universal would
have put a parameter on every category for a consumer that does not exist (nothing reads it);
deleting the write would have split a pair that is written together. Mirroring the sibling is
the only option that leaves the two consistent.

**2. Lighting on Rooms or Fixtures? — Fixtures.** The receiver decides.

Your question about Spaces exposed something else: `LightingGridCommand` collects
`OfCategory(OST_Rooms)` **only**, so on an MEP model that uses Spaces it finds nothing to
light and silently does nothing. That is a command limitation, and **no binding can paper
over it** — the command never reaches a Space to write to. Logged as **LIGHTGRID-1**.

**3. `ELC_PNL_NAME_TXT` on three categories? — No, Conduits.** Both write receivers are
conduits (`conduit`, and `seg` from `route.AllSegments`).

## Your instinct about conduits was right, and then some

Not only do *some* wire parameters belong on conduits — **all of them do, and none belongs on
wires.** The resolver widens them further to `Cable Trays|Conduits|Electrical Circuits`, which
is a superset of the evidence and defensible: a cable run is annotated on trays as well.

## Result

```
write targets bound to NOTHING:   57  →  0
```

Seven gates green · build 0/0 · 1,542 tests.

⚠️ **Re-run Create Parameters in Revit, then re-tag.** The binding makes the write possible;
it does not perform it. `HVC_PEAK_SENS_W` should hold a number for the first time.
