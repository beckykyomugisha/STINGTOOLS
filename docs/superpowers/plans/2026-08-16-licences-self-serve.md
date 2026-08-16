# Self-Serve Licences Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `POST /api/license/issue` a client, so a customer can obtain a `.lic` without anyone running `curl`.

**Architecture:** A new `GET /api/license` returns the tenant's licences plus the seat numbers, reusing the exact helpers `issue.ts` gates on so the page can never contradict a refusal. A new static page `/licences` posts to `issue.ts`, hands back the signed licence as a file download, and renders the list. The plugin's activation dialog is changed to show the Stable machine code and link to that page.

**Tech Stack:** Cloudflare Pages Functions (TypeScript), D1/SQLite, vanilla HTML+JS (no framework, no build step), C#/WPF for the plugin dialog. Tests: `node:test` + esbuild + miniflare against a real D1.

**Spec:** `docs/superpowers/specs/2026-08-16-licences-page-design.md` — read it first.

**Branch:** `claude/licences-self-serve` (exists on origin, already carries the spec).

---

### Task 1: `GET /api/license` — the list endpoint

**Files:**
- Create: `marketing-site/functions/api/license/index.ts`
- Test: `marketing-site/tests/license.test.ts` (append)

- [ ] **Step 1: Add the GET helpers to the test file**

The existing `call()` only does POST. Add this directly below the `present` helper (around line 180), and add the import beside the other handler imports at the top:

```ts
import { onRequestGet as listLicenses } from "../functions/api/license/index";
```

```ts
function callGet(
  handler: unknown,
  h: Harness,
  headers: Record<string, string> = {}
): Promise<Response> {
  const request = new Request("https://planscape.build/api/license", {
    method: "GET",
    headers,
  });
  return (handler as (ctx: unknown) => Promise<Response>)({
    request,
    env: h.env,
    params: {},
  });
}

interface ListBody {
  cap: number | null;
  inUse: number;
  licences: Array<{
    machineCode: string;
    licensee: string;
    issuedAt: string;
    expiresAt: string;
    revokedAt: string | null;
    lastSeenAt: string | null;
    lastSeenPluginVersion: string | null;
    lastSeenRevitVersion: string | null;
  }>;
}

const list = (h: Harness) =>
  callGet(listLicenses, h, { Authorization: `Bearer ${h.token}` });
```

- [ ] **Step 2: Write the failing test**

Append to `tests/license.test.ts`:

```ts
// --- the list endpoint -----------------------------------------------------

test("the list reports the same seat numbers the cap is checked against", async (t) => {
  const h = await harness();

  const issued = await issue(h, "ADD3-E01C-3412-14C8-175E");
  assert.equal(issued.status, 200);
  const { license } = (await issued.json()) as { license: string };
  await present(h, { license, pluginVersion: "2.2.0", revitVersion: "2025" });

  const res = await list(h);
  assert.equal(res.status, 200);
  const body = (await res.json()) as ListBody;

  assert.equal(body.licences.length, 1);
  assert.equal(body.licences[0].machineCode, "ADD3-E01C-3412-14C8-175E");
  assert.equal(body.licences[0].lastSeenPluginVersion, "2.2.0");
  assert.equal(body.licences[0].lastSeenRevitVersion, "2025");
  assert.notEqual(body.licences[0].lastSeenAt, null);
  assert.equal(body.licences[0].revokedAt, null);

  // The numbers must come from the same helper issue.ts gates on. If the
  // endpoint grew its own query, this drifts silently — which is the exact
  // failure seats.ts exists to prevent.
  assert.equal(body.cap, CAP);
  assert.equal(body.inUse, await seats(h));
  assert.equal(body.inUse, 1);
});
```

- [ ] **Step 3: Run it and watch it fail**

Run: `cd marketing-site && npm test`
Expected: FAIL — esbuild cannot resolve `../functions/api/license/index`.

- [ ] **Step 4: Write the endpoint**

Create `marketing-site/functions/api/license/index.ts`:

```ts
// GET /api/license — what this tenant has licensed, and how much of its cap
// that uses.
//
// The numbers come from resolveCap + countLicensedSeats, the SAME pair issue.ts
// consults before refusing at cap and present.ts reports back. A second query
// here would be a second definition of "in use", and two definitions in two
// files is exactly how the server-side seat meter drifted (see _lib/seats.ts).
//
// Revoked and expired rows ARE returned, with their dates, so a user can see
// why a seat is or is not being consumed. countLicensedSeats excludes them from
// inUse on its own — this endpoint does not re-implement that rule.
//
// The signed licence text is deliberately absent: issue.ts persists the row but
// never the text, so a .lic exists exactly once, in the response that mints it.
// Recovery is re-issue, which reuses the seat.

import { withHandler } from "../auth/_lib/handler";
import { handlePreflight } from "../auth/_lib/cors";
import { requireAuth } from "../auth/_lib/auth";
import { unauthorized } from "../auth/_lib/errors";
import { getTenantById } from "../auth/_lib/db";
import { resolveCap } from "../auth/_lib/limits";
import { countLicensedSeats } from "./_lib/seats";
import type { Env } from "../auth/_lib/types";

interface LicenseRow {
  machine_code: string;
  licensee: string;
  issued_at: string;
  expires_at: string;
  revoked_at: string | null;
  last_seen_at: string | null;
  last_seen_plugin_version: string | null;
  last_seen_revit_version: string | null;
}

export const onRequestOptions: PagesFunction = async ({ request }) =>
  handlePreflight(request);

export const onRequestGet = withHandler(async ({ request, env }) => {
  const e = env as Env;
  const auth = await requireAuth(request, e);

  const tenant = await getTenantById(e.WAITLIST_DB, auth.tenantId);
  if (!tenant) throw unauthorized("Account no longer exists.");

  const res = await e.WAITLIST_DB.prepare(
    `SELECT machine_code, licensee, issued_at, expires_at, revoked_at,
            last_seen_at, last_seen_plugin_version, last_seen_revit_version
       FROM licenses
      WHERE tenant_id = ?
      ORDER BY created_at DESC`
  )
    .bind(auth.tenantId)
    .all<LicenseRow>();

  const cap = resolveCap(tenant.plan_product, tenant.plan_tier);

  return {
    // Infinity is not JSON. null means unlimited — the same convention
    // present.ts uses for licencesIncluded.
    cap: cap === Infinity ? null : cap,
    inUse: await countLicensedSeats(
      e.WAITLIST_DB,
      auth.tenantId,
      new Date().toISOString()
    ),
    licences: (res.results ?? []).map((r) => ({
      machineCode: r.machine_code,
      licensee: r.licensee,
      issuedAt: r.issued_at,
      expiresAt: r.expires_at,
      revokedAt: r.revoked_at,
      lastSeenAt: r.last_seen_at,
      lastSeenPluginVersion: r.last_seen_plugin_version,
      lastSeenRevitVersion: r.last_seen_revit_version,
    })),
  };
});
```

- [ ] **Step 5: Run the tests**

Run: `cd marketing-site && npm test`
Expected: PASS — 8 tests, 0 failures.

- [ ] **Step 6: Typecheck**

Run: `cd marketing-site && npm run typecheck`
Expected: no errors.

- [ ] **Step 7: Commit**

```bash
git add marketing-site/functions/api/license/index.ts marketing-site/tests/license.test.ts
git commit -m "feat(license): GET /api/license lists a tenant's licences and seat use"
```

---

### Task 2: Prove the list cannot leak across tenants

**Files:**
- Test: `marketing-site/tests/license.test.ts` (append)

- [ ] **Step 1: Write the failing test**

`reset()` inserts one tenant, so this seeds a second one and a licence against it. The token in the harness belongs to `TENANT_ID`, so the second tenant's row must not appear.

```ts
test("the list returns only the caller's tenant, never another tenant's machines", async (t) => {
  const h = await harness();

  await issue(h, "ADD3-E01C-3412-14C8-175E");

  const now = new Date().toISOString();
  const other = "tenant-test-0002";
  await h.db.batch([
    h.db
      .prepare(
        `INSERT INTO tenants
           (id, name, slug, country, currency, plan_product, plan_tier,
            subscription_status, trial_started_at, trial_ends_at, created_at)
         VALUES (?,?,?,?,?,?,?,?,?,?,?)`
      )
      .bind(other, "Other Firm", "other-firm", "UG", "USD", PLAN_PRODUCT,
            PLAN_TIER, "active", now,
            new Date(Date.now() + 30 * 86400_000).toISOString(), now),
    h.db
      .prepare(
        `INSERT INTO licenses
           (id, tenant_id, user_id, machine_code, licensee, issued_at,
            expires_at, created_at, updated_at)
         VALUES (?,?,?,?,?,?,?,?,?)`
      )
      .bind("lic-other-0001", other, "user-other-0001",
            "BEEF-BEEF-BEEF-BEEF-BEEF", "Other Firm", now,
            new Date(Date.now() + 365 * 86400_000).toISOString(), now, now),
  ]);

  const body = (await (await list(h)).json()) as ListBody;

  assert.equal(body.licences.length, 1, "only this tenant's machines");
  assert.equal(body.licences[0].machineCode, "ADD3-E01C-3412-14C8-175E");
  assert.equal(
    body.licences.some((l) => l.machineCode === "BEEF-BEEF-BEEF-BEEF-BEEF"),
    false,
    "another tenant's machine must never appear"
  );
  assert.equal(body.inUse, 1, "another tenant's licence must not count here");
});
```

- [ ] **Step 2: Run the tests**

Run: `cd marketing-site && npm test`
Expected: PASS (the `WHERE tenant_id = ?` in Task 1 already satisfies it — this test locks it in against a future edit).

- [ ] **Step 3: Commit**

```bash
git add marketing-site/tests/license.test.ts
git commit -m "test(license): the list cannot return another tenant's machines"
```

---

### Task 3: Revoked rows are shown but not counted

**Files:**
- Test: `marketing-site/tests/license.test.ts` (append)

- [ ] **Step 1: Write the failing test**

```ts
test("a revoked licence is listed as revoked and stops consuming a seat", async (t) => {
  const h = await harness();

  await issue(h, "ADD3-E01C-3412-14C8-175E");
  assert.equal(await seats(h), 1);

  await h.db
    .prepare(`UPDATE licenses SET revoked_at = ? WHERE tenant_id = ?`)
    .bind(new Date().toISOString(), TENANT_ID)
    .run();

  const body = (await (await list(h)).json()) as ListBody;

  // Visible, so a user can see WHY the seat came back.
  assert.equal(body.licences.length, 1);
  assert.notEqual(body.licences[0].revokedAt, null);

  // But not counted — and counted by the same helper, not by this endpoint.
  assert.equal(body.inUse, 0);
  assert.equal(body.inUse, await seats(h));
});
```

- [ ] **Step 2: Run the tests**

Run: `cd marketing-site && npm test`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add marketing-site/tests/license.test.ts
git commit -m "test(license): revoked licences are listed but excluded from seat use"
```

---

### Task 4: The list refuses an unauthenticated caller

**Files:**
- Test: `marketing-site/tests/license.test.ts` (append)

- [ ] **Step 1: Write the failing test**

```ts
test("the list refuses a caller with no token", async (t) => {
  const h = await harness();
  await issue(h, "ADD3-E01C-3412-14C8-175E");

  const res = await callGet(listLicenses, h); // no Authorization header
  assert.equal(res.status, 401);

  const body = (await res.json()) as { error: string };
  assert.match(body.error, /Authorization header/i);
});
```

- [ ] **Step 2: Run the tests**

Run: `cd marketing-site && npm test`
Expected: PASS — 11 tests, 0 failures.

- [ ] **Step 3: Commit**

```bash
git add marketing-site/tests/license.test.ts
git commit -m "test(license): the list requires authentication"
```

---

### Task 5: The `/licences` page

**Files:**
- Create: `marketing-site/licences.html`

**Do NOT add a `_redirects` rule for `/licences`.** Pages auto-canonicalises `.html`; a rule creates a redirect loop. The file says so (commit `d75f094a0`).

- [ ] **Step 1: Create the page**

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Licences — Planscape</title>
<meta name="theme-color" content="#E8912D">
<meta name="robots" content="noindex,nofollow">
<link rel="icon" type="image/svg+xml" href="/images/favicon.svg">
<link rel="canonical" href="https://planscape.build/licences">
<link rel="stylesheet" href="/assets/site.css">
<style>
.lc-wrap { max-width: 780px; margin: 40px auto; padding: 0 16px; }
.lc-head h1 { margin: 0 0 4px; font-size: 26px; }
.lc-head .sub { color: var(--muted); margin: 0 0 20px; }
.state { display: none; }
.state.shown { display: block; }
.spinner { font-size: 28px; text-align: center; padding: 40px 0; }
.card { border: 1px solid var(--line); border-radius: 12px; background: var(--card); padding: 20px 22px; margin-bottom: 16px; }
.card h2 { margin: 0 0 2px; font-size: 19px; }
.card .sub { color: var(--muted); font-size: 14px; margin: 0 0 14px; }
.row { display: flex; gap: 10px; flex-wrap: wrap; align-items: flex-start; }
input[type=text] { font-family: Consolas, ui-monospace, monospace; font-size: 15px; padding: 10px 12px; border: 1px solid var(--line); border-radius: 8px; min-width: 280px; text-transform: uppercase; }
.btn-primary { background: var(--accent); color: #fff; border: 0; padding: 11px 18px; font-size: 14px; font-weight: 600; border-radius: 8px; cursor: pointer; }
.btn-primary:hover { background: #d57f1f; }
.btn-primary[disabled] { opacity: .55; cursor: default; }
.btn-secondary { background: none; color: var(--ink); border: 1px solid var(--line); padding: 9px 16px; font-size: 14px; font-weight: 600; border-radius: 8px; cursor: pointer; }
.err { color: #8b2018; font-size: 14px; margin: 10px 0 0; }
.ok { border: 1px solid rgba(40,167,69,.35); background: rgba(40,167,69,.07); border-radius: 10px; padding: 14px 16px; margin-top: 14px; font-size: 14px; }
.stop { border: 1px solid rgba(204,51,34,.3); background: rgba(204,51,34,.07); color: #8b2018; border-radius: 10px; padding: 14px 16px; margin-top: 14px; font-size: 14px; }
.path { font-family: Consolas, ui-monospace, monospace; background: #faf8f5; border: 1px solid var(--line); border-radius: 6px; padding: 2px 6px; font-size: 13px; }
table.lic { width: 100%; border-collapse: collapse; font-size: 14px; }
table.lic th, table.lic td { text-align: left; padding: 9px 10px; border-bottom: 1px solid var(--line); }
table.lic th { font-size: 12px; text-transform: uppercase; letter-spacing: .04em; color: var(--muted); }
table.lic td.mono { font-family: Consolas, ui-monospace, monospace; font-size: 13px; }
.pill { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: 11px; font-weight: 700; }
.pill.active { background: rgba(40,167,69,.12); color: #1e7e34; }
.pill.expired { background: rgba(90,100,120,.12); color: #56607a; }
.pill.revoked { background: rgba(204,51,34,.1); color: #8b2018; }
.note { font-size: 13px; color: var(--muted); margin: 12px 0 0; }
</style>
</head>
<body>

<div class="lc-wrap">
  <div class="state shown" id="loading"><div class="spinner">◐</div></div>

  <div class="state" id="ready">
    <div class="lc-head">
      <h1>Licences</h1>
      <p class="sub" id="seat-line">—</p>
    </div>

    <div class="card">
      <h2>Licence a machine</h2>
      <p class="sub">Open STING Tools in Revit. The activation window shows a machine
        code — paste it here.</p>
      <div class="row">
        <input type="text" id="code" spellcheck="false" autocomplete="off"
               placeholder="ADD3-E01C-3412-14C8-175E" maxlength="24">
        <button type="button" class="btn-primary" id="issue-btn">Get licence</button>
      </div>
      <div class="err" id="issue-err" style="display:none"></div>
      <div id="issue-ok" style="display:none"></div>
    </div>

    <div class="card">
      <h2>Licensed machines</h2>
      <table class="lic" id="lic-table">
        <thead><tr>
          <th>Machine</th><th>Expires</th><th>Last seen</th><th>Plugin</th>
          <th>Revit</th><th>Status</th>
        </tr></thead>
        <tbody id="lic-body"></tbody>
      </table>
      <p class="note" id="lic-empty" style="display:none">No machines licensed yet.</p>
      <p class="note">A licence file is produced once, when it is issued — we do not
        keep a copy. Lost it? Enter the same machine code again; that reuses the
        machine's existing seat rather than spending another.</p>
    </div>
  </div>
</div>

<footer class="site">
  <div class="legal">© 2026 Planscape Ltd · Kampala, Uganda · All rights reserved</div>
</footer>

<script>
(function(){
  function $(id){ return document.getElementById(id); }
  function esc(s){ return String(s == null ? '' : s).replace(/[&<>"]/g, function(c){
    return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]; }); }

  // Session convention (see downloads.html): mint an access token from the
  // HttpOnly ps_refresh cookie and keep it in memory only. Never persisted.
  var accessToken = null;
  var CODE_RE = /^[0-9A-F]{4}(-[0-9A-F]{4}){4}$/;

  function toLogin(){ window.location.href = '/login'; }

  function fmtDate(iso){
    if (!iso) return '—';
    var d = new Date(iso);
    return isNaN(d.getTime()) ? '—' : d.toISOString().slice(0, 10);
  }

  function statusOf(l){
    if (l.revokedAt) return 'revoked';
    return new Date(l.expiresAt).getTime() <= Date.now() ? 'expired' : 'active';
  }

  function renderList(data){
    var cap = data.cap;
    $('seat-line').textContent = cap === null
      ? data.inUse + (data.inUse === 1 ? ' machine licensed' : ' machines licensed')
      : data.inUse + ' of ' + cap + ' machines licensed';

    var rows = data.licences || [];
    $('lic-empty').style.display = rows.length ? 'none' : 'block';
    $('lic-body').innerHTML = rows.map(function(l){
      var st = statusOf(l);
      // "never" is a fact; an empty cell reads as a missing value.
      var seen = l.lastSeenAt ? fmtDate(l.lastSeenAt) : 'never';
      return '<tr>' +
        '<td class="mono">' + esc(l.machineCode) + '</td>' +
        '<td>' + fmtDate(l.expiresAt) + '</td>' +
        '<td>' + seen + '</td>' +
        '<td>' + esc(l.lastSeenPluginVersion || '—') + '</td>' +
        '<td>' + esc(l.lastSeenRevitVersion || '—') + '</td>' +
        '<td><span class="pill ' + st + '">' + st + '</span></td>' +
      '</tr>';
    }).join('');
  }

  function loadList(){
    return fetch('/api/license', { headers: { 'Authorization': 'Bearer ' + accessToken } })
      .then(function(res){
        if (res.status === 401) { toLogin(); throw new Error('401'); }
        if (!res.ok) throw new Error('list');
        return res.json();
      })
      .then(renderList);
  }

  function saveFile(text){
    var blob = new Blob([text], { type: 'application/octet-stream' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url; a.download = 'StingTools.lic';
    document.body.appendChild(a); a.click(); document.body.removeChild(a);
    setTimeout(function(){ URL.revokeObjectURL(url); }, 1000);
  }

  function onIssued(data){
    var expired = new Date(data.expiresAt).getTime() <= Date.now();
    var ok = $('issue-ok');
    ok.style.display = 'block';

    if (expired) {
      // Do NOT hand over a dead licence, and do not overwrite a working one.
      // A lapsed trial still passes entitlement and mints an expired licence
      // (issue #677). This is a guard, not the fix.
      ok.className = 'stop';
      ok.innerHTML = '<strong>That licence is already expired.</strong> It expired on ' +
        fmtDate(data.expiresAt) + ', because this account\'s trial has ended. ' +
        'We have not downloaded it — installing it would replace a working licence ' +
        'with a dead one. <a href="/account">Choose a plan</a>, then try again.';
      return;
    }

    ok.className = 'ok';
    ok.innerHTML = '<strong>Licence issued for ' + esc(data.machineCode) + '</strong> — ' +
      'valid until ' + fmtDate(data.expiresAt) + '. Your download should have started.' +
      '<div style="margin-top:10px"><button type="button" class="btn-secondary" id="again-btn">Download again</button> ' +
      '<button type="button" class="btn-secondary" id="copy-btn">Copy licence text</button></div>' +
      '<p class="note">Save it as <span class="path">C:\\ProgramData\\Planscape\\StingTools\\StingTools.lic</span>, ' +
      'then restart Revit.</p>';

    saveFile(data.license);
    $('again-btn').addEventListener('click', function(){ saveFile(data.license); });
    $('copy-btn').addEventListener('click', function(){
      navigator.clipboard.writeText(data.license).then(function(){
        $('copy-btn').textContent = 'Copied';
      }, function(){ $('copy-btn').textContent = 'Copy failed'; });
    });
  }

  function issue(){
    var code = $('code').value.trim().toUpperCase();
    var err = $('issue-err');
    err.style.display = 'none';
    $('issue-ok').style.display = 'none';

    if (!CODE_RE.test(code)) {
      err.style.display = 'block';
      err.textContent = 'That machine code doesn\'t look right. It appears in the ' +
        'plugin as five groups of four, like ADD3-E01C-3412-14C8-175E.';
      return;
    }

    var btn = $('issue-btn');
    btn.disabled = true; btn.textContent = 'Issuing…';

    fetch('/api/license/issue', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + accessToken },
      body: JSON.stringify({ machineCode: code })
    }).then(function(res){
      if (res.status === 401) { toLogin(); throw new Error('401'); }
      return res.json().then(function(body){ return { ok: res.ok, body: body }; });
    }).then(function(r){
      if (!r.ok) {
        // The server's message is written for the customer (seat cap, locked
        // account). Show it rather than inventing our own.
        err.style.display = 'block';
        err.textContent = (r.body && r.body.error) || 'Could not issue a licence.';
        return;
      }
      onIssued(r.body);
      return loadList();
    }).catch(function(e){
      if (String(e.message) === '401') return;
      err.style.display = 'block';
      err.textContent = 'Could not reach the licensing service. Please try again.';
    }).then(function(){
      btn.disabled = false; btn.textContent = 'Get licence';
    });
  }

  $('issue-btn').addEventListener('click', issue);
  $('code').addEventListener('keydown', function(e){ if (e.key === 'Enter') issue(); });

  fetch('/api/auth/refresh', { method: 'POST', credentials: 'include' })
    .then(function(res){
      if (!res.ok) throw new Error('refresh');
      return res.json();
    })
    .then(function(j){
      accessToken = j && j.token;
      if (!accessToken) throw new Error('no token');
      return loadList();
    })
    .then(function(){
      $('loading').classList.remove('shown');
      $('ready').classList.add('shown');
    })
    .catch(function(){ toLogin(); });
})();
</script>
</body>
</html>
```

- [ ] **Step 2: Typecheck (the page is not TypeScript, but confirm nothing else broke)**

Run: `cd marketing-site && npm run typecheck`
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add marketing-site/licences.html
git commit -m "feat(licences): self-serve page to issue a licence and see licensed machines"
```

---

### Task 6: Point the plugin at the page, with the Stable code

**Files:**
- Modify: `StingTools/UI/ActivationDialog.cs:28`, `:32`, `:40`

`LicenseGate.MachineCode` is `MachineFingerprint.Current` — MachineGuid plus three WMI factors that fail transiently and flip the code. `LicenseGate.VerifyEither` already accepts Current **or** Stable, so this change is backwards compatible: licences already issued against Current keep working.

- [ ] **Step 1: Change the instruction text**

Replace line 28:

```csharp
                Text = "Send this machine code to Planscape (support@planscape.app) to receive your license file.",
```

with:

```csharp
                Text = "Get your licence at https://planscape.build/licences — sign in, " +
                       "paste the machine code below, and download the file.",
```

- [ ] **Step 2: Show the Stable code**

Replace line 32:

```csharp
                Text = LicenseGate.MachineCode, IsReadOnly = true,
```

with:

```csharp
                // Stable, not LicenseGate.MachineCode (== MachineFingerprint.Current).
                // Current mixes in three WMI factors that fail transiently and flip the
                // code, silently invalidating a valid licence. VerifyEither accepts
                // either, so licences already issued against Current keep working.
                Text = MachineFingerprint.Stable, IsReadOnly = true,
```

- [ ] **Step 3: Copy the Stable code too**

Replace line 40:

```csharp
            copyBtn.Click += (s, e) => { try { Clipboard.SetText(LicenseGate.MachineCode); } catch { } };
```

with:

```csharp
            copyBtn.Click += (s, e) => { try { Clipboard.SetText(MachineFingerprint.Stable); } catch { } };
```

- [ ] **Step 4: Build the plugin**

Run: `dotnet build StingTools/StingTools.csproj -c Release`
Expected: `0 Error(s)`. This machine has Revit + the .NET SDK — do **not** use the "committed without build verification" caveat.

- [ ] **Step 5: Commit**

```bash
git add StingTools/UI/ActivationDialog.cs
git commit -m "fix(licensing): activation dialog shows the Stable machine code and links to /licences"
```

---

### Task 7: Open the PR

- [ ] **Step 1: Run everything once more**

```bash
cd marketing-site && npm test && npm run typecheck
```
Expected: 11 tests passing, no type errors. Paste the real output into the PR — not "should pass".

- [ ] **Step 2: Push and open the PR**

```bash
git push -u origin claude/licences-self-serve
gh pr create --base main --title "feat(licences): self-serve licence issuing (#673)" --body "..."
```

The PR body must state:
- what was verified (test output, plugin build) and what was **not** (the page itself is unverified against production — there is no UI harness);
- that **merging ships nothing**: `marketing-site` has no git-connected build (#651), so this is not live until someone runs `npm run deploy`;
- that the expiry guard mitigates #677 in the UI but does not fix it.

- [ ] **Step 3: Do not deploy.** That is the user's call.

---

## Self-review

**Spec coverage:** endpoint (Task 1) · page (Task 5) · plugin (Task 6) · all four spec tests (Tasks 1–4: seat numbers, isolation, revoked-excluded, auth) · expiry guard (Task 5, `onIssued`) · no re-download claim (Task 5, the note in the second card) · `_redirects` warning (Task 5 preamble) · token-in-memory (Task 5, bootstrap).

**Placeholders:** none — every code step carries complete code. The PR body in Task 7 is the one `"..."`, and its required content is spelled out beneath it.

**Type consistency:** `ListBody` in Task 1 matches the endpoint's return shape and is reused verbatim by Tasks 2–3. `list()` and `callGet()` are defined once in Task 1 and used in Tasks 2–4. The page reads `cap`, `inUse`, `licences[].{machineCode,expiresAt,revokedAt,lastSeenAt,lastSeenPluginVersion,lastSeenRevitVersion}` — all present in the endpoint's output. `issue.ts`'s response fields used by the page (`license`, `machineCode`, `expiresAt`) match its actual `return`.
