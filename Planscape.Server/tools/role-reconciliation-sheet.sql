-- role-reconciliation-sheet.sql — one row per project member, for hand-resolution.
--
-- READ-ONLY. Produces the sheet the product owner fills in before any role
-- migration runs. Nothing here writes.
--
--   docker exec -i docker-postgres-1 psql -U planscape -d planscape \
--     -f - --csv < tools/role-reconciliation-sheet.sql > role-reconciliation.csv
--
-- ── WHY A SHEET AND NOT AN AUTOMATIC RULE ───────────────────────────────────
-- ProjectMember.Iso19650Role holds values from TWO different vocabularies:
--
--   ISO 19650 ROLE (ProjectMembersController.GetRoles)
--     A PM BC BA AR SE ME CE QS CA CT SC FM OM CL M V Z
--   STING DISCIPLINE (ASS_DISCIPLINE_COD_TXT, TAG_CONFIG_v5_0_DISC_SYS_FUNC.csv)
--     A E FP H LV M MG P RP S
--
-- They overlap on exactly TWO codes, and those two are the most common values
-- in the data:
--     "A" -> Appointing Party (role)  OR  Architectural (discipline)
--     "M" -> Model Author    (role)  OR  Mechanical    (discipline)
--
-- A stored "M" cannot be resolved by looking at it. Capability is derived from
-- the role, so guessing wrong grants or denies real permissions. Hence: propose,
-- flag confidence, and let someone who knows these people by name decide.
--
-- The confidence column is the point of the sheet. Sort by it and only the
-- AMBIGUOUS and REVIEW rows need a human.

WITH role_vocab(code) AS (VALUES
    ('A'),('PM'),('BC'),('BA'),('AR'),('SE'),('ME'),('CE'),('QS'),
    ('CA'),('CT'),('SC'),('FM'),('OM'),('CL'),('M'),('V'),('Z')),
discipline_vocab(code) AS (VALUES
    ('A'),('E'),('FP'),('H'),('LV'),('M'),('MG'),('P'),('RP'),('S')),
member AS (
    SELECT pm."Id"            AS member_id,
           p."Code"           AS project,
           u."DisplayName"    AS display_name,
           u."Email"          AS email,
           pm."ProjectRole"   AS project_role,
           pm."Iso19650Role"  AS iso_stored,
           u."Role"::text     AS app_user_role,
           (pm."Iso19650Role" IN (SELECT code FROM role_vocab))       AS in_role_vocab,
           (pm."Iso19650Role" IN (SELECT code FROM discipline_vocab)) AS in_disc_vocab
      FROM "ProjectMembers" pm
      JOIN "Users"    u ON u."Id"  = pm."UserId"
      JOIN "Projects" p ON p."Id"  = pm."ProjectId"
)
SELECT
    member_id,
    project,
    display_name,
    email,
    project_role,
    iso_stored,
    app_user_role,

    -- Proposed ISO 19650 role. Blank where a human must choose.
    CASE
        WHEN in_role_vocab AND NOT in_disc_vocab THEN iso_stored
        WHEN in_role_vocab AND in_disc_vocab     THEN NULL          -- A / M: ambiguous
        ELSE CASE project_role                                       -- fall back to ProjectRole
                 WHEN 'Manager'     THEN 'PM'
                 WHEN 'Coordinator' THEN 'BC'
                 WHEN 'Contributor' THEN 'BA'
                 WHEN 'Viewer'      THEN 'V'
                 WHEN 'ClientGuest' THEN 'CL'
                 WHEN 'PM'          THEN 'PM'   -- legacy value; see ProjectRoles.LegacyProjectRolePm
                 ELSE NULL                       -- Owner / Admin have no ISO equivalent
             END
    END AS proposed_iso_role,

    -- Proposed discipline. Only inferable when the stored code is
    -- discipline-only; otherwise unknown and left for the sheet.
    CASE
        WHEN in_disc_vocab AND NOT in_role_vocab THEN iso_stored
        ELSE NULL
    END AS proposed_discipline,

    CASE
        WHEN in_role_vocab AND in_disc_vocab THEN
            'AMBIGUOUS — "' || iso_stored || '" is BOTH an ISO role and a discipline. Human must choose.'
        WHEN iso_stored IS NOT NULL AND NOT in_role_vocab AND NOT in_disc_vocab THEN
            'REVIEW — "' || iso_stored || '" is in neither vocabulary (likely a typo; e.g. EL for E).'
        WHEN in_role_vocab THEN
            'HIGH — unambiguous ISO role; kept as-is.'
        WHEN in_disc_vocab THEN
            'HIGH — unambiguous discipline; ISO role derived from ProjectRole.'
        WHEN project_role IN ('Owner','Admin') THEN
            'REVIEW — ProjectRole "' || project_role || '" has no ISO equivalent.'
        ELSE
            'MEDIUM — no stored ISO value; ISO role derived from ProjectRole alone.'
    END AS confidence,

    ''::text AS approved_iso_role,      -- ← product owner fills these two in
    ''::text AS approved_discipline
FROM member
ORDER BY
    CASE
        WHEN in_role_vocab AND in_disc_vocab THEN 0                          -- ambiguous first
        WHEN iso_stored IS NOT NULL AND NOT in_role_vocab AND NOT in_disc_vocab THEN 1
        WHEN project_role IN ('Owner','Admin') THEN 2
        ELSE 3
    END,
    project, display_name;
