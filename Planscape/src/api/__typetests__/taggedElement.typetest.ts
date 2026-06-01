/**
 * Compile-time conformance test for the `TaggedElement` wire contract
 * (Drift 2). The mobile app has no jest runner, so this is a *type-level*
 * test: it is validated by `tsc --noEmit` (`npm run typecheck`). If the
 * `TaggedElement` interface drifts from the server's serialized shape,
 * one of the assertions below fails to compile.
 *
 * `SERVER_SAMPLE` is a captured response from
 *   GET /api/tagsync/elements/search
 * i.e. the raw `TaggedElement` entity serialized by ASP.NET Core's default
 * camelCase policy (see Planscape.Server .../Entities/TaggedElement.cs).
 *
 * Assigning the (inferred-type) sample to a `TaggedElement` slot checks
 * it is assignable — catching a missing/renamed REQUIRED field. Because
 * the sample is a named const (not a fresh literal in the typed slot),
 * structural typing correctly *allows* the extra wire-only fields the
 * mobile interface omits (`syncedBy`/`version`/…), mirroring runtime
 * `JSON.parse` where excess keys are simply ignored. The `@ts-expect-error`
 * block below catches the reverse: a verbose alias sneaking back in.
 *
 * This file is type-only and exports nothing used at runtime, so the
 * bundler drops it from the app.
 */
import type { TaggedElement } from '../../types/api';

// A representative row exactly as the server emits it (camelCase). Note
// BOTH `lvl` (code) and `level` (name) are present and distinct.
const SERVER_SAMPLE = {
  id: 'e3f1c2a4-0000-4000-8000-000000000001',
  tenantId: '11111111-1111-4111-8111-111111111111',
  projectId: '22222222-2222-4222-8222-222222222222',
  revitElementId: 350421,
  uniqueId: 'a1b2c3d4-0000-0000-0000-000000000000-000559d5',
  disc: 'M',
  loc: 'BLD1',
  zone: 'Z01',
  lvl: 'L02',
  sys: 'HVAC',
  func: 'SUP',
  prod: 'AHU',
  seq: '0042',
  tag1: 'M-BLD1-Z01-L02-HVAC-SUP-AHU-0042',
  tag7: 'Air Handling Unit serving Level 2 supply air',
  tag7A: 'AHU-02',
  tag7B: 'HVAC / Supply',
  tag7C: 'BLD1 · Z01 · L02',
  tag7D: 'NEW · P01',
  tag7E: '4500 l/s, 1.2 kW',
  tag7F: 'Ss_65_40_08',
  categoryName: 'Mechanical Equipment',
  familyName: 'M_AHU',
  typeName: 'AHU-4500',
  status: 'NEW',
  rev: 'P01',
  gridRef: 'C-4',
  roomName: 'Plant Room',
  level: 'Level 2',
  isStale: false,
  isComplete: true,
  isFullyResolved: false,
  validationErrors: null,
  previousTag: null,
  source: 'revit',
  syncedAt: '2026-06-01T09:30:00Z',
  lastModifiedUtc: '2026-06-01T09:29:58Z',
  // Server also emits these (entity has them); the interface intentionally
  // omits them as not-needed-by-mobile. As a named const (below) assigned
  // to a TaggedElement slot, these extra keys are allowed by structural
  // typing — exactly as runtime JSON.parse ignores them.
  syncedBy: 'sting.davis',
  version: 1,
  tagModifiedAt: '2026-06-01T09:29:58Z',
};

// Assignability check: the captured wire row must satisfy the interface.
// Variable (not fresh literal) ⇒ no excess-property false-positive on the
// extra wire-only keys above; still fails if a REQUIRED field is missing
// or mistyped.
const _conforms: TaggedElement = SERVER_SAMPLE;
void _conforms;

// ── Direction 1: every interface field exists on the sample ───────────
// Spot-check the fields the old verbose interface got wrong, plus the
// lvl/level disambiguation that this fix exists to resolve.
const _tag1: string = SERVER_SAMPLE.tag1;
const _disc: string = SERVER_SAMPLE.disc;
const _sys: string = SERVER_SAMPLE.sys;
const _prod: string = SERVER_SAMPLE.prod;
const _seq: string = SERVER_SAMPLE.seq;
const _rev: string | null | undefined = SERVER_SAMPLE.rev;
const _tag7: string | null | undefined = SERVER_SAMPLE.tag7;
const _lvlCode: string = SERVER_SAMPLE.lvl; // CODE
const _levelName: string | null | undefined = SERVER_SAMPLE.level; // NAME — distinct
void [_tag1, _disc, _sys, _prod, _seq, _rev, _tag7, _lvlCode, _levelName];

// ── Direction 2: the interface must NOT resurrect the verbose aliases ─
// These property accesses MUST be type errors. `@ts-expect-error` flips
// that: tsc fails to compile if the property unexpectedly *does* exist
// (i.e. if someone re-adds `assTag1` etc.), turning a silent regression
// into a build break.
const el = {} as TaggedElement;
// @ts-expect-error — server has no `assTag1`; the field is `tag1`
void el.assTag1;
// @ts-expect-error — server has no `discipline`; the field is `disc`
void el.discipline;
// @ts-expect-error — server has no `systemType`; the field is `sys`
void el.systemType;
// @ts-expect-error — server has no `productCode`; the field is `prod`
void el.productCode;
// @ts-expect-error — server has no `sequenceNumber`; the field is `seq`
void el.sequenceNumber;
// @ts-expect-error — server has no `tag7Summary`; the field is `tag7`
void el.tag7Summary;
