#!/usr/bin/env bash
#
# squash-migrations.sh — collapse all EF Core migrations into a single,
# clean InitialCreate baseline.
#
# WHY
#   The migration history drifted ~58 entities behind the model across many
#   un-migrated phases. Nothing has applied these migrations to a real
#   database yet (dev uses EnsureCreated; production isn't deployed), so the
#   correct path is to collapse them into ONE InitialCreate that builds the
#   entire current schema. Run this ONCE, before the first production deploy.
#
# WHAT IT PRESERVES
#   The model now declares the two objects that previously lived only in raw
#   migration SQL — the pgcrypto extension and the Issues.CustomFields GIN
#   index — so a model-based InitialCreate reproduces them. The script
#   verifies both appear in the generated migration.
#
# WHAT IT DROPS (intentionally)
#   One-time DATA backfills (TenantId backfill, StageGateCriteria backfill).
#   These repaired EXISTING rows; a fresh production DB has nothing to
#   backfill, and SeedData.cs handles first-run seeding.
#
# SAFETY
#   Backs up the existing Migrations folder first and restores it if the
#   generation fails or produces column-altering ops (a clean baseline's
#   Up() is CreateTable-only).
#
# AFTER
#   1. Review + commit the regenerated Data/Migrations/.
#   2. Apply to a FRESH/empty production DB:  dotnet ef database update ...
#   3. Do NOT apply to a dev DB created by EnsureCreated — the tables
#      already exist and it will collide. Dev keeps using EnsureCreated.
#
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."        # -> Planscape.Server
MIGDIR="src/Planscape.Infrastructure/Data/Migrations"
INFRA="src/Planscape.Infrastructure"
API="src/Planscape.API"

command -v dotnet >/dev/null 2>&1 || { echo "ERROR: dotnet not found on PATH"; exit 1; }
dotnet tool restore >/dev/null 2>&1 || true

# ── Pre-flight: refuse to squash if migrations carry raw SQL ──────────────
# A model-based InitialCreate ONLY reproduces what's in the EF model. Raw
# SQL (RLS policies, triggers, functions, table partitions, special/partial
# indexes, extensions) would be SILENTLY DROPPED. This codebase has several
# such objects, so squashing is unsafe here — prefer an additive migration.
rawsql_files="$(grep -lE '\.Sql\(' "${MIGDIR}"/*.cs 2>/dev/null | grep -v Designer | grep -v Snapshot || true)"
if [[ -n "${rawsql_files}" ]]; then
  echo "!! REFUSING TO SQUASH — these migrations contain raw SQL a model-based"
  echo "   InitialCreate cannot reproduce (RLS / triggers / functions /"
  echo "   partitions / special indexes / extensions):"
  echo "${rawsql_files}" | sed 's#.*/#     - #'
  echo ""
  echo "   RECOMMENDED instead — keep all migrations, add ONE for the drift:"
  echo "     dotnet ef migrations add AddDriftedEntities -p ${INFRA} -s ${API} -o Data/Migrations"
  echo ""
  echo "   Only if you have manually re-homed every schema-level raw-SQL"
  echo "   object (into the model or into follow-up migrations) should you"
  echo "   re-run with FORCE_SQUASH=1."
  [[ "${FORCE_SQUASH:-0}" == "1" ]] || exit 3
  echo "   FORCE_SQUASH=1 — proceeding; you accept the raw-SQL loss."
fi

stamp="$(date +%Y%m%d_%H%M%S)"
backup="migrations_backup_${stamp}"
echo "==> Backing up ${MIGDIR} -> ${backup}/"
cp -r "${MIGDIR}" "${backup}"

echo "==> Removing existing migrations + snapshot"
rm -f "${MIGDIR}"/*.cs

echo "==> Generating fresh InitialCreate baseline (builds the whole model)"
if ! dotnet ef migrations add InitialCreate -p "${INFRA}" -s "${API}" -o Data/Migrations; then
  echo "!! Generation failed — restoring backup."
  rm -f "${MIGDIR}"/*.cs
  cp -r "${backup}/." "${MIGDIR}/"
  exit 1
fi

newmig="$(ls "${MIGDIR}"/*_InitialCreate.cs 2>/dev/null | head -1 || true)"
if [[ -z "${newmig}" ]]; then
  echo "!! InitialCreate not produced — restoring backup."
  rm -f "${MIGDIR}"/*.cs
  cp -r "${backup}/." "${MIGDIR}/"
  exit 1
fi

echo "==> Verifying the baseline"
creates="$(grep -cE 'migrationBuilder\.CreateTable' "${newmig}" || true)"
danger="$(grep -cE 'migrationBuilder\.(AlterColumn|RenameColumn|DropColumn)' "${newmig}" || true)"
has_pgcrypto="$(grep -c 'pgcrypto' "${newmig}" || true)"
has_gin="$(grep -c 'IX_Issues_CustomFields_gin\|gin' "${newmig}" || true)"

echo "    CreateTable ............................. ${creates}"
echo "    AlterColumn/RenameColumn/DropColumn ..... ${danger}   (expect 0)"
echo "    pgcrypto extension present .............. ${has_pgcrypto}   (expect >=1)"
echo "    CustomFields GIN index present .......... ${has_gin}   (expect >=1)"

fail=0
[[ "${danger}" == "0" ]]    || { echo "!! baseline has column-altering ops — NOT clean"; fail=1; }
[[ "${has_pgcrypto}" -ge 1 ]] || { echo "!! pgcrypto extension missing from baseline"; fail=1; }
[[ "${has_gin}" -ge 1 ]]      || { echo "!! CustomFields GIN index missing from baseline"; fail=1; }

if [[ "${fail}" != "0" ]]; then
  echo ""
  echo "Verification FAILED. Review ${newmig} (backup kept at ${backup}/)."
  echo "Restore with:  rm -f ${MIGDIR}/*.cs && cp -r ${backup}/. ${MIGDIR}/"
  exit 2
fi

echo ""
echo "==> SUCCESS — clean InitialCreate baseline:"
echo "    ${newmig}"
echo ""
echo "Next:"
echo "  1. Review it, then: git add ${MIGDIR} && commit; rm -rf ${backup}/"
echo "  2. Apply to a FRESH/empty production DB:"
echo "       dotnet ef database update -p ${INFRA} -s ${API}"
echo "  3. Never run 'database update' against an EnsureCreated dev DB."
