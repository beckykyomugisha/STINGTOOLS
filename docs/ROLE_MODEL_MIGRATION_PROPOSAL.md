# Role model: four fields → three — proposal

**Status: PROPOSED. Nothing in this document has been run.** The reconciliation
sheet generator (`Planscape.Server/tools/role-reconciliation-sheet.sql`) is
read-only and is the only executable artefact.

Measured 2026-08-06 against the local dev database. Production has not been
measured — every count below is dev, and the sheet must be regenerated against
production before anything is approved.

---

## Why this carries no revenue risk

Entitlement is the **StingTools licence** (signed, offline, machine-bound), not a
role. One licence = one author seat. Billing does not read `ProjectRole`,
`Iso19650Role`, or `AppUser.Role` — so this migration cannot mis-bill anyone.
Its blast radius is **permissions**, which is precisely why it is being done by
hand rather than by rule.

---

## 1. The problem: one column, two vocabularies

`ProjectMember.Iso19650Role` holds values drawn from **two different and
unrelated vocabularies**:

| Vocabulary | Source | Codes |
|---|---|---|
| ISO 19650 **role** | `ProjectMembersController.GetRoles()` | `A PM BC BA AR SE ME CE QS CA CT SC FM OM CL M V Z` |
| STING **discipline** | `ASS_DISCIPLINE_COD_TXT`, `TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv` | `A E FP H LV M MG P RP S` |

They overlap on **exactly two codes**, and those two are the most common values
in the data:

- **`A`** → Appointing Party (role) **or** Architectural (discipline)
- **`M`** → Model Author (role) **or** Mechanical (discipline)

This also explains values previously dismissed as invalid: **`S` is Structural**,
a perfectly good discipline code that was only ever wrong because it was stored
in a column being read as a role.

### Measured impact (dev, 34 member rows)

| Confidence | Rows | Meaning |
|---|---|---|
| **AMBIGUOUS** | **20 (59%)** | stored code is `A` or `M` — unresolvable by inspection |
| HIGH | 13 | stored code belongs to exactly one vocabulary |
| REVIEW | 1 | `EL` — in neither vocabulary (near-miss for `E`) |

**An automatic rule would silently mis-resolve the majority of rows.** Capability
is derived from role, so a wrong resolution is a wrong permission. Hence: no
auto-migration.

The display names show why a human is needed rather than a cleverer rule —
`Contributor + A` on *"Architectural Lead"* is obviously the discipline, while
`Manager + A` on *"BIM Coordinator"* is genuinely arguable. Only someone who
knows these people can say.

---

## 2. Target shape: four fields → three

| Field | Purpose | Vocabulary | Change |
|---|---|---|---|
| `AppUser.Role` (`UserRole`) | **Authorization** | `Viewer…Owner`, `SecurityOfficer` | **UNCHANGED** |
| `ProjectMember.Iso19650Role` | **Responsibility + capability source** | ISO 19650 role | Becomes the single role field |
| `ProjectMember.Discipline` *(new)* | **Trade** | STING discipline | New column |
| ~~`ProjectMember.ProjectRole`~~ | — | — | **Retired** after backfill |

`AppUser.Role` stays because every admin surface is
`[Authorize(Roles = "Admin,Owner")]`. Turning it into an ISO code means
rewriting authorization across the API — a separate change with its own risk,
and not required by this one.

`AppUser.Iso19650Role` exists too and carries the same ambiguity; it is included
in the sheet as context but is **out of scope** for this pass. Retiring it is a
follow-up once `ProjectMember` is clean.

---

## 3. Capability map (explicit, to be tested)

Derived from the ISO role only. Preserves the intent #540 established — curate is
broader than approve, and the two are evaluated separately.

| ISO role | Curate | Approve photos | Author information |
|---|---|---|---|
| `A` Appointing Party | ✅ | ✅ | ✅ |
| `PM` Project Manager | ✅ | ✅ | ✅ |
| `BC` BIM Coordinator | ✅ | ❌ | ✅ |
| `CA` Contract Administrator | ✅ | ❌ | ✅ |
| `BA` BIM Author · `M` Model Author | ❌ | ❌ | ✅ |
| `AR` `SE` `ME` `CE` `QS` | ❌ | ❌ | ✅ |
| `CT` Main Contractor · `SC` Subcontractor | ❌ | ❌ | ✅ |
| `FM` · `OM` | ❌ | ❌ | ✅ |
| `CL` Client Representative | ❌ | ❌ | ❌ |
| `V` Viewer | ❌ | ❌ | ❌ |
| `Z` Unassigned | ❌ | ❌ | ❌ |

The map lives beside `CanCurate` / `CanApproveSitePhotos` in `ProjectRoles`, with
a test asserting every code in `GetRoles()` has exactly one entry — so adding a
role to the API without deciding its capability fails the build rather than
defaulting to deny (or worse, allow).

---

## 4. Migration sequence — proposed, not run

**Step 1 — schema, additive only.**
Add `ProjectMember.Discipline` and `ProjectMember.RoleMigrationBackup`
(text, holds the pre-migration `ProjectRole|Iso19650Role` pair). Nothing is
dropped. Fully reversible: the backup column reconstructs the original state.

**Step 2 — dual-write.**
Writers set both the old and new fields; readers prefer the new field and fall
back to the old. The system is correct at every point, and the change can be
abandoned at any point without data loss.

**Step 3 — reconciliation sheet.**
Generate against **production**:

```bash
docker exec -i <pg> psql -U planscape -d planscape --csv \
  -f - < Planscape.Server/tools/role-reconciliation-sheet.sql > role-reconciliation.csv
```

One row per member: name, email, current `ProjectRole`, current
`Iso19650Role`, current `AppUser.Role`, a proposed resolution, and a confidence
flag. Sorted so AMBIGUOUS and REVIEW rows come first. The product owner fills in
`approved_iso_role` and `approved_discipline`. Only the flagged rows need
attention — on dev that is 21 of 34.

**Step 4 — backfill from the approved sheet only.**
Import the completed CSV; write `Iso19650Role` and `Discipline` from the
*approved* columns, never from the proposed ones. Refuse to run if any flagged
row is unapproved — a half-approved sheet must fail loudly, not partially apply.

**Step 5 — retire `ProjectRole`.**
Only after the backfill is verified and dual-write has run clean for a release.
Drop it in a separate migration, with `RoleMigrationBackup` retained for at
least one further release.

---

## 5. What is deliberately NOT in this proposal

- **No auto-migration.** See §1.
- **`AppUser.Role` is untouched.** Rewriting `[Authorize]` is its own change.
- **`AppUser.Iso19650Role`** carries the same ambiguity and is a follow-up.
- **No billing coupling.** Entitlement is the licence; billing reads none of
  these fields, before or after.
- **`EL`** is not silently corrected to `E`. It goes to the sheet as REVIEW,
  because a plausible typo fix is still a guess about a permission.
