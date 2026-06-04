using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Planscape.API;

/// <summary>
/// Pre-merge Gate 2 — schema-drift self-check.
///
/// This codebase deliberately materialises its schema from the EF model
/// (EnsureCreated/CreateTables on a fresh DB) plus the idempotent
/// <see cref="PlatformSchemaPatcher"/> for pre-existing DBs, rather than from
/// an EF <c>[Migration]</c> baseline (see docs/adr/0001-schema-management.md
/// for the rationale — the legacy migrations carry hand-written RLS / partition
/// / trigger SQL that a model-generated baseline cannot reproduce, so a naive
/// <c>Migrate()</c> would silently drop tenant-isolation and tamper-evidence).
///
/// The risk that approach carries is <b>drift</b>: an entity added to the EF
/// model that nobody mirrors into the patcher will exist on fresh dev DBs (via
/// CreateTables) but be MISSING on a long-lived prod DB (where EnsureCreated
/// short-circuits) — and the gap is invisible until the first write throws
/// <c>relation/column does not exist</c> in production.
///
/// This checker closes that gap. It enumerates every table + column the live
/// EF model expects (the single source of truth) and diffs it against the
/// actual <c>information_schema</c>. Anything the model expects but the DB
/// lacks is reported. It runs at startup (always logs), and a CI / canary run
/// can set <c>Database:SchemaDriftStrict=true</c> (or env
/// <c>PLANSCAPE_SCHEMA_DRIFT_STRICT=true</c>) so a drifted schema fails the
/// boot — turning a silent prod 500 into a loud, pre-deploy red build.
///
/// Scope: PUBLIC-schema tables only. It reports MISSING tables/columns
/// (expected-but-absent), never EXTRA ones — Hangfire's own schema, table
/// partitions, and operator-added objects are legitimately not in the EF model
/// and must not trip the check.
/// </summary>
internal static class SchemaDriftChecker
{
    public sealed class DriftResult
    {
        public List<string> MissingTables { get; } = new();
        // table -> list of missing column names
        public Dictionary<string, List<string>> MissingColumns { get; } = new(StringComparer.Ordinal);
        public int ExpectedTableCount { get; set; }
        public bool HasDrift => MissingTables.Count > 0 || MissingColumns.Count > 0;
    }

    /// <summary>
    /// Compares the live EF model against the live DB schema.
    /// </summary>
    public static async Task<DriftResult> CheckAsync(DbContext db, DbConnection conn, CancellationToken ct = default)
    {
        var result = new DriftResult();

        // ── Expected: (table -> column set) from the EF relational model ──
        var expected = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var et in db.Model.GetEntityTypes())
        {
            var tableName = et.GetTableName();
            if (string.IsNullOrEmpty(tableName)) continue;          // view-mapped / keyless-no-table
            var schema = et.GetSchema();
            if (!string.IsNullOrEmpty(schema) &&
                !string.Equals(schema, "public", StringComparison.Ordinal))
                continue;                                            // only audit the public schema

            var storeId = StoreObjectIdentifier.Table(tableName, et.GetSchema());
            if (!expected.TryGetValue(tableName, out var cols))
            {
                cols = new HashSet<string>(StringComparer.Ordinal);
                expected[tableName] = cols;
            }
            foreach (var p in et.GetProperties())
            {
                var col = p.GetColumnName(storeId);
                if (!string.IsNullOrEmpty(col)) cols.Add(col!);
            }
        }
        result.ExpectedTableCount = expected.Count;

        // ── Actual: (table -> column set) from information_schema ──
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var actual = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT table_name, column_name FROM information_schema.columns " +
                "WHERE table_schema = 'public'";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var t = reader.GetString(0);
                var c = reader.GetString(1);
                if (!actual.TryGetValue(t, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    actual[t] = set;
                }
                set.Add(c);
            }
        }

        // ── Diff ──
        foreach (var (table, cols) in expected)
        {
            if (!actual.TryGetValue(table, out var actualCols))
            {
                result.MissingTables.Add(table);
                continue;
            }
            var missing = cols.Where(c => !actualCols.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
            if (missing.Count > 0) result.MissingColumns[table] = missing;
        }
        result.MissingTables.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>
    /// Runs <see cref="CheckAsync"/>, logs the verdict, and (when strict) throws
    /// so the host exits non-zero. Wire after the schema patcher in Program.cs.
    /// </summary>
    public static async Task AssertAsync(DbContext db, DbConnection conn, bool strict, CancellationToken ct = default)
    {
        DriftResult drift;
        try
        {
            drift = await CheckAsync(db, conn, ct);
        }
        catch (Exception ex)
        {
            // The checker must never be the reason the app fails to boot in the
            // non-strict (default) path — a malformed query or odd provider
            // shouldn't take production down. Strict mode still surfaces it.
            Console.WriteLine($"[schema-drift] check ERROR — {ex.GetType().Name}: {ex.Message}");
            if (strict) throw;
            return;
        }

        if (!drift.HasDrift)
        {
            Console.WriteLine($"[schema-drift] OK — {drift.ExpectedTableCount} EF tables match the live schema.");
            return;
        }

        Console.WriteLine($"[schema-drift] DRIFT DETECTED against {drift.ExpectedTableCount} EF tables:");
        foreach (var t in drift.MissingTables)
            Console.WriteLine($"[schema-drift]   MISSING TABLE  : {t}");
        foreach (var (t, cols) in drift.MissingColumns.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            Console.WriteLine($"[schema-drift]   MISSING COLUMNS: {t} -> {string.Join(", ", cols)}");
        Console.WriteLine(
            "[schema-drift] Remediation: add the missing table(s)/column(s) to " +
            "PlatformSchemaPatcher / PatchDevSchemaAsync (see docs/adr/0001-schema-management.md).");

        if (strict)
        {
            throw new InvalidOperationException(
                $"Schema drift detected: {drift.MissingTables.Count} missing table(s), " +
                $"{drift.MissingColumns.Count} table(s) with missing column(s). " +
                "See the [schema-drift] log lines above. " +
                "(Database:SchemaDriftStrict=true)");
        }
    }
}
