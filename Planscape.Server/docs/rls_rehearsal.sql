\set ON_ERROR_STOP on
\echo '================ RLS REHEARSAL ================'

-- Non-superuser role. Superusers ALWAYS bypass RLS, so testing as `postgres`
-- would pass vacuously no matter what the policy said.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rls_app') THEN
    EXECUTE 'DROP OWNED BY rls_app CASCADE';
    EXECUTE 'DROP ROLE rls_app';
  END IF;
END $$;
CREATE ROLE rls_app LOGIN PASSWORD 'rehearsal' NOSUPERUSER NOBYPASSRLS;

DROP TABLE IF EXISTS "Projects";
CREATE TABLE "Projects" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Name" text NOT NULL);
ALTER TABLE "Projects" OWNER TO rls_app;

INSERT INTO "Projects" VALUES
  ('11111111-1111-1111-1111-111111111111','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','Tenant A - Alpha'),
  ('22222222-2222-2222-2222-222222222222','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','Tenant A - Beta'),
  ('33333333-3333-3333-3333-333333333333','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','Tenant B - Secret');

\echo ''
\echo '--- STEP 1: RED. No policy yet. Tenant A must be able to see Tenant B. ---'
\echo '--- (If this is already 0, the rest of the test proves nothing.)      ---'
SET ROLE rls_app;
SET app.current_tenant = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
SELECT count(*) AS "RED_total_rows_visible_expect_3" FROM "Projects";
SELECT count(*) AS "RED_tenantB_rows_visible_expect_1" FROM "Projects"
  WHERE "TenantId" = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
RESET ROLE;

\echo ''
\echo '--- STEP 2: apply the EXACT SQL RlsPolicyPatcher.PolicyFor("Projects") emits ---'
ALTER TABLE "Projects" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Projects" FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON "Projects";
CREATE POLICY tenant_isolation ON "Projects"
    USING ("TenantId"::text = current_setting('app.current_tenant', true))
    WITH CHECK ("TenantId"::text = current_setting('app.current_tenant', true));

\echo ''
\echo '--- STEP 2b: idempotency — run it a second time, must not error ---'
ALTER TABLE "Projects" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "Projects" FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON "Projects";
CREATE POLICY tenant_isolation ON "Projects"
    USING ("TenantId"::text = current_setting('app.current_tenant', true))
    WITH CHECK ("TenantId"::text = current_setting('app.current_tenant', true));
\echo 'idempotent re-apply OK'

\echo ''
\echo '--- STEP 3: GREEN positive path. Tenant A sees its OWN rows (must be NON-EMPTY = 2) ---'
SET ROLE rls_app;
SET app.current_tenant = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
SELECT count(*) AS "GREEN_tenantA_own_rows_expect_2" FROM "Projects";
SELECT "Name" AS "GREEN_tenantA_names_expect_2_rows" FROM "Projects" ORDER BY "Name";

\echo ''
\echo '--- STEP 4: GREEN cross-tenant. Tenant A must see ZERO of Tenant B ---'
SELECT count(*) AS "GREEN_tenantB_visible_to_A_expect_0" FROM "Projects"
  WHERE "TenantId" = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

\echo ''
\echo '--- STEP 5: the other tenant is symmetric (non-empty, and only its own) ---'
SET app.current_tenant = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
SELECT count(*) AS "GREEN_tenantB_own_rows_expect_1" FROM "Projects";
SELECT "Name" AS "GREEN_tenantB_name_expect_Secret" FROM "Projects";

\echo ''
\echo '--- STEP 6: FAIL CLOSED. GUC unset => zero rows, not everything ---'
RESET app.current_tenant;
SELECT count(*) AS "FAILCLOSED_unset_guc_expect_0" FROM "Projects";

\echo ''
\echo '--- STEP 7: WITH CHECK blocks cross-tenant WRITE too ---'
SET app.current_tenant = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
\echo 'attempting INSERT of a Tenant B row while acting as Tenant A (must fail):'
DO $$
BEGIN
  INSERT INTO "Projects" VALUES
    ('44444444-4444-4444-4444-444444444444','bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','Injected');
  RAISE EXCEPTION 'WRITE_NOT_BLOCKED — WITH CHECK did not hold';
EXCEPTION WHEN insufficient_privilege THEN
  RAISE NOTICE 'WRITE BLOCKED as expected (insufficient_privilege)';
END $$;
RESET ROLE;

\echo ''
\echo '--- STEP 8: ROLLBACK — the exact BuildRollbackStatements() SQL ---'
DROP POLICY IF EXISTS tenant_isolation ON "Projects";
ALTER TABLE "Projects" NO FORCE ROW LEVEL SECURITY;
ALTER TABLE "Projects" DISABLE ROW LEVEL SECURITY;

\echo ''
\echo '--- STEP 9: key OFF => nothing changed. All 3 rows visible again. ---'
SET ROLE rls_app;
SET app.current_tenant = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
SELECT count(*) AS "ROLLEDBACK_total_visible_expect_3" FROM "Projects";
RESET ROLE;

\echo ''
\echo '--- CLEANUP ---'
DROP TABLE IF EXISTS "Projects";
DROP OWNED BY rls_app CASCADE;
DROP ROLE IF EXISTS rls_app;
\echo '================ REHEARSAL COMPLETE ================'
