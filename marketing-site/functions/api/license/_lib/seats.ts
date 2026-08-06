// The single definition of "how many licences is this tenant currently using".
//
// This is one function rather than one query-per-caller on purpose. The seat
// meter on the Planscape server counted ProjectMembers carrying a role string
// that nothing ever wrote, while the paths that actually sold a seat wrote
// AppUser rows — so an accepted invite could never move the number it was
// checked against. Two queries in two files drifted apart and nobody noticed,
// because each one looked correct on its own.
//
// So: the path that SPENDS a seat (issue.ts, when it decides whether the tenant
// is at cap) and the path that REPORTS on one (present.ts) both call this. A
// change to what counts as "in use" reaches both or neither. The test in
// tests/license-present.test.ts asserts against this same function, so "the
// number moved" means the number the cap is actually checked against.

// A licence counts against the cap unless it has been revoked or has expired.
// Re-issuing for a machine already licensed updates that machine's row instead
// of inserting another, so a reinstall never double-counts (see issue.ts).
export async function countLicensedSeats(
  db: D1Database,
  tenantId: string,
  nowIso: string
): Promise<number> {
  const row = await db
    .prepare(
      `SELECT COUNT(*) AS n FROM licenses
        WHERE tenant_id = ? AND revoked_at IS NULL AND expires_at > ?`
    )
    .bind(tenantId, nowIso)
    .first<{ n: number }>();
  return row?.n ?? 0;
}
