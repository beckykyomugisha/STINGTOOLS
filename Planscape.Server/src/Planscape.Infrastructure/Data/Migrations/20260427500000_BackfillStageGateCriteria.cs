using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Planscape.Infrastructure.Data.Migrations;

/// <inheritdoc />
/// <remarks>
/// Phase 147 — backfill the normalised <c>StageGateCriteria</c> table from
/// the legacy <c>StageGate.CriteriaJson</c> blob. Idempotent — only copies
/// rows for gates that don't already have entries in the table (so a
/// project halfway through migration via the per-key auto-migrate path
/// doesn't get duplicates).
///
/// The legacy column is left in place here so any third-party reader has
/// a graceful deprecation window. Once we're confident no client reads
/// it, a follow-up migration can drop it.
/// </remarks>
public partial class BackfillStageGateCriteria : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Phase 148 — be self-sufficient. PostgreSQL 13+ has
        // gen_random_uuid() built-in, but PG 12 still needs pgcrypto.
        // Every current Planscape PG deployment already has pgcrypto
        // enabled (the initial migration loaded it), so this is
        // belt-and-braces: a fresh PG 12 instance running the
        // migrations cold no longer depends on a previous migration's
        // side-effect to succeed. IF NOT EXISTS makes it a no-op when
        // the extension is already present, so no extra permission is
        // needed on hosts that pre-installed it.
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

        // PG-only — the test/dev SQLite path uses an empty fresh DB so
        // there's nothing to back-fill.
        migrationBuilder.Sql(@"
WITH untouched_gates AS (
  SELECT g.""Id"" AS gate_id, g.""CriteriaJson"" AS criteria_json
  FROM ""StageGates"" g
  LEFT JOIN ""StageGateCriteria"" c ON c.""StageGateId"" = g.""Id""
  WHERE g.""CriteriaJson"" IS NOT NULL
    AND jsonb_typeof(g.""CriteriaJson""::jsonb) = 'array'
    AND c.""Id"" IS NULL
  GROUP BY g.""Id"", g.""CriteriaJson""
),
expanded AS (
  SELECT
    gate_id,
    elem,
    ord
  FROM untouched_gates,
       LATERAL jsonb_array_elements(criteria_json::jsonb) WITH ORDINALITY AS t(elem, ord)
)
INSERT INTO ""StageGateCriteria"" (
  ""Id"", ""StageGateId"", ""Key"", ""Label"", ""Description"", ""SortOrder"",
  ""Met"", ""EvidenceDocId"", ""SignedBy"", ""SignedByUserId"", ""SignedAt"",
  ""Comment"", ""CreatedAt"", ""UpdatedAt""
)
SELECT
  gen_random_uuid(),
  gate_id,
  COALESCE(elem->>'key', ''),
  COALESCE(elem->>'label', ''),
  elem->>'description',
  (ord - 1)::int,
  COALESCE((elem->>'met')::boolean, false),
  NULLIF(elem->>'evidenceDocId', '')::uuid,
  elem->>'signedBy',
  NULL,
  NULLIF(elem->>'signedAt', '')::timestamptz,
  elem->>'comment',
  now() AT TIME ZONE 'utc',
  now() AT TIME ZONE 'utc'
FROM expanded
WHERE COALESCE(elem->>'key', '') <> ''
ON CONFLICT (""StageGateId"", ""Key"") DO NOTHING;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Backfill is data-only and idempotent; the safe Down is a no-op
        // (don't strip data the operator may have edited since).
    }
}
