# Design — STING-Managed View Templates

## The proposal in one line

Stop authoring Revit view templates by hand. Author **packs** in STING; let the
runtime *generate* and *maintain* the matching Revit view templates in the
background, and bind every produced view to one. The user never opens Revit's
VG / templates dialog.

---

## Why the current state has friction

The Phase 136 work made `pack.ViewTemplate` a *fallback* for
`DrawingType.ViewTemplateName`, but both still **point at a Revit-authored
template**. Three forms of drift result:

1. **Pack VG vs template VG.** When a template is locked, Revit honours the
   template; pack VG overrides land on it indirectly or are silently lost. The
   applier already warns about this.
2. **Editor expectation vs runtime reality.** The editor lets you author VG +
   filters in the pack, but if the template locks the same categories, you've
   double-edited and the pack's contribution is invisible.
3. **Template lifecycle.** Templates are project-scoped. Packs are corporate.
   So the same `corp-standard-plan` pack ends up bound to different templates
   in every project — and when one project edits the template, the others
   don't follow.

The user's instinct is right: if STING owns the visual style, *Revit's
template UI shouldn't be the source of truth*.

---

## What a view template can express (and where the pack stands today)

Template setting | Pack today | Comment
---|---|---
V/G category overrides (line wt, colour, pattern, fg/bg fill, halftone, transparency, detail level) | ✅ | Phase 136 expanded to full Revit fidelity
Filter overrides | ✅ | Per-filter VG (now mirrored against StyleVgOverride shape)
Filter visibility | ✅ |
Detail level | ✅ | `pack.DetailLevel`
Scale | ⚠️ | `pack.ScaleHint` is informational; `DrawingType.Scale` is authoritative
Discipline | ⚠️ | Derived from `DrawingType.Discipline`
**View range** (top, cut plane, bottom, view depth) | ❌ | *Plan-only*
**Far clip offset** | ❌ | Section / elevation
**Phase + Phase filter** | ❌ |
**Visual style** (wireframe / hidden / shaded / consistent / realistic / raytrace) | ❌ |
**Sun setting** | ❌ | Mostly elevations / 3D
**Annotation crop** | ❌ |
**Display options** (shadows, edges, ambient, sketchy lines) | ❌ |
**Background** (image / gradient / sky) | ❌ |
**Photographic exposure** | ❌ | 3D
**Worksets visibility** | ❌ |
**Underlay** (level + range + halftone) | ❌ | *Plan-only*

So **~10 fields** are needed before a pack can be a complete template
substitute. Most are simple scalars or enums; none are Revit API-hard.

---

## What Revit lets us do programmatically

Confirmed Revit 2025+ APIs:

```csharp
// Create a view of the right type
View v = ViewPlan.Create(doc, viewFamilyTypeId, levelId);
// Apply VG / filters / scale / detail level / phase / etc.
v.Scale = 50;
v.DetailLevel = ViewDetailLevel.Fine;
v.ViewTemplateId = ElementId.InvalidElementId;  // detach
v.SetCategoryOverrides(catId, ogs);
v.SetFilterOverrides(filterId, ogs);
v.SetFilterVisibility(filterId, true);
v.AddFilter(filterId);

// Convert it into a template
v.ViewTemplateId = ElementId.InvalidElementId;     // not bound
// (a "view template" is just a View with IsTemplate=true; setting it via
//  the v.IsTemplate setter is read-only — but you can flag it on creation
//  via the View Templates dialog or via Element.CopyAsTemplate workflows)
```

The cleanest pattern: **maintain one Revit template per (pack-id, view-type)
pair**, named `STING:<pack-id>:<view-type>`, stamped with
`STING_PACK_ID_TXT` + `STING_PACK_CHECKSUM_TXT` so the runtime knows it owns
the template and can detect drift.

---

## Three viable architectures (compared)

### A. Pure managed — kill `ViewTemplate` fields entirely

- Remove `pack.ViewTemplate` and `DrawingType.ViewTemplateName`.
- Pack carries every visual + view setting.
- Runtime generates `STING:<pack>:<view-type>` templates and binds views to them.

| Pro | Con |
|---|---|
| Single source of truth, no drift possible | No escape hatch — if Revit grows a template feature STING doesn't model, you're stuck |
| One-stop editing | Disruptive migration; all existing JSON breaks |
| Cleanest mental model | Every team that has invested in custom templates loses them |

### B. Pure external — the old way

Status-quo before Phase 136. Pack is just decoration on top of a Revit
template the user authored. Rejected — that's the current pain point.

### C. **Hybrid (recommended)** — `templateMode` flag per pack

Each pack declares one of:

- **`managed`** *(new default)*: STING auto-generates and maintains
  `STING:<pack>:<view-type>` templates. Pack carries all settings;
  `pack.ViewTemplate` field is ignored.
- **`external`**: Existing behaviour. Pack names a Revit template by name;
  STING applies VG/filters on top *only if the template doesn't lock them*.

| Pro | Con |
|---|---|
| Single source of truth in the common case | Two code paths to maintain |
| Escape hatch for legacy projects + Revit-only features | Slightly more configuration surface |
| Phased migration — flip mode per-pack | Need clear UX for which mode is active |

**Recommendation: C.** It costs one boolean flag per pack and gives every
project the choice. Most corporate work flips to `managed` and never looks
back; one-off projects with a heavy custom Revit template flip to `external`.

---

## What the pack needs to grow (for `managed` mode to be complete)

Add to `ViewStylePack`:

```jsonc
{
  "templateMode": "managed",   // or "external"

  "viewRange": {                // plans only
    "topMm":            2300,
    "cutPlaneMm":       1200,
    "bottomMm":            0,
    "viewDepthMm":      -300
  },
  "underlay": {                 // plans only
    "baseLevel":    "below",    // "off" | "below" | "<level-name>"
    "topLevel":     "above",
    "orientation":  "look-down",
    "halftone":     true
  },
  "phaseFilter":     "Show All",     // by name
  "discipline":      "Architectural",// "Architectural"|"Structural"|"Mechanical"|"Electrical"|"Plumbing"|"Coordination"
  "visualStyle":     "HiddenLine",
  "sunSetting":      { "preset": "Lighting" },
  "displayOptions": {
    "shadows":       false,
    "sketchyLines":  false,
    "ambientShadows":false
  },
  "annotationCrop":  false,
  "farClipMm":      30000,             // sections / elevations
  "background":      "None",            // "None"|"Sky"|"Gradient"|"Image:..."

  // Bounds — used to keep auto-generated templates in sync with what user
  // actually wants. Anything not listed here, the runtime won't touch on
  // the auto-generated template (so user can manually tweak).
  "managedFields": [
    "vg", "filters", "detailLevel", "viewRange", "phaseFilter",
    "discipline", "visualStyle", "annotationCrop"
  ]
}
```

`managedFields` is the contract that protects user work: STING only writes
the fields you list. Everything else on the auto-generated template is
fair game for manual edits.

---

## Runtime sync algorithm

Pseudocode for `ManagedTemplateSyncer.EnsureTemplate(doc, pack, viewType)`:

```
1. Compute pack checksum (SHA-256 of pack JSON, scoped to managedFields)
2. Look for existing Revit view named  STING:<pack-id>:<viewType>
   - If absent:
       a. Create a stub view of viewType (ViewPlan / ViewSection / View3D…)
       b. Detach it from any active template
       c. Apply pack settings (VG, filters, scale, detail, phase, …)
       d. Stamp STING_PACK_ID_TXT, STING_PACK_CHECKSUM_TXT, STING_MANAGED_BOOL=1
       e. Convert to template (set IsTemplate via the supported API path)
       f. Cache template ElementId
   - If present:
       a. Read STING_PACK_CHECKSUM_TXT
       b. If matches → reuse, return
       c. If drift → re-apply only the fields in pack.managedFields,
          update checksum stamp
3. Return the template's ElementId
```

`DrawingTypePresentation.Apply` then:

```
if (pack.templateMode == "managed") {
    var tplId = ManagedTemplateSyncer.EnsureTemplate(doc, pack, view.ViewType);
    view.ViewTemplateId = tplId;
    // Pack VG / filters now live ON the template, not on the view —
    // so the existing ViewStylePackApplier.Apply becomes a no-op in
    // managed mode (it ran on the template, not the view).
} else {
    // existing behaviour: resolve pack.ViewTemplate by name + apply pack
    // VG/filters on the view (and warn if template is locked).
}
```

---

## What this gives you

- **One screen, all visual control.** Open the View Style Pack editor, edit
  VG + view range + phase + visual style. Save. Every drawing bound to that
  pack updates instantly on next regen.
- **No template name typos.** STING picks the template name; user never
  types it.
- **Versioning works.** Pack checksum drift is detectable (already wired in
  `DrawingDriftDetector`); a single `SyncStyles` re-applies all packs.
- **Project-portable.** Open the same project on a different machine — the
  STING template gets regenerated from the pack JSON; no need to ship
  pre-authored Revit templates.

---

## Known limits / risks

1. **Template lifecycle in Revit.** The auto-generated templates appear in
   the Project Browser (Templates section) named `STING:*`. Some teams may
   find this noisy. *Mitigation:* the Project Browser organizer that
   already groups by `STING_DRAWING_TYPE_ID_TXT` can be extended to fold
   STING templates into a dedicated branch.
2. **User edits a managed template in Revit's UI.** Next sync will revert
   any field listed in `managedFields`. *Mitigation:* show a warning
   sticker on the template's primary view, plus a "this template is
   STING-managed" tooltip in the editor.
3. **Migration.** Existing `pack.ViewTemplate` entries need to be either
   converted to managed (read the named template's settings into pack
   fields) or kept as `external`. Both are scriptable.
4. **API edge cases.** Some template fields (e.g. *Section Box* for 3D
   views, schedule appearance fields) require version-specific API calls.
   Defer these until a real project asks for them.
5. **VG editor visibility.** When `templateMode = managed`, the editor's
   "View template name" combo should hide / disable. We need a clear UI
   signal that the pack is *self-sufficient*.

---

## Phased implementation

Each phase is shippable on its own.

### Phase 1 — Scaffolding (~2 days)
- Add `templateMode`, `viewRange`, `phaseFilter`, `discipline`,
  `visualStyle`, `annotationCrop`, `farClipMm`, `displayOptions`,
  `managedFields` to `ViewStylePack`.
- Editor cards: View Range / Phase / Display under the existing Appearance
  card. Hide them when `templateMode == "external"`.
- New runtime class `ManagedTemplateSyncer` with `EnsureTemplate(doc, pack,
  viewType)`. Initially honours only VG + filters + detail level +
  phaseFilter + discipline (the cheap fields). Returns ElementId of the
  generated template.
- Wire `DrawingTypePresentation.Apply` to call the syncer when
  `pack.templateMode == "managed"`.

### Phase 2 — Full template fidelity (~3 days)
- Add View Range, Underlay, Sun, Visual Style, Display Options, Background,
  Annotation Crop, Far Clip, Photographic Exposure handling.
- Per-view-type template generation (`STING:<pack>:Plan`,
  `STING:<pack>:Section`, `STING:<pack>:Elevation`, `STING:<pack>:3D`,
  `STING:<pack>:Detail`, `STING:<pack>:Drafting`).
- Drift detection includes pack checksum stamp.

### Phase 3 — Migration tools (~1 day)
- Command: **"Convert Pack to Managed"** — picks an existing Revit template
  by name, reads its settings into pack fields, sets `templateMode =
  managed`. Removes the original template (or renames to
  `<original>_legacy` for safety).
- Command: **"Detach Pack from STING Management"** — copies pack settings
  out into a new Revit template, sets `templateMode = external` pointing
  at it.

### Phase 4 — Polish (~½ day)
- Project Browser organizer fold STING templates into a `STING — managed
  templates` branch.
- Editor sticker: "This pack manages 4 templates: STING:corp-plan:Plan,
  STING:corp-plan:Section, …" (so the user sees the side-effect).
- "Regenerate templates now" button on the pack editor toolbar.

---

## Recommendation

**Adopt the hybrid (Architecture C).** Default new packs to `managed`. Keep
`external` as the escape hatch. Build in phases: get Phase 1 shipping with
~80% of the value (VG + filters + phase + discipline), then layer Phase 2
on top as need surfaces.

The user's instinct — *don't go back to Revit for VG / template management*
— is correct, and this design honours it without throwing away the option
when an unusual project needs it.
