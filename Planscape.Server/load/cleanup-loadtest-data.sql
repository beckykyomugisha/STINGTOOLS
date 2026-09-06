-- cleanup-loadtest-data.sql — removes everything seed-loadtest-data.sql created.
--
-- LOCAL DEV ONLY, like the seed it undoes.
--
--   docker exec -i docker-postgres-1 psql -U planscape -d planscape \
--     < load/cleanup-loadtest-data.sql
--
-- Normally you do not run this by hand — load/run-capacity.sh runs it on exit,
-- including when the k6 run fails or is interrupted.
--
-- ── WHY THIS FILE EXISTS ────────────────────────────────────────────────────
-- The seed used to document its own cleanup as two DELETEs:
--
--   DELETE FROM "Issues" WHERE "IssueCode" LIKE 'ISS-%';
--   DELETE FROM "Users"  WHERE "Email" LIKE 'loadtest%';
--
-- The second one CANNOT SUCCEED. FK_ProjectMembers_Users_UserId is RESTRICT,
-- and the seed gives every loadtest user a ProjectMembers row, so the delete
-- aborts with a foreign-key violation and every user stays. That is not a
-- hypothetical: it is why a demo tenant was found holding 426 users against a
-- cap of 50, which read as a live onboarding blocker and sent two separate
-- investigations down the wrong path. The excess was never the defect — the
-- residue was.
--
-- Three FKs into "Users" are RESTRICT and therefore block the delete:
--   ProjectMembers.UserId · DevicePushTokens.UserId · PhotoNdaAcceptances.UserId
-- Everything else is SET NULL or CASCADE and needs no help. If a future
-- scenario writes a row into some other RESTRICT table, the final DELETE throws
-- and ON_ERROR_STOP makes psql exit non-zero — loudly, rather than leaving a
-- partial mess behind. Add the table here when that happens.

\set ON_ERROR_STOP on

\set project_id '\'ae61a15c-6040-4e5c-8170-42fb59b44ffb\''

BEGIN;

-- Issues first: scoped by BOTH project and code prefix. 'ISS-' is the seed's
-- own format; real seeded issues use RFI-/NCR-/CLASH-/SI-/BCF- and live on
-- other projects. Scoping by both means a future collision cannot make this
-- delete reach real data.
DELETE FROM "Issues"
 WHERE "ProjectId" = :project_id
   AND "IssueCode" LIKE 'ISS-%';

-- Then the RESTRICT dependants, in any order among themselves.
DELETE FROM "ProjectMembers"
 WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "Email" LIKE 'loadtest%');

DELETE FROM "DevicePushTokens"
 WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "Email" LIKE 'loadtest%');

DELETE FROM "PhotoNdaAcceptances"
 WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "Email" LIKE 'loadtest%');

-- Finally the users themselves.
DELETE FROM "Users" WHERE "Email" LIKE 'loadtest%';

-- ── Verify, in the same transaction ─────────────────────────────────────────
-- A cleanup that reports success while leaving rows behind is the whole bug
-- being fixed, so the check runs before COMMIT: any residue rolls the whole
-- thing back and exits non-zero rather than half-cleaning.
--
-- No psql variables are referenced inside the dollar-quoted body — psql does
-- not interpolate there, and a silently unsubstituted :project_id would make
-- this assert on the wrong set.
DO $$
DECLARE
    stale_users   int;
    stale_members int;
BEGIN
    SELECT count(*) INTO stale_users
      FROM "Users" WHERE "Email" LIKE 'loadtest%';

    SELECT count(*) INTO stale_members
      FROM "ProjectMembers" pm
      JOIN "Users" u ON u."Id" = pm."UserId"
     WHERE u."Email" LIKE 'loadtest%';

    IF stale_users > 0 OR stale_members > 0 THEN
        RAISE EXCEPTION
            'loadtest cleanup INCOMPLETE: % users and % project members remain. '
            'Transaction rolled back; the database is unchanged. Look for a new '
            'RESTRICT foreign key into "Users" and add it to this file.',
            stale_users, stale_members;
    END IF;
END $$;

COMMIT;

SELECT (SELECT count(*) FROM "Users" WHERE "Email" LIKE 'loadtest%')          AS users_remaining,
       (SELECT count(*) FROM "Issues" WHERE "ProjectId" = :project_id
                                        AND "IssueCode" LIKE 'ISS-%')         AS issues_remaining;
