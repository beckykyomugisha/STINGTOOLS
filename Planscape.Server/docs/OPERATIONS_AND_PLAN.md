# Planscape — Operations & Planning Guide

**Date:** 2026-04-17
**Audience:** Project sponsor, BIM coordinator, dev+ops lead
**Companion to:** `DEPLOYMENT.md` (operational runbook) and `PLANSCAPE_GAPS.md` / `ONSITE_SHARING_GAPS.md` (gap inventory)

This document has two parts:

1. **Part A — External Pieces Guide.** The four things that can only be done outside this repo: Firebase service account, Apple Developer Program, Google Play Console, DNS + TLS. Plus a fifth: verifying the server build, which needs a `dotnet` toolchain.
2. **Part B — Costed Remaining-Gap Plan.** Tiered delivery plan for every open gap across the five dimensions (Integration, Flexibility, Information, Logic, Automation), with when, who, and cost.

All time and cost figures assume a single mid-weight full-stack developer at £600/day. Swap numbers for your actual rate.

---

## Part A — The Four (actually Five) External Pieces

### A.0 At a glance

| # | External piece | Lead time | One-off cost | Recurring cost | Blocks |
|---|----------------|-----------|--------------|----------------|--------|
| 1 | Firebase project + service account | 1 day | £0 | £0 (Spark) / ~$25/mo (Blaze) | FCM push to standalone iOS/Android builds |
| 2 | Apple Developer Program | 1–7 days enrolment | £0 | $99/year | TestFlight distribution, App Store |
| 3 | Google Play Console | 1–2 days | $25 one-time | £0 | Play Store distribution |
| 4 | DNS domain + Let's Encrypt | 1 hour | £10–15/year (domain) | £0 (LE is free) | HTTPS, ATS compliance on iOS, production CORS |
| 5 | Server build verification | 2 hours | £0 | £0 | Catching C# compile errors before runtime |

Total external cost to launch: **~£130 one-off + ~$100-400/year recurring** depending on usage.


### A.1 Firebase service account JSON (FCM push)

**What:** Credentials that let `FirebasePushService` send push notifications to standalone iOS / Android builds via FCM HTTP v1. Not needed for Expo Go — those tokens are Expo-shaped and `ExpoPushService` handles them.

**When:** Needed before the first TestFlight / Play internal build goes out to real users. Not a blocker for Expo Go demos.

**Lead time:** 1 working day.

**Cost:**
- Spark plan (free): ≤100 K FCM messages / day, ≤10 GB Firebase Storage — fine for pilot and up to ~50 active sites
- Blaze plan (pay-as-you-go): ~$25/month for a mid-size org; scales with usage

**Step by step:**

```bash
# 1. Create a Firebase project
#    https://console.firebase.google.com → "Add project" → name "planscape-prod"
#    Disable Google Analytics unless you actually want it (one less consent to handle)

# 2. Register the two apps
#    Project settings → "Add app"
#      iOS:     bundle id  com.planscape.app
#      Android: package    com.planscape.app
#    Download google-services.json (Android) and GoogleService-Info.plist (iOS)
#    Place under Planscape/google-services.json + Planscape/GoogleService-Info.plist
#    Also add EAS: `eas secret:create --scope project --name GOOGLE_SERVICES_JSON --type file --value ./google-services.json`

# 3. Generate a service account key for the server
#    Project settings → Service accounts → "Generate new private key"
#    Save the JSON as  secrets/firebase-service-account.json  (do NOT commit)

# 4. Wire into the server config (two options)
#    Option A — mount the file at /run/secrets/firebase.json and point config at it:
export Firebase__ProjectId=planscape-prod
export Firebase__ServiceAccountJson=$(cat secrets/firebase-service-account.json)

#    Option B — use Docker secrets / K8s secrets
docker secret create planscape_firebase secrets/firebase-service-account.json
#    then in docker-compose.prod.yml:
#       secrets: [planscape_firebase]
#       environment: Firebase__ServiceAccountJson_FILE=/run/secrets/planscape_firebase

# 5. Smoke test
curl -X POST https://api.planscape.example/api/notifications/test \
  -H "Authorization: Bearer $JWT"
# Expected: 202 Accepted, your device vibrates within ~5s
```

**Pitfalls:**
- The service-account JSON contains a private key — treat it like a password. Never commit, rotate every 90 days.
- FCM token rotation: once a user reinstalls the app, the old token in `DevicePushTokens` becomes stale. The cleanup loop in `FirebasePushService.RemoveInvalidTokensAsync` handles this — no manual intervention.
- If your domain is ATS-blocked (HTTP only), iOS silent-fails the push registration. Resolve A.4 first.

**Validation:**
- Open Firebase console → Cloud Messaging → "Reports" — you should see Send Success > 0 within 10 minutes of first real user login.
- Check `logs/planscape-*.log` for `"Expo push"` vs `"Firebase not configured"` warnings to confirm which provider is receiving traffic.


### A.2 Apple Developer Program

**What:** The paid membership that lets you sign iOS builds, distribute via TestFlight (up to 10 K external testers), and publish to the App Store. Without it you can only run on a physical iPhone plugged into Xcode for 7 days at a time.

**When:** Needed before the first TestFlight drop to site coordinators. **Enrolment can take up to 7 business days** if Apple asks for D-U-N-S or manual identity verification — start this first.

**Lead time:** 1–7 working days.

**Cost:** $99/year (Organization) or $99/year (Individual). Organization is strongly recommended — enables team roles and an Apple Team ID that your company owns.

**Step by step:**

```bash
# 1. Get a D-U-N-S number (only for Organization enrolments)
#    https://developer.apple.com/enroll/duns-lookup/
#    Free via Dun & Bradstreet; takes up to 5 business days

# 2. Enroll
#    https://developer.apple.com/programs/enroll/
#    Select Organization, pay $99 with a corporate card
#    You'll receive an Apple Team ID (e.g., ABCDE12345)

# 3. Create the App ID
#    https://developer.apple.com/account/resources/identifiers/list
#    + → App IDs → Explicit → com.planscape.app
#    Capabilities to enable now (so EAS picks them up):
#      - Push Notifications  (REQUIRED for FCM/APNs)
#      - Background Modes → Remote notifications  (optional, for silent push)
#      - Associated Domains (optional, for universal links later)

# 4. Create an APNs auth key (preferred over legacy certificates)
#    Keys → + → "Apple Push Notifications service (APNs)" → All apps
#    Download the .p8 file ONCE (cannot be re-downloaded)
#    Save as  secrets/AuthKey_<KeyID>.p8
#    Note the Key ID and Team ID

# 5. Tell Firebase about APNs (only if using FCM for iOS)
#    Firebase console → Project settings → Cloud Messaging → Apple app configuration
#    Upload the .p8, enter Key ID and Team ID
#    FCM now delivers to iOS via APNs automatically

# 6. Create an App Store Connect API key
#    https://appstoreconnect.apple.com/access/api
#    Generate key with "App Manager" or "Developer" role — enables `eas submit`
#    Download the .p8, note the Issuer ID + Key ID

# 7. Update Planscape/eas.json with the three identifiers
#    "submit": {
#      "production": {
#        "ios": {
#          "appleId": "you@yourco.com",
#          "ascAppId": "1234567890",
#          "appleTeamId": "ABCDE12345"
#        }
#      }
#    }

# 8. First TestFlight build
eas build --profile preview --platform ios
eas submit --platform ios --latest
```

**Pitfalls:**
- TestFlight builds show a beta watermark and time out 90 days after upload. Re-upload is required for long-running pilots.
- The `NSLocationWhenInUseUsageDescription` string in `app.json` is shown verbatim at first prompt — make sure it's clear and not lawyer-speak.
- Apple's App Review will reject apps that use camera/location without a clear user-facing reason. Our usage strings (site photo capture + geofence validation) pass easily.
- An Apple review typically takes 24–48 hours for the first submission, 12–24 hours for subsequent.

**Validation:**
- TestFlight → Internal Testing → add emails → install via TestFlight app on test device.
- Push token registration: look for `ExponentPushToken[...]` in `DevicePushTokens` within 30 s of first login.


### A.3 Google Play Console

**What:** The developer console that lets you distribute signed APK/AAB to closed testers, open testers, or the public. Cheaper and faster to start than Apple, but signing keys are managed differently.

**When:** Before first Android internal-track release.

**Lead time:** 1–2 working days (ID verification has become stricter).

**Cost:** $25 one-time fee. No annual renewal.

**Step by step:**

```bash
# 1. Create the developer account
#    https://play.google.com/console/signup
#    Pay $25, submit ID (passport/driving licence) — Google now requires verified identity
#    Pick "Organisation" not "Yourself" unless you want it tied to a personal email forever

# 2. Create the app shell
#    Console → "Create app" → Planscape → Free → All apps acceptable
#    Default language: English (UK)
#    Confirm declarations (no ads yet, complies with Play policies)

# 3. Configure App signing — use Play App Signing (recommended)
#    Console → Setup → App signing → "Use Play App Signing"
#    Generate a new key or upload one from EAS. EAS handles this automatically
#    when you first run `eas build --platform android --profile production`

# 4. Create a service account for eas submit
#    https://console.cloud.google.com/iam-admin/serviceaccounts  (link from Play Console)
#    + Create → planscape-play-submit → grant roles: "Service Account User"
#    Keys tab → Add key → JSON → download as  secrets/play-service-account.json
#    In Play Console: Users and permissions → Invite the service account email
#      → Grant app permission: "Release to testing tracks"

# 5. Wire into eas.json (already done)
#    "submit": { "production": { "android": {
#        "serviceAccountKeyPath": "./secrets/play-service-account.json",
#        "track": "internal"
#    } } }

# 6. First build + submit
eas build --profile preview --platform android    # APK for internal QA
eas build --profile production --platform android # AAB for Play
eas submit --platform android --latest

# 7. Promote to internal testers
#    Play Console → Testing → Internal testing → Manage testers
#    Create a tester email list, share the opt-in link
```

**Mandatory store listing content (Play will block release until complete):**

| Item | What to prepare |
|------|-----------------|
| Short description | ≤80 chars — "ISO 19650 BIM coordination on site" |
| Full description | 500–4000 chars — what Planscape does, who it's for |
| App icon | 512×512 PNG |
| Feature graphic | 1024×500 JPG/PNG |
| Screenshots | ≥2 for phone, ≥1 for tablet, 16:9 or 9:16 |
| Privacy policy URL | **Required** — host a simple page at `planscape.yourco.com/privacy` |
| Data safety form | Declare GPS, photos, camera, notifications, cloud storage |
| Content rating | Questionnaire — Planscape is 3+ |

**Pitfalls:**
- Since 2024, new personal accounts must have 12 closed testers for 14 days before production release. Use an Organisation account to skip this.
- The "Data safety" form is audit-grade — under-declaring GPS/photo access is grounds for takedown. Declare everything the app requests.
- First release takes ~48 hours for review; subsequent take 2–8 hours.

**Validation:**
- Internal testing link → install on an Android device → log in → push arrives within 30 s.


### A.4 DNS + Let's Encrypt (TLS)

**What:** A real domain with DNS pointing at the server, plus a trusted TLS cert. iOS ATS and Android Network Security Config both require HTTPS with a publicly-trusted cert for production builds — a self-signed cert won't load.

**When:** Before any TestFlight / Play build that talks to a non-localhost server.

**Lead time:** ~1 hour once you have a registrar account and a public-facing server.

**Cost:**
- Domain registration: £10–15/year (e.g., Namecheap, Cloudflare Registrar, 123-Reg)
- Let's Encrypt certs: free
- Cloudflare proxy (optional, recommended): free tier covers most site use

**Step by step:**

```bash
# 1. Register the domain (skip if you already have one)
#    planscape.yourco.com is the API host in eas.json — adjust to match
#    Tip: use a subdomain of your main corporate domain (api.planscape.yourco.com)
#    so you inherit the corporate DNS + mail setup

# 2. Point DNS at the server
#    Create an A record:
#       api.planscape.yourco.com  →  1.2.3.4  (your server's public IP)
#    TTL: 300 (5 min) while setting up, bump to 3600 once stable

# 3. Open ports on the server host
sudo ufw allow 80/tcp   # http-01 challenge + redirect
sudo ufw allow 443/tcp  # https

# 4. Install certbot (one-time, on the server host)
sudo apt-get update && sudo apt-get install -y certbot

# 5. Obtain the cert
#    Stop nginx first so certbot can bind port 80 for the standalone challenge
docker compose -f Planscape.Server/docker/docker-compose.yml \
               -f Planscape.Server/docker/docker-compose.prod.yml stop nginx

sudo certbot certonly --standalone \
  -d api.planscape.yourco.com \
  --email ops@planscape.yourco.com \
  --agree-tos --no-eff-email

# 6. Copy the certs into the mounted nginx volume
sudo cp /etc/letsencrypt/live/api.planscape.yourco.com/fullchain.pem \
        Planscape.Server/docker/nginx/certs/server.crt
sudo cp /etc/letsencrypt/live/api.planscape.yourco.com/privkey.pem \
        Planscape.Server/docker/nginx/certs/server.key
sudo chown $USER:$USER Planscape.Server/docker/nginx/certs/server.*
chmod 600 Planscape.Server/docker/nginx/certs/server.key

# 7. Bring nginx back up
docker compose -f Planscape.Server/docker/docker-compose.yml \
               -f Planscape.Server/docker/docker-compose.prod.yml up -d nginx

# 8. Verify
curl -I https://api.planscape.yourco.com/health/live
# Expected: HTTP/2 200 + valid cert chain ("issued by R3" / "Let's Encrypt")

# 9. Auto-renewal (certs last 90 days). Add a cron job:
echo "0 3 * * 0 certbot renew --quiet --deploy-hook 'docker compose -f /path/to/Planscape.Server/docker/docker-compose.yml -f /path/to/Planscape.Server/docker/docker-compose.prod.yml restart nginx'" | sudo crontab -
```

**Alternative: Cloudflare proxy (no server cert management at all):**

```
1. Register the domain via Cloudflare or transfer it in
2. Enable "Proxied" on the A record — Cloudflare now handles TLS termination
3. SSL mode: "Full (strict)" — Cloudflare checks the origin cert
4. Keep the self-signed cert on the server; Cloudflare accepts it in Full (strict)
5. DNS: api.planscape.yourco.com → your server IP, proxied (orange cloud)
```

Cloudflare adds DDoS protection, a CDN, and WAF rules for free — worth doing even on single-region setups.

**Pitfalls:**
- ATS on iOS 14+ requires TLS 1.2+ with perfect forward secrecy. Our `nginx.conf` already specifies `TLSv1.2 TLSv1.3` + `HIGH:!aNULL:!MD5`.
- Don't use an IP-based cert — phones refuse it.
- Let's Encrypt rate limits: 50 certs/week per registered domain. If you need staging + prod, use subdomains (`staging.planscape.yourco.com`, `api.planscape.yourco.com`) not separate registrations.

**Validation:**
- `openssl s_client -connect api.planscape.yourco.com:443 -servername api.planscape.yourco.com` — look for `Verify return code: 0 (ok)`.
- SSL Labs scan: `https://www.ssllabs.com/ssltest/analyze.html?d=api.planscape.yourco.com` — target A or A+.


### A.5 Server build verification

**What:** A dotnet 8 SDK environment that can `dotnet build` and `dotnet ef` against the server projects. This isn't "external" in the licensing sense but the current sandbox can't do it, so every C# change this session has been committed without a compile gate.

**When:** Before every merge to `main`.

**Lead time:** 30 minutes first time, minutes thereafter.

**Cost:** £0. Microsoft provides the SDK free.

**Step by step:**

```bash
# 1. Install the SDK (pick your host OS)
# Ubuntu / Debian:
sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0
# macOS:
brew install --cask dotnet-sdk
# Windows: https://dotnet.microsoft.com/download/dotnet/8.0

dotnet --version     # expect 8.0.x

# 2. Restore + build the server projects
cd Planscape.Server
dotnet restore src/Planscape.API/Planscape.API.csproj
dotnet build src/Planscape.API/Planscape.API.csproj -c Release

# 3. Run the test suite (if/when tests are added)
dotnet test

# 4. EF migrations — verify the model snapshot is in sync
dotnet tool install --global dotnet-ef
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet ef migrations list \
  --project src/Planscape.Infrastructure/Planscape.Infrastructure.csproj \
  --startup-project src/Planscape.API/Planscape.API.csproj

# 5. Check for pending model changes
dotnet ef migrations has-pending-model-changes \
  --project src/Planscape.Infrastructure/Planscape.Infrastructure.csproj \
  --startup-project src/Planscape.API/Planscape.API.csproj
# If this returns "pending changes", run:
#   dotnet ef migrations add <Name>

# 6. Apply migrations to a dev database (done automatically on API startup too)
dotnet ef database update \
  --project src/Planscape.Infrastructure/Planscape.Infrastructure.csproj \
  --startup-project src/Planscape.API/Planscape.API.csproj

# 7. Wire into CI  (already done — .github/workflows/planscape-server.yml)
#    GitHub Actions runs `dotnet build` on every push; merge gate is automatic.
```

**What to check right now with the code as-pushed:**

The following files were written without a compile gate in this session. Verify they compile:

| File | Why to check |
|------|--------------|
| `Planscape.Server/src/Planscape.Infrastructure/Services/ExpoPushService.cs` | New class — verify `using Planscape.Core.Interfaces` resolves `PushPayload` |
| `Planscape.Server/src/Planscape.Infrastructure/Services/FirebasePushService.cs` | Added `ExpoPushService` constructor dep + `SendOneAsync` dispatcher |
| `Planscape.Server/src/Planscape.Infrastructure/Storage/S3FileStorageService.cs` | New class — verify AWSSDK.S3 v3.7.412 API surface is stable |
| `Planscape.Server/src/Planscape.Infrastructure/Services/DatabaseBackupJob.cs` | Uses `Process.Start` for `pg_dump` — verify `using System.Diagnostics` |
| `Planscape.Server/src/Planscape.Core/Entities/BimIssue.cs` | Added 8 new properties + 2 nav refs (`AssigneeUser`, `CreatedByUser`) |
| `Planscape.Server/src/Planscape.API/Controllers/IssuesController.cs` | Expanded `CreateIssue` with assignee-user resolution + validation |
| `Planscape.Server/src/Planscape.Infrastructure/Data/Migrations/20250417000000_AddIssueGpsAndAssigneeFk.cs` | New hand-written migration — verify column names match model snapshot |
| `StingTools/BIMManager/MobileIssueBridge.cs` | Uses `Planscape.PluginSync` project ref — verify resolves |
| `StingTools/UI/StingDockPanel.xaml.cs` | Added `LastInstance` + `SyncIndicator_Click` — verify signals route correctly |

**Common errors you might see on first build:**

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `CS0246: PushPayload could not be found` | Namespace missing in `ExpoPushService.cs` | Add `using Planscape.Core.Interfaces;` |
| `CS7069: Reference to type 'AppUser' claims it is defined in 'Planscape.Core'` | Assembly not loaded for snapshot | Run `dotnet restore` |
| `Model snapshot has drifted` | New migration fields don't match snapshot | Regenerate: `dotnet ef migrations add IssueGpsAndAssigneeFkFix` then reconcile |
| `CS0103: The name 'StingDockPanel' does not exist` | Partial class across files | Ensure `_instance` field / `LastInstance` property both sit in the same partial |
| Duplicate `TypeTokenInherit` (StingTools side) | Merge leftover — check git | `grep -n "TypeTokenInherit" StingTools/**/*.cs` and delete duplicates |

**Validation:**
- CI goes green on `planscape-server.yml` for the current branch
- Runtime: API container starts, `/health/live` returns 200, migration log shows `20250417_AddIssueGpsAndAssigneeFk` applied


---

## Part B — Costed Remaining-Gap Plan

This part is a *plan*, not an inventory. For the raw gap listing see `PLANSCAPE_GAPS.md` (original 40 items), `ONSITE_SHARING_GAPS.md` (17 focused items), and the 80 items produced in the April 17 five-dimension review (§B.1).

### B.1 Dimension scorecard (as of 2026-04-17)

Five-dimension review gave each area a pass/fail across 80 fresh findings:

| Dimension | CRITICAL | HIGH | MEDIUM | LOW | Subtotal | Health |
|-----------|----------|------|--------|-----|----------|--------|
| **Integration** (mobile↔server↔plugin, auth, SignalR, COBie/BCF) | 1 | 3 | 7 | 4 | 15 | AMBER |
| **Flexibility** (tenant/project config, hot-reload, extensibility) | 0 | 8 | 5 | 2 | 15 | RED |
| **Information** (data surfacing, audit trails, timelines) | 0 | 3 | 12 | 0 | 15 | AMBER |
| **Logic** (races, security, edge cases, auth) | 4 | 6 | 10 | 0 | 20 | RED |
| **Automation** (triage, OCR, geocoding, workflow) | 0 | 6 | 7 | 2 | 15 | AMBER |
| **Total fresh** | **5** | **26** | **41** | **8** | **80** | — |

Plus still-open items from earlier inventories:
- **From SRV-NN:** 7 NOT DONE (S3 was just closed, so now 6): SRV-08 batch ops, SRV-09 conflict UI, SRV-11 mobile SignalR auth path partial, SRV-16 WebP/AVIF, SRV-17 bandwidth negotiation
- **From INT-NN:** INT-02 dual-sync duplication still standing, INT-05 legacy pending fields cleanup, INT-06 token refresh auto-retry

Total open gaps across all inventories: **~90**.

### B.2 Delivery principles

Before the table — four rules that drive the tiering.

1. **Security before features.** Anything CRITICAL in Logic (tenant isolation, SignalR group join, race conditions) gets priority over any new feature.
2. **Site-journey first.** Within each tier, gaps that directly block "site user shares a construction update" win over internal refactors.
3. **Infrastructure before polish.** Observability and CI/CD before UX enhancements — you can't iterate on what you can't measure.
4. **One dev week = 4 productive days.** Add 25% for meetings, reviews, merge conflicts, follow-through. The numbers below already include that buffer.


### B.3 Tier 1 — Critical path (Weeks 1–3, ~60 dev-hours)

Everything here is a CRITICAL or directly on the site-sharing hot path. Do not ship TestFlight / Play without closing this tier.

| # | ID(s) | What | Effort | Owner | Cost |
|---|-------|------|--------|-------|------|
| 1 | NEW-LOGIC-15 | **SignalR JoinProject/JoinTenant authorisation** — verify user is a project member before adding to group. Eavesdropping fix. | 0.5 d | Backend | £300 |
| 2 | NEW-LOGIC-01/02 | **IssueCode race / injection** — add `UNIQUE(ProjectId, IssueCode)` DB constraint + retry loop on conflict; sanitise Type regex `^[A-Z]{2,6}$` | 0.5 d | Backend | £300 |
| 3 | NEW-LOGIC-20 | **Tenant isolation bypass audit** — add integration test that a user from tenant A gets 403 on every endpoint for tenant B; enforce `GetTenantId()` call in a base controller | 1 d | Backend | £600 |
| 4 | NEW-LOGIC-06/07 | **MIME sniffing + path traversal** — add magic-byte check on attachment upload, `Path.GetFileName()` sanitisation on document upload | 0.5 d | Backend | £300 |
| 5 | NEW-LOGIC-09/10 | **Hangfire job concurrency + SLA dedup** — `[DisableConcurrentExecution]` on all 4 recurring jobs; SLA job tracks last-pushed timestamp per issue-assignee pair | 0.5 d | Backend | £300 |
| 6 | NEW-INT-05 | **SignalR events actually emitted** — wire `IssueCreated` / `IssueUpdated` / `ComplianceChanged` Clients.Group().SendAsync() calls from the three controllers; currently mobile subscribes to silence | 1 d | Backend | £600 |
| 7 | NEW-INT-07 | **HEIC image support** — add `Magick.NET-Q8` or upgrade ImageSharp to accept HEIC; mobile fallback compresses to JPEG first | 1 d | Backend + Mobile | £600 |
| 8 | NEW-INT-04 / SRV-11 | **Mobile refresh-token flow** — on 401 from non-login endpoints, call `/api/auth/refresh` with stored refresh token; on 401 from refresh, push user to login screen | 0.5 d | Mobile | £300 |
| 9 | NEW-AUTO-09 / 11 / 15 | **Auto-capture wiring** — GPS auto-capture on issue create modal open, `useFocusEffect` dashboard refresh, notification tap auto-opens issue detail via `issueId` param | 1 d | Mobile | £600 |
| 10 | NEW-INT-06 | **Mobile BimIssue type sync** — add `assigneeUserId` / `latitude` / `longitude` / `locationAccuracy` / `deviceId` / `source` / `dueDate` / `isOverdue` / `daysOpen` / `attachmentCount` to `src/types/api.ts` and render them | 1 d | Mobile | £600 |
| 11 | External | **Server build verification** (§A.5) — install SDK, `dotnet build`, fix any fallout from this session's C# writes | 0.5 d | Backend | £300 |
| 12 | External | **TLS + DNS** (§A.4) — Let's Encrypt cert, nginx in front, DNS pointing at host | 0.5 d | DevOps | £300 |
| 13 | NEW-INFO-01/02 | **Attachment gallery + overdue badge** — issue detail shows thumbnails in a ScrollView (calls existing `/attachments` endpoint); list card shows "OVERDUE" + days open | 1 d | Mobile | £600 |
| 14 | NEW-LOGIC-14 | **FCM 5xx retry + token pruning** — exponential-backoff retry for transient FCM failures; prune tokens where `LastUsedAt < UtcNow - 90d` in the Stale cleanup job | 0.5 d | Backend | £300 |

**Tier 1 subtotal:** 10 dev-days · £6,000 · 3 calendar weeks (one dev).

### B.4 Tier 2 — Production quality (Weeks 4–7, ~90 dev-hours)

HIGH severity + infrastructure that you need before opening to >1 site.

| # | ID(s) | What | Effort | Cost |
|---|-------|------|--------|------|
| 15 | NEW-FLEX-11 / NEW-FLEX-10 | **Tenant + project switcher** — settings drawer with tenant list, project picker in the tab header, preserves per-tenant JWT stash | 2 d | £1,200 |
| 16 | NEW-FLEX-12 | **Notification preferences API + UI** — per-category opt-out, quiet hours; `UserNotificationPreferences` entity + `/api/me/notifications/preferences` | 2 d | £1,200 |
| 17 | NEW-FLEX-02 / 08 | **Issue types + priorities from server** — `/api/projects/{id}/settings/issue-types` endpoint; mobile fetches on project open, caches in Zustand | 1 d | £600 |
| 18 | NEW-FLEX-04 | **CDE state machine per project** — configurable state+transition matrix stored per project; `DocumentsController` reads instead of hard-coded dictionary | 2 d | £1,200 |
| 19 | NEW-FLEX-05 / 01 | **Configurable limits** — attachment/document size, SLA hours per priority, compliance snapshot retention; all loaded from `ProjectSettings` JSON column | 1 d | £600 |
| 20 | NEW-INFO-06/07 | **Issue activity timeline** — `AuditLog` query by `Entity=Issue, EntityId={id}` + before/after diffs in `DetailsJson`; `/api/issues/{id}/activity` endpoint + mobile timeline tab | 2 d | £1,200 |
| 21 | NEW-INFO-04/05 | **Dashboard payload enrichment** — `RecentIssues` array in `/dashboard`, 30-day compliance trend array; mobile home screen renders sparkline | 1 d | £600 |
| 22 | NEW-INFO-14 | **Geofence violation surfacing** — mobile shows "Outside boundary" pin + inline warning when create returns 403; admin can override | 0.5 d | £300 |
| 23 | NEW-AUTO-01 | **Auto-triage rule engine** — `ProjectTriageRules` entity (`discipline→userId`, `element-category→userId`); CreateIssue pre-fills assignee if rule matches | 3 d | £1,800 |
| 24 | NEW-AUTO-02 | **Auto-close stale issues** — Hangfire job archives OPEN issues with no activity > configurable N days (default 60); sends digest push | 1 d | £600 |
| 25 | NEW-AUTO-03 | **SLA escalation with reassignment** — on breach, promote priority + reassign to lead/manager (configurable chain) | 1 d | £600 |
| 26 | NEW-INT-01 | **Mobile coverage for Transmittals / Meetings / Workflows / Warnings** — 4 new endpoint modules + 4 mobile tabs (or at least read-only lists) | 3 d | £1,800 |
| 27 | NEW-INT-15 | **Document approval workflow mobile** — approve / reject buttons in document detail, `/documents/{id}/approvals` endpoints wired | 1 d | £600 |
| 28 | Infrastructure | **Monitoring stack** — Serilog.Sinks.Seq or Elastic, health alert on `/health/ready` flapping, Slack/email on push failure > 5% | 1 d | £600 |
| 29 | NEW-AUTO-14 | **Mobile thumbnail requests** — `?thumb=300` query param honoured by `/issues/{id}/attachments/{aid}/thumbnail`, mobile list uses 150 px thumbs | 0.5 d | £300 |
| 30 | NEW-LOGIC-16/17 | **Queue drain stability** — single-concurrent drain, jitter, orphan-issue cleanup job | 1 d | £600 |

**Tier 2 subtotal:** 23 dev-days · £13,800 · 4 calendar weeks.


### B.5 Tier 3 — Scale & differentiation (Weeks 8–12, ~80 dev-hours)

MEDIUM severity + everything that unlocks the "premium" experience and multi-project / multi-tenant at scale.

| # | ID(s) | What | Effort | Cost |
|---|-------|------|--------|------|
| 31 | NEW-INT-10 | **External platform connectors** — ACC OAuth flow + document/issue sync; Procore + Aconex + Trimble as phase 3.5 when there's a paying customer | 5 d (ACC only) | £3,000 |
| 32 | NEW-INT-13 | **BCF 2.1 bidirectional** — `/api/projects/{id}/bcf/export` + `/bcf/import`, round-trip preserves `BcfGuid` on `BimIssue` | 2 d | £1,200 |
| 33 | NEW-INT-11 | **COBie export with attachment metadata** — new `CobieController`, extend Attachment sheet with thumbnail URLs and EXIF GPS | 1.5 d | £900 |
| 34 | NEW-INT-02 | **Planscape MIM mobile surfacing** — assets tab, condition logging, maintenance schedules | 2 d | £1,200 |
| 35 | NEW-FLEX-13 | **Custom fields on issues** — JSON column + schema per project, mobile renders dynamic form sections | 3 d | £1,800 |
| 36 | NEW-FLEX-03 / 07 | **Tenant branding + email templates** — upload logo, set accent colour, editable email templates with Handlebars | 2 d | £1,200 |
| 37 | NEW-FLEX-15 | **i18n** — i18next + `expo-localization`, English + one more language (likely German or French for an EU launch) | 2 d | £1,200 |
| 38 | NEW-AUTO-06 | **GPS → room geocoding** — project-level spatial index of room polygons; issue create auto-fills `LOC` tag from current GPS | 2 d | £1,200 |
| 39 | NEW-AUTO-07 | **NLP auto-link to Revit elements** — "column on grid B-3" → resolve to ElementId via ISO tag regex + fuzzy match | 3 d | £1,800 |
| 40 | NEW-AUTO-13 | **OCR on attached photos** — Tesseract or AWS Textract; populate issue title from visible defect tags | 3 d | £1,800 |
| 41 | NEW-INFO-08/13 | **Last-sync + saved filters** — show "synced 3 min ago" on offline screen, persist named views per user | 1 d | £600 |
| 42 | NEW-INFO-15 | **Resolution-time KPIs** — per-user and per-discipline resolution SLA compliance dashboard | 2 d | £1,200 |
| 43 | SRV-08 | **Batch operations endpoint** — `POST /issues/batch` for offline-queued replay of 50+ actions in one round-trip | 1 d | £600 |
| 44 | SRV-09 | **Conflict resolution UI contract** — `SyncConflicts` table surfaces via `/api/sync/conflicts`, mobile shows 3-way merge screen | 2 d | £1,200 |
| 45 | Load test + SRE | **Weekly synthetic load tests** — k6 in CI, fail deploys if p95 regresses > 20% | 1 d | £600 |

**Tier 3 subtotal:** 32.5 dev-days · £19,500 · 5 calendar weeks.

### B.6 Tier 4 — Polish & low-frequency items (Weeks 13+, ~30 dev-hours)

LOW severity + "nice to have" — do only if you have capacity.

| # | ID(s) | What | Effort | Cost |
|---|-------|------|--------|------|
| 46 | NEW-FLEX-14 | Dynamic text scaling (a11y) | 0.5 d | £300 |
| 47 | NEW-INFO-03 | Avatar / initial colour-coded assignee | 0.5 d | £300 |
| 48 | NEW-INFO-11 | Standard `deepLink` field on every push payload | 0.5 d | £300 |
| 49 | NEW-INFO-12 | Mobile filter by `source` (mobile / plugin / web) | 0.5 d | £300 |
| 50 | NEW-AUTO-10 | Mobile dead-letter queue + manual review UI for stuck offline actions | 1 d | £600 |
| 51 | NEW-AUTO-12 | Assignee auto-suggest by scanned element discipline | 0.5 d | £300 |
| 52 | SRV-16 | WebP / AVIF image acceptance (Android sends AVIF by default from Android 14) | 0.5 d | £300 |
| 53 | SRV-17 | Field selection `?fields=id,title,priority` for large list payloads | 1 d | £600 |
| 54 | INT-02 | Retire `PlanscapeServerClient` or fold it into `SyncScheduler.SyncNow` | 1 d | £600 |
| 55 | NEW-AUTO-04 | BCF export pipeline on revision creation (only after BCF I/O lands — see Tier 3) | 1 d | £600 |
| 56 | NEW-AUTO-05 | Compliance-snapshot invalidation on issue status change (SignalR-driven) | 0.5 d | £300 |

**Tier 4 subtotal:** 8 dev-days · £4,800 · 2 calendar weeks.


### B.7 Cost summary

All figures one dev @ £600/day, 4 productive days per calendar week.

| Tier | Dev-days | Labour cost | Calendar | Key unlocks |
|------|----------|-------------|----------|-------------|
| External prep (§A) | 2 | £1,200 | Parallel w/ Tier 1 | TLS, store access, compile gate, Firebase |
| Tier 1 — Critical path | 10 | **£6,000** | Weeks 1–3 | Security closed, mobile journey complete, can ship internal alpha |
| Tier 2 — Production quality | 23 | **£13,800** | Weeks 4–7 | Multi-project / multi-tenant viable, TestFlight → internal Play track |
| Tier 3 — Scale + differentiation | 32.5 | **£19,500** | Weeks 8–12 | Public App Store release, paying-customer features |
| Tier 4 — Polish | 8 | £4,800 | Weeks 13+ | Nice-to-have, fit around feature work |
| **Total dev labour** | **75.5** | **£45,300** | **~15 weeks** | — |

External / recurring costs on top of dev labour:

| Line item | Year 1 | Year 2+ |
|-----------|--------|---------|
| Apple Developer Program | $99 (~£80) | $99/yr |
| Google Play Console | $25 (~£20) one-off | £0 |
| Domain (planscape.yourco.com) | £12 | £12/yr |
| Firebase Blaze (low-tier site use) | $0–300 | ~$300/yr |
| Server host (1× 4 GB VM + 50 GB SSD) | £360 (£30/mo) | £360/yr |
| Managed Postgres (optional — RDS t4g.small) | £420 (£35/mo) | £420/yr |
| Cloudflare / CDN (optional) | £0 (free tier) | £0 |
| Sentry / log aggregation (optional) | £240 (£20/mo) | £240/yr |
| **External total** | **~£1,132–1,432** | **~£1,112–1,412/yr** |

**All-in year 1:** £45,300 labour + ~£1,200 external = **~£46,500** to go from today's state to a store-ready multi-tenant product.

Two-dev parallelisation (one backend, one mobile) compresses calendar time from ~15 weeks to ~8–9 weeks with ~15% coordination overhead — roughly £52,000 year 1 for a faster market entry.

### B.8 Suggested 12-week roadmap (single dev, serialised)

```
Week 1   ┌───────────────── External prep (§A.1, A.4, A.5) ──────────────────┐
         │  Firebase project, domain + Let's Encrypt, dotnet build           │
         ├─ Tier 1: SignalR auth (LOGIC-15), IssueCode race (LOGIC-01/02),   │
         │  tenant isolation audit (LOGIC-20), MIME + path traversal fixes   │
Week 2   ├─ Tier 1: Hangfire concurrency (LOGIC-09/10), SignalR emit         │
         │  (INT-05), HEIC support (INT-07), mobile refresh-token (INT-04)   │
Week 3   ├─ Tier 1: Auto-capture GPS/focus/tap (AUTO-09/11/15),              │
         │  mobile BimIssue type sync (INT-06), attachment gallery (INFO-01) │
         │  → First internal alpha for site coordinator smoke test           │
Week 4   ├─ Tier 2: Tenant + project switcher (FLEX-10/11), notification     │
         │  preferences (FLEX-12), issue types from server (FLEX-02/08)      │
Week 5   ├─ Tier 2: CDE state machine per project (FLEX-04), configurable    │
         │  limits (FLEX-05/01), issue activity timeline (INFO-06/07)        │
Week 6   ├─ Tier 2: Dashboard enrichment (INFO-04/05), geofence violation    │
         │  surfacing (INFO-14), auto-triage rules (AUTO-01)                 │
Week 7   ├─ Tier 2: Auto-close stale (AUTO-02) + SLA reassignment (AUTO-03), │
         │  mobile coverage for TX / Meetings / Workflows (INT-01),          │
         │  document approval mobile (INT-15), monitoring (NEW)              │
         │  → Apple D-U-N-S + Play Console enrolment (§A.2, A.3)             │
Week 8   ├─ Tier 3: ACC OAuth + document/issue sync (INT-10), BCF I/O        │
         │  (INT-13)                                                         │
Week 9   ├─ Tier 3: COBie w/ attachments (INT-11), MIM mobile (INT-02),      │
         │  custom fields (FLEX-13)                                          │
Week 10  ├─ Tier 3: Tenant branding + email templates (FLEX-03/07), i18n     │
         │  (FLEX-15), GPS→room geocoding (AUTO-06)                          │
Week 11  ├─ Tier 3: NLP element auto-link (AUTO-07), OCR (AUTO-13),          │
         │  KPIs + saved filters (INFO-08/13/15)                             │
Week 12  ├─ Tier 3: Batch ops + conflict resolution UI (SRV-08, SRV-09),     │
         │  load-test CI gate                                                │
         │  → TestFlight + Play internal release                             │
Week 13+ └───── Tier 4 polish as capacity allows ──────────────────────────┘
```

Decision points:
- **End of week 3** — alpha gate. Can a coordinator raise a photo+GPS issue and have the assignee receive a push within 30 s? Yes → continue; No → extend Tier 1.
- **End of week 7** — paid-pilot gate. Can a second tenant onboard without code changes? Yes → Tier 3; No → extend Tier 2.
- **End of week 12** — public release gate. SSL Labs A+, k6 p95 < 1.5 s at 50 concurrent, zero open CRITICAL or HIGH security items? Yes → store submission; No → extend.

### B.9 What to skip if budget is half

If funding only covers **8 weeks** (~£20,000 labour):

- **KEEP all of Tier 1** (week 1–3, non-negotiable)
- **KEEP from Tier 2:** items 15, 16, 17, 19, 20, 21, 26, 28 (tenant/project switcher, preferences, issue types, limits, timeline, dashboard, mobile coverage, monitoring)
- **DROP from Tier 2:** items 18 (CDE per project), 23 (triage), 24 (auto-close), 25 (SLA reassign), 27 (approval mobile), 29, 30
- **DROP all of Tier 3 & 4** except BCF I/O (INT-13) if a customer needs it

This gets you to "works for one tenant, one paying customer" without sanding every edge.


### B.10 Risk register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Apple / Play review rejects privacy policy or usage strings | Medium | 1-week delay | Draft policy + strings before week 7; use Apple's pre-submission review on enterprise accounts |
| Let's Encrypt rate limits hit (staging + prod both under same domain) | Low | Lose TLS for 1 week | Use subdomains + CF proxy as fallback |
| FCM changes service-account JSON format | Very Low | 1-day rewrite | Keep `FirebasePushService` + `ExpoPushService` separated; swap is localised |
| Site has no cell signal, only 3G/satellite | High | Mobile UX degrades | Offline queue already handles this; add "queued for sync" badge (Tier 1 item 13 covers); aggressive image compression |
| Third-party platform API changes (ACC, Procore) | Medium | Tier 3 item slips | Build connector interface now; mock the provider; retire individual impls on breakage |
| Server can't be built without dotnet SDK locally | Certain | Cannot compile-gate commits | §A.5 — set up SDK + CI gate as first-day task |
| Team bus factor = 1 | Medium | Total stop | Document as you go (this doc); pair-review every PR; fold this doc into the onboarding pack |

### B.11 What changes as you grow

| Users | Change needed | Cost delta |
|-------|---------------|------------|
| 1–20 (pilot) | Status quo | £0 |
| 20–100 | Move Postgres to managed (RDS / Azure DB); 8 GB API VM | +£60/mo |
| 100–500 | Multi-region CDN for attachments; read replica; scheduled scaling | +£300/mo |
| 500+ | K8s, separate worker nodes for Hangfire, CloudFront signed URLs | +£1,000/mo + 1 SRE role (£110 K/yr) |

No architectural rewrite needed at any boundary — the current schema, storage abstraction, and SignalR backplane all scale to 500+ users without code changes beyond config. The first architectural decision (horizontal scale-out beyond one API node) is already unblocked by having Redis as a SignalR backplane (verify `AddStackExchangeRedis` on `AddSignalR`).

---

## Appendix 1 — Minimum viable release checklist

Tear off and tick as you go.

```
Week -1 (external prep, parallel):
  [ ] Apple Developer enrolment submitted (up to 7 days)
  [ ] Google Play Console account paid + verified ($25)
  [ ] Domain registered (api.planscape.yourco.com)
  [ ] Firebase project + service-account JSON downloaded
  [ ] dotnet SDK 8 installed on CI + dev workstation
  [ ] DNS A record pointing at server IP
  [ ] Let's Encrypt cert installed, HTTPS verified

Week 1–3 (Tier 1):
  [ ] SignalR membership check in Hub
  [ ] UNIQUE(ProjectId, IssueCode) constraint + retry
  [ ] Tenant isolation integration tests green
  [ ] MIME sniffing + path traversal tests green
  [ ] DisableConcurrentExecution on all 4 Hangfire jobs
  [ ] SLA escalation deduplication
  [ ] IssueCreated / IssueUpdated / ComplianceChanged SignalR events emitted
  [ ] HEIC photo upload round-trip works
  [ ] Mobile 401 → /api/auth/refresh → login fallback
  [ ] GPS / focus / notification tap auto-wiring
  [ ] Mobile BimIssue type includes all new fields
  [ ] Server build verification clean
  [ ] TLS A grade on SSL Labs
  [ ] Attachment gallery + overdue badge on mobile
  [ ] FCM 5xx retry + token pruning

Week 4–7 (Tier 2):
  [ ] Tenant + project switcher on mobile
  [ ] Notification preferences API + mobile screen
  [ ] Issue types/priorities from server
  [ ] CDE state machine per project
  [ ] Configurable limits (size, SLA hours, retention)
  [ ] Issue activity timeline endpoint + mobile tab
  [ ] Dashboard enrichment (recent issues + trend sparkline)
  [ ] Geofence violation UI
  [ ] Auto-triage rule engine
  [ ] Auto-close stale + SLA reassignment
  [ ] Mobile coverage for TX / Meetings / Workflows / Warnings
  [ ] Document approval mobile
  [ ] Monitoring stack alerting on Slack/email
  [ ] Thumbnail size requests on list views
  [ ] Queue drain stability + orphan cleanup

Store submission (end of week 7 or 12):
  [ ] Privacy policy live at a public URL
  [ ] App Store Connect: app record created, screenshots, description
  [ ] Play Console: data safety form, content rating, internal-test track
  [ ] TestFlight internal build smoke-tested on iPhone + iPad
  [ ] Play internal build smoke-tested on Android phone + tablet
  [ ] First external coordinator invited

Post-launch (week 13+):
  [ ] Weekly k6 regression test
  [ ] Monthly cert renewal verified
  [ ] Quarterly Firebase + AWS cost review
  [ ] Biannual security audit (OWASP mobile checklist)
```

## Appendix 2 — Related docs in this repo

| Doc | Purpose |
|-----|---------|
| `Planscape.Server/DEPLOYMENT.md` | Runbook for getting the stack up locally + production |
| `Planscape.Server/docs/PLANSCAPE_GAPS.md` | Original 40-gap inventory (April 10) |
| `Planscape.Server/docs/ONSITE_SHARING_GAPS.md` | 17-item focused on-site sharing analysis |
| `Planscape.Server/src/Planscape.API/appsettings.Production.template.json` | Annotated production config |
| `Planscape.Server/docker/docker-compose.prod.yml` | TLS + MinIO + backups overlay |
| `Planscape/eas.json` | Three mobile build profiles |
| `.github/workflows/planscape-*.yml` | CI pipelines |
| This doc | External pieces + costed plan |

— END —
