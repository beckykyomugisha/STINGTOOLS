-- ===========================================================================
-- Read-only. Safe to run in Render's psql shell against production.
-- Nothing here writes, locks, or reads personal data.
--
-- Context: PR "one authorization model" (#647 / prompt 14).
-- Local measurement 2026-08-18 is in the PR body; this confirms production.
-- ===========================================================================


-- ---------------------------------------------------------------------------
-- QUERY 1 — THE ONLY NUMBER THAT CHANGES THE WORK. Read this one first.
-- ---------------------------------------------------------------------------
--
-- ZERO      -> the gate is dead in production too. The PR widens who may edit
--              project settings from NOBODY to project managers and above,
--              exactly as framed. Nothing to decide; merge on the usual terms.
--
-- NON-ZERO  -> STOP. Those people can edit project settings TODAY, and
--              CanAdministerProject as written may not include them — so the
--              change could TAKE AWAY access rather than only grant it. That is
--              a different PR needing a different justification. Say so on the
--              PR and do not merge it as framed.
--
SELECT count(*) AS "rows_that_can_edit_settings_today"
FROM   "ProjectMembers"
WHERE  "Iso19650Role" IN ('K', 'C');


-- ===========================================================================
-- EVERYTHING BELOW IS CONTEXT. It does NOT change the decision above.
-- Skip it unless query 1 came back non-zero or you are curious.
-- ===========================================================================


-- CONTEXT A — the full ProjectMember ISO role distribution.
-- Flags values outside the canonical vocabulary that GET
-- api/projects/{id}/members/roles serves. Local run found two strays
-- ('S' and 'EL', one row each). Strays are TOLERATED by design: the PR
-- validates new writes without making an existing row unsaveable, and logs a
-- Warning at boot naming them. They do not block anything.
SELECT "Iso19650Role",
       count(*) AS rows,
       CASE WHEN "Iso19650Role" IN (
              'A','PM','BC','BA','AR','SE','ME','CE','QS','CA',
              'CT','SC','FM','OM','CL','M','V','Z')
            THEN 'canonical'
            ELSE 'STRAY — outside the served vocabulary'
       END AS status
FROM   "ProjectMembers"
GROUP  BY 1
ORDER  BY 3 DESC, 2 DESC;


-- CONTEXT B — the same for AppUser, which carries a DIFFERENT declared
-- vocabulary (AppUser.cs:14 says A/M/E/S/H/P/C/I/K/Q/F/W/L/Z). Evidence for
-- the follow-up issue proposing the two lists be reconciled. Locally this
-- column holds QS/BC/PM/AR/EL — values from the OTHER list — so it has drifted
-- away from its own documented vocabulary too.
SELECT "Iso19650Role", count(*) AS rows
FROM   "Users"
GROUP  BY 1
ORDER  BY 2 DESC;


-- CONTEXT C — identify the stray rows for the cleanup issue. Returns internal
-- IDs only, no names or emails: a human with project context decides what each
-- was meant to be. The PR deliberately does NOT guess a mapping.
SELECT m."Id"           AS project_member_id,
       m."ProjectId",
       m."Iso19650Role" AS stray_value,
       m."ProjectRole",
       m."IsActive"
FROM   "ProjectMembers" m
WHERE  m."Iso19650Role" NOT IN (
         'A','PM','BC','BA','AR','SE','ME','CE','QS','CA',
         'CT','SC','FM','OM','CL','M','V','Z')
ORDER  BY m."Iso19650Role";
