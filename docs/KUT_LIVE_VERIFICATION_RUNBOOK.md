# KUT live-verification runbook — ACC · Niagara · Fohlio

**Written 2026-09-10**, against the code at that date. Companion to
[`KUT_INTEGRATION_READINESS_2026-09.md`](KUT_INTEGRATION_READINESS_2026-09.md), which measured
*what is built*. This says *how to prove it works against the real thing*.

Three integrations are named in the issued KUT documents and are therefore promises. Two of them —
ACC and Niagara — are **wired but unproven**: real transports that have never met a live tenant or
station. The third — Fohlio — works on its contracted CSV route, and its outstanding item is a
signature, not a code change.

Nothing here can be done by a developer alone. Each section names **who must act first**, because
that is the whole reason these items are still open.

> This is an internal `docs/` file. It names product, command and parameter names freely; none of
> that vocabulary appears in anything issued to the Owner.

---

## What each section gives you

| | |
|---|---|
| **Who must act first** | The person without whom nothing below can start |
| **What they must supply** | The exact values, and what each one is |
| **Steps** | What to do once the values arrive |
| **Observable proof** | What you must see. Not "no error" — a positive, countable observation |
| **When it does not work** | The failure you will actually get, and what it means |

**Do not substitute a mock for any of these.** A mocked verification closes the item in everyone's
mind while leaving it open in reality, which is worse than an item that is visibly outstanding.

---

## B1 — ACC (Autodesk Construction Cloud)

**Status: overdue.** Mobilisation began the week of **25 August 2026**, and the fortnightly
Coordination Cycle depends on this. It is the single most time-critical item in the readiness report
and the only one nobody on the delivery side can close alone.

### Who must act first

| Person | What only they can do |
|---|---|
| **Owner / Autodesk account holder** | Create the APS (Autodesk Platform Services) application and register its callback URL |
| **ACC project administrator** | Read out the container ids for Issues and, if it differs, Model Coordination |

### What they must supply

| Field | What it is | Where it goes |
|---|---|---|
| `ClientId` | The APS app's Client ID | BIM Coordination Center → Platforms → **ACC** card → Save Credentials |
| `ClientSecret` | The APS app's Client Secret | same card |
| `ProjectId` | The **Issues container** id, in `b.<guid>` form | same card |
| `CoordContainerId` | The **Model Coordination** container id — set it **only if it differs** from `ProjectId`; left empty it falls back to `ProjectId` | same card |

Both ids are `REPLACE_WITH_ACC_CONTAINER_ID` until the ACC project administrator supplies them.
Do not derive, guess or pattern-match them from a project name: a wrong container id is the exact
input that used to make a coordination cycle report a clean federation (see *When it does not work*).

The credentials are written to `%APPDATA%\Planscape\acc_credentials.json`. **That file is never
committed and never leaves the machine it was created on.**

### Two things that catch people

**The APS app must be a "Traditional Web App".** That is the confidential app type — it issues a
**Client Secret**, which is what the token exchange needs: the code authenticates the token endpoint
with HTTP Basic over `ClientId:ClientSecret`. A Single-Page App or Desktop/Mobile app issues no
secret, and sign-in will fail at the token exchange no matter how correct everything else is.

**The callback URL must be registered, exactly:**

```
http://localhost:8910/callback
```

Sign-in opens a browser, then listens on loopback port 8910 for Autodesk to redirect back. If that
URL is not on the app's allowed-callback list, Autodesk refuses the redirect and the sign-in never
returns. Register it verbatim — `http`, not `https`; `localhost`, not `127.0.0.1`; the `/callback`
path included.

### Steps

1. Owner creates the APS app (**Traditional Web App**), registers `http://localhost:8910/callback`,
   and grants it the scopes `data:read data:write account:read` (Issues + Model Coordination). The
   **upload** path additionally needs `data:create` — grant all four if ACC Docs upload is wanted.
2. ACC project administrator reads out the Issues container id and, if different, the Model
   Coordination container id.
3. In Revit: BIM Coordination Center → **Platforms** → **ACC** card. Enter Client ID, Client Secret
   and Project ID. **Save Credentials.**
4. Click **Sign in with Autodesk.** A browser opens; sign in with an account that can see the ACC
   project; the browser returns to a local confirmation page.
5. Click **Test / Refresh.** This proves the stored refresh token still yields an access token.
6. **The single highest-value check — run this one even if you run nothing else.** With a model set
   that is **known to contain clashes**, run `ACC_PullClashes` (dock panel → BIM tab → clash section
   → **ACC Pull**, or the ACC card).

Step 6 is the point of the whole exercise. The clash service's `tests` and `resources` sub-paths
under `bim360/clash/v3` are the **only genuine residual** in the ACC path — they were written from
the public APS sample rather than confirmed against a tenant. One live pull settles them.

You do **not** need to hand-enter `IssueTypeId`. `EnsureIssueTypeAsync` fetches the container's issue
types, prefers a Clash/Coordination type, caches the choice and persists it. Leave it empty.

### Observable proof

| Step | What you must see |
|---|---|
| Sign in | The browser returns to a success page, and **Test / Refresh** then succeeds |
| Model sets | The picker lists **at least one named model set**. An empty picker is now reported as "Autodesk answered, and this container has no coordination model sets", naming the container — which is a real answer, not a silent pass |
| Clash pull | A **non-zero clash count** in the dialog, a top-10 triage list with real document names on both sides, and a written CSV whose path the dialog prints. Open the CSV and confirm the row count matches the reported count |
| Issue push (optional) | Choose "Push top N clashes to ACC Issues", then **open ACC in a browser** and see the issues there. The dialog's own count is not the proof; the issues in ACC are |

**Choose a model set that has clashes.** A pull against a genuinely clean set proves the plumbing
but not the sub-paths, because the same empty answer arrives whether or not the resource hop worked.

### When it does not work

Since the 2026-09-10 fix, a failed read **no longer looks clean**. `ACC_PullClashes` returns
`Result.Failed` and shows a dialog that opens with:

```
Could not read clashes for model set '<name>' from ACC.
NOTHING WAS CHECKED — this is not a clean result.

Failure: NotFound
Reason:  Autodesk returned HTTP 404 Not Found — the container id, model set or
         clash-service sub-path is wrong
Container id used: b.<the id it actually used>
```

Read the **Failure** line and act on it:

| Failure | What it means | What to do |
|---|---|---|
| `AuthFailed` | Autodesk rejected the token | Sign in again. If that fails, the app type is probably wrong (see "Traditional Web App") or the account cannot see this project |
| `NotFound` | Wrong container id, wrong model set, or a changed clash sub-path | Check `ProjectId` / `CoordContainerId` against what the ACC administrator gave you. **If both are confirmed correct, this is the residual** — the `bim360/clash/v3` sub-paths need re-confirming against current APS documentation. Record the exact URL from `StingTools_<date>.log` and file it |
| `TransportFailed` | Network failure, or a 200 whose payload shape this client does not recognise | Check reachability of `developer.api.autodesk.com`. A 200 with an unrecognised payload also means a schema change — same action as `NotFound` |

A dialog that says *"Either the model set is clash-clean, or a clash test has not completed in ACC
yet"* now appears **only** after a request that genuinely succeeded. Before the fix it also appeared
after a 404, and the step still returned success — a coordination cycle that passed without having
checked anything.

**Two operational facts worth knowing before scheduling this.** `ACC_PullClashes` opens a model-set
picker, so the Coordination Cycle **cannot run unattended**. And step 6 of that cycle, `ACCPublish`,
builds a **local** ACC-ready bundle for manual upload — it is labelled accordingly and does not
publish to the CDE. The real uploader is the separate `ACC_UploadModel` command (dock panel → BIM
tab → **ACC Upload**), which asks which file to upload; it is deliberately in no workflow, because a
workflow step cannot answer that question and guessing would put an unintended file in an issued
container.

---

## B2 — Niagara (Tridium) BMS

**Status: not due until Stage 3.1 (~M40).** Nothing about this blocks mobilisation. It is written
now so that whoever picks it up in three years does not have to read source code to learn the field
names — which, until 2026-09-10, is exactly what they would have had to do.

### What is and is not promised

The delivery playbook commits to two things, and the **file-mediated** path serves both:

| Stage | Commitment | Command |
|---|---|---|
| 3.1 (~M40) | A commissioning point list exported from the model for the controls contractor to load — model-driven, not hand-built | `Niagara_ExportPoints` |
| 3.3 | Reconcile model equipment and points against the live station | `Niagara_Reconcile`, against a station export |

Neither needs a network connection to the station. **The live HTTP read below is an optional extra**
(it feeds the BMS commissioning valuation), not a playbook promise. If the live read proves
awkward, the promises are still met by the file route.

### Who must act first

| Person | What only they can do |
|---|---|
| **MEP/BIM authoring team** | Populate `ICT_HEALTHIOT_*` on BMS-monitored equipment during Stage 2.3. Until that exists, `Niagara_ExportPoints` correctly exports nothing and says so |
| **Controls / commissioning contractor** | The station base URL, the **oBIX points path**, and credentials |

### What they must supply

Copy [`docs/examples/KUT/niagara_connection.json.example`](examples/KUT/niagara_connection.json.example)
to `<project>/_BIM_COORD/niagara_connection.json` and fill it in. The real file is **gitignored and
never committed**. The keys are case-sensitive and read verbatim; a mistyped key is a silent default,
not an error.

| Key | What it is |
|---|---|
| `baseUrl` | Station base URL — `REPLACE_WITH_STATION_BASE_URL` |
| `pointsPath` | The station's points feed path — `REPLACE_WITH_STATION_POINTS_PATH` |
| `apiKey` | Bearer token, **or** leave empty and use the pair below |
| `username` / `password` | HTTP Basic credentials, **or** leave empty and use `apiKey` |

**`pointsPath` is the one to get right.** The code defaults it to `/obix`, which is the oBIX *lobby*,
not a points feed — the real path is normally deeper. **Ask the controls contractor for it. Do not
guess it**, because a wrong path returns nothing, and nothing is what an unpopulated station also
returns.

### Steps

1. Confirm Stage 2.3 has populated `ICT_HEALTHIOT_*` on the monitored equipment.
2. Run `Niagara_ExportPoints`. Hand the export to the controls contractor to load.
3. (Stage 3.3) Take a station export and run `Niagara_Reconcile` against it. **This satisfies the
   playbook. Everything below is optional.**
4. (Optional live read) Obtain the four values above, write `niagara_connection.json`, and run the
   BMS-driven valuation.

### Observable proof

| Step | What you must see |
|---|---|
| Export | A **non-zero point count**. "No BMS/IoT points found" is honest and means step 1 is not done — it is not a transport failure |
| Reconcile | A matched/unmatched breakdown with **non-zero totals on both sides** |
| Live read | The provenance line reads **`live (captured …)`**. If it reads `CACHED … (station unreachable — last good read)` or `no BMS data`, the live read did **not** happen — the valuation is still correct, but it is not evidence the transport works |

That provenance line is the proof. A valuation can never silently run on stale points, so the
absence of `live (captured …)` is a definite negative, not an ambiguity.

### When it does not work

The client returns **null** on a transport failure and an **empty dictionary** for a station that
answered with nothing, and the two are reported differently. A feed whose id field is named something
unexpected is counted as `SkippedNoId` rather than passing as an empty station — so "the station is
empty" and "we could not read the station" cannot be confused.

Most likely causes, in order: wrong `pointsPath`; wrong auth pair (`apiKey` set *and* `username`
set — use one); station not reachable from the machine. This transport **has never been run against a
live station**, so treat the first fetch as a verification exercise with the controls contractor
present, not a routine one.

---

## B3 — Fohlio field mapping sign-off

**Status: on the mobilisation critical path. Not a code task.**

### Who must act first

**The Interior Designer**, who owns the Fohlio record. This is a meeting.

### What is outstanding

The delivery playbook makes *"field mapping agreed with Fohlio"* a mobilisation obligation. The
shipped map (`project-templates/KUT/_BIM_COORD/fohlio_map.json`) was authored on the delivery side
and **nothing in the repository evidences the Interior Designer's agreement to it.** The code works;
the agreement is missing.

The contracted route is the **CSV/XLSX** exchange, and the issued BEP already discloses and mitigates
the REST-tier gap (*"Fohlio API delay → use file/Add-in route now"*), so no API key is needed for
this contract and none should be requested.

### Steps

1. Send the current column mapping to the Interior Designer — headers, the Revit/parameter each maps
   to, and the fact that import matches **by Room Number**.
2. Walk one real round trip together: export → they enrich in Fohlio → import back, with the diff
   preview shown before anything is written.
3. Record the agreement (date, who agreed, the version of the map agreed) so a later dispute has an
   answer.
4. Amend `fohlio_map.json` if they want different columns, and re-run the shipped map test.

### Observable proof

| Step | What you must see |
|---|---|
| Round trip | Export produces **one row per placed room**, keyed by room number, with a non-zero row count |
| Import | The **diff preview lists actual changes** before writing. A preview with nothing in it means the enriched file did not match — check the room-number column first |
| Currency | The monthly `Fohlio_Audit` reports a **linked %** and named missing-reference items, not just a total |
| Agreement | A dated note naming the agreed map version. Without it, this item stays open regardless of how well the code runs |

### When it does not work

An import that matches nothing is almost always a room-number mismatch — Fohlio's identifier column
was renamed, or the rooms were renumbered after export. Re-export and compare the two files' key
columns before changing any code.

---

## What is deliberately not here

- **Fohlio REST.** Stubbed, and disclosed as such in the issued BEP. Every method throws; there is no
  connection test to run, because a connection test that made no connection was removed on
  2026-09-10 rather than left to pass against a typo.
- **BACnet / OPC-UA readback.** Stubbed by design. An FM add-on, promised by no KUT document.

Neither has a verification step because neither has anything to verify. Adding one would create the
appearance of coverage.
