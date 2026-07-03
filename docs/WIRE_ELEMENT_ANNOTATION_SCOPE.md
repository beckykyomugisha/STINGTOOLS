# Scope — Annotating Revit *Wire* Elements

**Status**: Proposal / not implemented. This document scopes a feature extension; it commits no code.
**Related**: [WIRE_ANNOTATION_GUIDE.md](WIRE_ANNOTATION_GUIDE.md) (the current conduit-based system).

---

## 1. Problem statement

STING's wire annotation labels **modelled conduit** — it reads a conduit's connected circuit and draws
the cable-spec ticks and label on the conduit run. Many electrical drawings, however, are produced
with Revit's **Wire** tool (the 2-D lines drawn between devices to show a circuit on a plan) and never
model conduit. On those drawings there is nothing for the current tool to attach to.

This scope covers extending STING to place the same cable-spec annotation directly on Revit
**Wire** elements (`Autodesk.Revit.DB.Electrical.Wire`, category `OST_Wire`).

## 2. Current state

- All wire-annotation code targets conduit (`StingTools/Commands/Electrical/WireAnnotationCommands.cs`);
  the header states it *"places BS 7671-style wire-spec annotations on conduit runs."*
- Circuit data is read by traversing the conduit's connectors to the panel; the ticks and label are
  drawn along the conduit's location curve.
- The `ELC_WIRE_*` shared parameters are bound to the **Conduits** category only (per `MR_PARAMETERS`).
- There is **no** handling of `OST_Wire` elements anywhere in the codebase.

## 3. Goal

Allow a user to select (or batch) Revit **Wire** elements and produce the same annotation — tick marks
for conductor count plus the cable-spec label — sourced from the wire's circuit, in the same style and
with the same standards basis as the conduit path.

## 4. Conduit vs Wire — what data is available

| Input the annotation needs | Conduit (today) | Wire element |
|---|---|---|
| Circuit (phase, cores, panel, demand) | Via connector traversal to the panel | **Directly** — a wire belongs to one circuit; read it from the wire's circuit association |
| Geometry to place ticks on | Conduit location curve | Wire vertices / segments (a polyline in the view) |
| Real run length (for voltage drop) | Conduit length | **Not the drawn length** — a wire's drawn length is schematic; must use the circuit's length parameter or a user-entered value |
| Conduit diameter (Ø) | Yes | **N/A** — omit from label |
| Fill % | Yes | **N/A** — omit from label |
| View scope | Model element, shown in many views | **View-specific** — a wire lives in one view (fits annotation naturally) |

**Net:** circuit data is *easier* to obtain from a wire (no traversal). The trade-offs are (a) no
containment data (diameter/fill drop out of the label), and (b) length for voltage drop must come from
the circuit, not the drawn geometry.

## 5. Design approach (phased)

### Phase 1 — Annotate on wire, compute-on-place (no persistence)
A parallel command (e.g. `Electrical_WireElementAnnotate`) that, per selected wire:
1. Resolves the wire's circuit and reads phase / cores / panel / demand.
2. Sizes the cable and computes voltage drop **using the circuit length** (not the drawn wire length).
3. Places the ticks across the mid-point of the wire's longest segment and the label at the standard
   perpendicular offset, reusing the existing style, colour logic, marker registry, and label builder.
4. Suppresses the conduit-only label fields (Ø, fill %) via a label-mode flag.

Compute-on-place sidesteps the parameter-binding question (Section 6) for a first, useful version:
nothing is stored on the wire; the label reflects the values at the moment it is placed.

### Phase 2 — Persistence, stamping, batch, refresh
Add the equivalent of `W-Stamp` / `Csz-Sync` / `VD-Sync` / batch / `W-Rfsh` for wires, **conditional on
resolving where per-wire values are stored** (Section 6). If storage is available, wires gain the same
drift-detection and refresh behaviour conduits have; if not, the compute-on-place model from Phase 1
remains the ceiling.

### Reuse
The label builder, colour resolution, slash geometry, `AnnotationMarkerRegistry`, cable sizer, and
voltage-drop engine are all reusable unchanged. New work is confined to: wire selection, circuit-direct
data read, wire geometry handling, the label-mode flag, and the length source.

## 6. Open questions to verify against the Revit API

These determine how far Phase 2 can go and must be confirmed before committing:

1. **Circuit access from a wire.** Confirm the API path from a `Wire` to its `ElectricalSystem`
   (circuit), and that phase / poles / base-equipment / apparent-current are readable from it.
2. **Wire geometry.** Confirm vertex/segment access for placing ticks and the label on a multi-segment
   wire, and behaviour for arc vs chamfer wire types.
3. **Parameter binding to `OST_Wire`.** Confirm whether instance shared parameters can be bound to the
   Wires category at all. Wires are annotation-like and historically restrict user parameters — if
   binding is not possible, Phase 2 persistence must use ExtensibleStorage on the wire (or store on the
   circuit / base equipment) rather than `ELC_WIRE_*` parameters.
4. **Circuit length for voltage drop.** Confirm the circuit length parameter is populated and reliable
   for wire-only circuits, and define the fallback when it is not (user prompt vs skip VD).
5. **Home-run wires.** Revit distinguishes home-run wire types; decide whether to detect and annotate
   these differently (the conduit path already has a home-run arrow feature).

## 7. Effort and recommendation

- **Phase 1** is moderate and low-risk: it reuses the existing engines and only adds a wire-facing
  command plus geometry/label-mode handling. It delivers the core capability (annotate wire-drawn
  circuits) without touching parameter bindings.
- **Phase 2** depends entirely on Question 3. If Wires cannot carry the parameters, persistence should
  go through ExtensibleStorage; otherwise the compute-on-place model is the practical limit and should
  be documented as such.

**Recommendation:** build Phase 1 behind the API verification in Section 6 (particularly Questions 1–4),
ship it as an additive command that leaves the conduit path untouched, and defer Phase 2 until the
storage question is resolved. Update [WIRE_ANNOTATION_GUIDE.md](WIRE_ANNOTATION_GUIDE.md) to document
the wire path once Phase 1 lands.
