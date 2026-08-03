-- seed-loadtest-data.sql — fixture data for load/tier-capacity.js.
--
-- LOCAL DEV ONLY. It clones an existing user's password hash and grants project
-- membership in bulk. Never run against production.
--
--   docker exec -i docker-postgres-1 psql -U planscape -d planscape \
--     < load/seed-loadtest-data.sql
--
-- Creates:
--   * 400 users  loadtest1..400@planscape.demo  (password: same as SEED_EMAIL's)
--   * project membership for each, on the target project
--   * 5,000 issues on that project
--
-- WHY 400 USERS. MapControllers().RequireRateLimiting("api") budgets 100
-- req/min per user. Driving load through one account measures the rate limiter,
-- not the server -- an early run reported 91 req/s at a 97.95% failure rate and
-- a 2 ms p95, which is the signature of instant 429s. 400 users buys
-- 400*100/60 ~= 666 req/s of headroom.
--
-- WHY 5,000 ISSUES. With an empty project the issue-list endpoint returns
-- {"items":[],"total":0} and its .Include() chains hydrate nothing, so the
-- measurement flatters the server badly: the same box measured a knee at
-- 240 req/s empty versus roughly 120-180 req/s with real rows.
--
-- Idempotent: re-running will not duplicate members. Users and issues WILL
-- duplicate on a second run -- clean up first if you need an exact count:
--   DELETE FROM "Issues" WHERE "IssueCode" LIKE 'ISS-%';
--   DELETE FROM "Users"  WHERE "Email" LIKE 'loadtest%';

\set ON_ERROR_STOP on

\set seed_email    '\'admin@planscape.demo\''
\set project_id    '\'ae61a15c-6040-4e5c-8170-42fb59b44ffb\''
\set user_count    400
\set issue_count   5000

-- ── Users ───────────────────────────────────────────────────────────────────
INSERT INTO "Users" ("Id","TenantId","Email","DisplayName","PasswordHash","Role",
                     "Iso19650Role","IsActive","CreatedAt","IsDeleted")
SELECT gen_random_uuid(), u."TenantId",
       'loadtest'||g||'@planscape.demo', 'Load Test '||g,
       u."PasswordHash", u."Role", u."Iso19650Role", true, now(), false
FROM "Users" u, generate_series(1, :user_count) g
WHERE u."Email" = :seed_email;

-- ── Project membership (copies the shape of an existing member row) ─────────
INSERT INTO "ProjectMembers" ("Id","TenantId","ProjectId","UserId","ProjectRole",
                              "Iso19650Role","IsActive","JoinedAt")
SELECT gen_random_uuid(), m."TenantId", m."ProjectId", u."Id",
       m."ProjectRole", m."Iso19650Role", true, now()
FROM "Users" u
CROSS JOIN LATERAL (
  SELECT pm.* FROM "ProjectMembers" pm WHERE pm."ProjectId" = :project_id LIMIT 1
) m
WHERE u."Email" LIKE 'loadtest%'
ON CONFLICT ("ProjectId","UserId") DO NOTHING;

-- ── Issues ──────────────────────────────────────────────────────────────────
INSERT INTO "Issues" ("Id","TenantId","ProjectId","IssueCode","Type","Title",
                      "Description","Priority","Status","Assignee","CreatedBy",
                      "CreatedAt","UpdatedAt","Discipline")
SELECT gen_random_uuid(), p."TenantId", p."Id",
       'ISS-'||lpad(g::text, 6, '0'),
       (ARRAY['Clash','RFI','Snag','Observation'])[1 + (g % 4)],
       'Load test issue '||g,
       repeat('Representative description text for load testing. ', 6),
       (ARRAY['Low','Medium','High','Critical'])[1 + (g % 4)],
       (ARRAY['Open','InProgress','Resolved','Closed'])[1 + (g % 4)],
       'loadtest'||(1 + (g % :user_count))||'@planscape.demo',
       'admin@planscape.demo',
       now() - (g || ' minutes')::interval, now(),
       (ARRAY['A','S','M','E','P'])[1 + (g % 5)]
FROM "Projects" p, generate_series(1, :issue_count) g
WHERE p."Id" = :project_id;

SELECT (SELECT count(*) FROM "Users" WHERE "Email" LIKE 'loadtest%')        AS users,
       (SELECT count(*) FROM "ProjectMembers" WHERE "ProjectId" = :project_id) AS members,
       (SELECT count(*) FROM "Issues" WHERE "ProjectId" = :project_id)      AS issues;
