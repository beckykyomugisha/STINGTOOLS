# STING IDS Authoring Guide

For authors writing new IDS files in `shared/ifc/ids/`. Captures the
gotchas surfaced during Phase 186 verification + the conventions
STING uses across its IDS catalogue.

> **TL;DR**: IDS v1.0 is stricter than it looks. The schema rejects
> wrong child-element order, mis-placed `minOccurs`, abstract entity
> classes, and IFC version strings outside a fixed allow-list. Run
> every new file through `python3 -c "import xmlschema, ifctester;
> xmlschema.XMLSchema(ifctester.__file__.replace('__init__.py',
> 'ids.xsd')).validate('your.ids')"` before committing.

## File skeleton — the safe template

```xml
<?xml version="1.0" encoding="UTF-8"?>
<ids xmlns="http://standards.buildingsmart.org/IDS"
     xmlns:xs="http://www.w3.org/2001/XMLSchema"
     xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
     xsi:schemaLocation="http://standards.buildingsmart.org/IDS http://standards.buildingsmart.org/IDS/1.0/ids.xsd">

  <info>
    <title>STING — &lt;short scope&gt;</title>
    <copyright>Planscape Limited, 2026</copyright>
    <version>1.0.0</version>
    <description>One paragraph: what this IDS validates, who consumes
    it, what it deliberately does not cover.</description>
    <author>info@stingtools.io</author>
    <date>2026-MM-DD</date>
    <purpose>Where this IDS sits in the validation pipeline.</purpose>
    <milestone>Stage_&lt;N&gt; or Stage_&lt;N&gt;_through_Stage_&lt;M&gt;</milestone>
  </info>

  <specifications>

    <specification name="Human_readable_spec_name"
                   identifier="01-SHORT-CODE"
                   ifcVersion="IFC4 IFC4X3_ADD2">
      <applicability minOccurs="1" maxOccurs="unbounded">
        <entity>
          <name>
            <xs:restriction base="xs:string">
              <xs:pattern value="IFC(WALL|DOOR|WINDOW|SLAB|BEAM|COLUMN)"/>
            </xs:restriction>
          </name>
        </entity>
      </applicability>
      <requirements>
        <property dataType="IFCLABEL" cardinality="required">
          <propertySet><simpleValue>Pset_StingTags</simpleValue></propertySet>
          <baseName><simpleValue>Discipline</simpleValue></baseName>
          <value>
            <xs:restriction base="xs:string">
              <xs:enumeration value="M"/>
              <xs:enumeration value="E"/>
              <!-- ... -->
            </xs:restriction>
          </value>
        </property>
      </requirements>
    </specification>

  </specifications>
</ids>
```

## Three gotchas to know about

### Gotcha 1 — `<info>` children have a strict order

The IDS schema requires `<info>`'s children in this exact order:

```
title  →  copyright  →  version  →  description  →  author  →  date  →
purpose  →  milestone
```

Order any of them differently and the file fails XSD validation with
"Unexpected child with tag '…' at position N".

`ifcVersion` is **not** an `<info>` child — it's a per-specification
**attribute**.

### Gotcha 2 — `minOccurs` / `maxOccurs` go on `<applicability>`, never on `<specification>`

```xml
<!-- WRONG — schema rejects -->
<specification name="x" minOccurs="1" maxOccurs="unbounded" ifcVersion="IFC4">
  <applicability>
    <entity>…</entity>
  </applicability>
</specification>

<!-- RIGHT -->
<specification name="x" ifcVersion="IFC4">
  <applicability minOccurs="1" maxOccurs="unbounded">
    <entity>…</entity>
  </applicability>
</specification>
```

**Cardinality semantics**:
- `minOccurs="1" maxOccurs="unbounded"` — at least one element must
  match the applicability, otherwise the spec is reported as
  FAILED. Use this when the spec is meant to catch missing data
  (e.g. "every wall must have Discipline").
- `minOccurs="0" maxOccurs="unbounded"` — zero or more matches are
  fine. Use this for "when present, validate" specs (e.g. "if
  TokenLock is set, it must be a comma-separated list").

### Gotcha 3 — Abstract IFC entity classes match nothing

ifctester does **exact class matching**, not inheritance-aware.
`IfcElement` is abstract — no concrete instance reports
`is_a() == "IfcElement"`. The entity facet `<name><simpleValue>
IFCELEMENT</simpleValue></name>` therefore matches **zero entities**
in any model.

Use `<xs:pattern>` to cover concrete classes:

```xml
<entity>
  <name>
    <xs:restriction base="xs:string">
      <xs:pattern value="IFC(WALL|DOOR|WINDOW|SLAB|BEAM|COLUMN|ROOF|STAIR|RAILING|CEILING|COVERING|CURTAINWALL|PLATE|MEMBER|FOOTING|PILE|RAMP|FURNISHINGELEMENT|FLOWTERMINAL|FLOWFITTING|FLOWCONTROLLER|FLOWSEGMENT|DISTRIBUTIONELEMENT|BUILDINGELEMENTPROXY|MECHANICALFASTENER|ELECTRICAPPLIANCE|LIGHTFIXTURE|SANITARYTERMINAL|AIRTERMINAL|FIREDETECTORTYPE|ALARMTYPE)"/>
    </xs:restriction>
  </name>
</entity>
```

This is the **STING standard taggable-element pattern**. It covers
the 31 concrete classes STING tags by default. Domain packs (health-
care, fabrication) extend the pattern in their own IDS files; never
override it in the core specs.

## Other rules worth remembering

| Topic | Rule |
|---|---|
| `ifcVersion` attribute | Allowed values: `IFC2X3`, `IFC4`, `IFC4X3_ADD2`. **NOT** `IFC4X3` (without ADD2) — that's not in the IDS v1.0 enum |
| `<partOf>` children | Wrap entity names in `<entity>`: `<partOf><entity><name><simpleValue>IFCBUILDING</simpleValue></name></entity></partOf>` |
| Spec identifiers | 2-digit family number + dash + short verb code: `01-LOC-IS-ENUM`, `04-BUILDING-HAS-LOC`. Stable across releases — never reuse an identifier |
| Description text | One sentence. Multi-line wreaks havoc on report rendering |
| Cross-entity equality | **Cannot** be expressed in IDS v1.0. Use a partOf-presence-only check + close the equality gap in STING-side `SpatialChecker` |
| Multi-hop containment | **Cannot** be expressed. `partOf` checks immediate-container only. Wall → Storey works; Wall → Building doesn't. Split into two specs + STING-side traversal |
| Uniqueness | **Cannot** be expressed. Use STING-side or server-side checks for `SEQ_UNIQUE_WITHIN_GROUP` and friends |
| `<value>` restriction | Use `<xs:enumeration>` for closed sets (≤ 20 values), `<xs:pattern>` for grammar-based (e.g. SEQ regex), `<xs:minLength>` + `<xs:maxLength>` to bound free-text |

## Per-domain conventions

| Domain | Required first specs |
|---|---|
| Core tag grammar | `10-DISC` through `17-SEQ` mirroring the 8 segments; `18-FULLTAG` for derived consistency; `19-TOKENLOCK` + `20-MODIFIEDAT` for behavioural-rule format checks |
| Spatial codes | `01a/01b LOC`, `02a/02b LVL`, `03a/03b ZONE` (pattern + presence-on-spatial-container), `04-BUILDING-HAS-LOC`, `05-STOREY-HAS-LVL` |
| Healthcare (Phase 187) | Per-facility-profile entity-filter patterns (clinical theatres, MRI suites, isolation rooms); MGS gas-type enumeration; pressure-regime bounds; ligature-rating enumeration |
| Stage-aware overlay | One spec per "must-be-set-by-Stage-N" rule; applicability narrowed by `phase` attribute or property check |

## Validation pipeline

Every new IDS file goes through three checks before merge:

1. **XSD conformance** — must validate against the ifctester `ids.xsd`:
   ```bash
   python3 -c "
   import xmlschema, ifctester
   from pathlib import Path
   schema = xmlschema.XMLSchema(str(Path(ifctester.__file__).parent / 'ids.xsd'))
   schema.validate('shared/ifc/ids/your-file.ids')
   print('OK')
   "
   ```

2. **Execution against the canonical positive fixture** — every
   declared spec must pass on `tests/fixtures/spatial_codes_ok.ifc`
   (regenerate via `python3 tools/tests/round_trip.py
   --generate-fixture` if absent). At least one spec should pass.

3. **Execution against a negative fixture** — specifically crafted
   to fire the new specs. Add a `--mismatch-kind YOUR_FEATURE`
   variant to `round_trip.py` if the existing fixtures don't cover
   it.

The CI workflow `.github/workflows/ifc-substrate.yml` runs steps 1
on every PR. Steps 2 + 3 are the author's responsibility before
opening the PR.

## When IDS isn't the right tool

IDS v1.0 deliberately constrains itself to per-element rules. The
following STING checks live elsewhere:

| Check | Where it lives |
|---|---|
| LOC/LVL/ZONE equality with spatial container | `stingtools_core.spatial.SpatialChecker` |
| SEQ uniqueness across (Disc, Sys, Lvl) | Same |
| FullTag consistency | Same |
| Token lock + retag history (behavioural) | Each host plugin's tag-write path + audit log |
| Project-wide uniqueness of LocationCode | Planscape Server IFC-ingest path |
| Document register lifecycle | Planscape Server + template engine |
| Workflow conditional gating | `WorkflowEngine` (Revit) / `stingtools_bonsai/workflows/engine.py` (Bonsai) |

If your rule is per-element + value-based, write an IDS spec. If
it's cross-entity / cross-element / cross-time, write a SpatialChecker
extension or a server-side check.

## Reference reading

- buildingSMART IDS v1.0 specification: https://github.com/buildingSMART/IDS
- ifctester source + schema: pip install ifctester; `import ifctester;
  ifctester.__file__`
- `shared/ifc/ids/sting-spatial-codes-rules.md` — companion to the
  spatial-codes IDS, documents per-rule encoding decisions and the
  STING-side closeouts.
- `shared/ifc/psets/Pset_StingTags.xml` — source-of-truth for which
  rules are statically vs behaviourally enforced. Look for
  `enforced-by="host"` on rules that can't be expressed in IDS.
