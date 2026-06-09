# Firebase push notifications (FCM + APNs) — plug-and-play setup

This guide turns on **mobile push** so meeting invitations (and every other
Planscape push) land on phones. Until you do this, invites still work — they
deliver **in-app (SignalR) + email** and the server logs
`[meeting-invite] push skipped (no FCM); notified in-app/email`. The moment you
drop in a Firebase key and restart the API, push starts flowing. No code
changes required.

Follow the steps in order. Each one is a checklist a non-expert can complete.

---

## What you'll end up with

- **Android** → Firebase Cloud Messaging (FCM)
- **iOS** → Apple Push Notification service (APNs), routed *through* Firebase
- **Server** → one base64 env var (`PUSH_FIREBASE_SERVICE_ACCOUNT_JSON`)
- **App** → registers each device's push token to the server automatically on
  sign-in (already wired — nothing to build)

---

## Step 1 — Create the Firebase project + service-account key

1. Go to the **[Firebase console](https://console.firebase.google.com/)** → **Add project** → give it a name (e.g. `planscape`) → create. (Google Analytics is optional — skip it if unsure.)
2. In the project, open **⚙ Project settings** (gear, top-left) → **Service accounts** tab.
3. Click **Generate new private key** → **Generate key**. A `*.json` file downloads (this is the *service-account key* — it contains `project_id`, `client_email`, and a `private_key`).
4. **Keep this file secret.** It grants send-as-your-project rights. Never commit it, paste it in chat, or screenshot it.

> Note the **Project ID** shown on the Settings → General page (e.g. `planscape-1a2b3`). The server auto-reads it from the JSON, so you don't normally need to type it.

---

## Step 2 — Put the key on the server (`.env`)

The server reads the service-account JSON from the env var
**`Push__Firebase__ServiceAccountJson`**. It accepts the JSON **raw** *or*
**base64-encoded**. **Use base64** — it's a single line with no quoting/newline
headaches (the `private_key` field contains embedded `\n`).

### 2a. Base64-encode the key file

| OS | Command |
|---|---|
| Linux | `base64 -w0 service-account.json` |
| macOS | `base64 -i service-account.json` |
| Windows PowerShell | `[Convert]::ToBase64String([IO.File]::ReadAllBytes("service-account.json"))` |

Copy the **single-line** output.

### 2b. Set it in the Docker env file

Edit `Planscape.Server/docker/.env` (this file is **gitignored** — never
committed) and set:

```dotenv
PUSH_FIREBASE_SERVICE_ACCOUNT_JSON=<paste the base64 string here>
# Optional — only to override; otherwise auto-derived from the key's project_id:
# PUSH_FIREBASE_PROJECT_ID=planscape-1a2b3
```

docker-compose maps these into the API container as
`Push__Firebase__ServiceAccountJson` and `Push__Firebase__ProjectId`
(see `docker/docker-compose.yml`). The `.env.template` documents them.

> Raw JSON also works (`PUSH_FIREBASE_SERVICE_ACCOUNT_JSON={"type":"service_account",...}`)
> but you must keep it on one line and the inner quotes intact — base64 avoids
> all of that. **The server accepts either.**

### 2c. Restart the API and verify

```bash
cd Planscape.Server/docker
docker compose up -d --force-recreate api
```

Verify FCM is now recognised — send an invite (Step 6) and check the response
field **`pushConfigured: true`**, or hit the test endpoint in Step 6. When the
key is missing/blank you'll instead see `pushConfigured: false` and the
`[meeting-invite] push skipped (no FCM)` log line.

---

## Step 3 — Android (FCM)

The app already ships the `expo-notifications` plugin (`app.config.js`). For a
**standalone build** that talks directly to FCM you also need the
`google-services.json` from your Firebase project.

1. Firebase console → **Project settings → General → Your apps → Add app → Android**.
   - **Android package name** must be **`com.planscape.app`** (matches `app.config.js` → `android.package`).
   - Register the app → **Download `google-services.json`**.
2. Put `google-services.json` in the repo root of the Expo app (`Planscape/google-services.json`) — or upload it as an **EAS file secret** for cloud builds (`eas secret:create --scope project --name GOOGLE_SERVICES_JSON --type file --value ./google-services.json`).
3. Point the Expo config at it. In `Planscape/app.config.js`, add to the `android` block:
   ```js
   android: {
     // …existing…
     googleServicesFile: process.env.GOOGLE_SERVICES_JSON || './google-services.json',
   },
   ```
4. The **FCM Sender ID** = the **Project number** on Firebase console → Project settings → General (alongside the Project ID). Expo + `google-services.json` wire this for you; you don't enter it by hand, but that's the number FCM uses to identify your project.
5. Rebuild the dev/standalone client: `npx expo run:android` (local) or `eas build -p android`.

> Expo Go and EAS **dev** builds can use Expo's push relay (`ExponentPushToken[…]`)
> without `google-services.json` — handy for testing. Production standalone
> builds need `google-services.json` for native FCM. The server's
> `FirebasePushService` auto-detects the token shape and routes via Expo or
> native FCM accordingly, so both work side-by-side.

---

## Step 4 — iOS (APNs)

iOS push goes through APNs, which Firebase relays for you.

1. In the **[Apple Developer](https://developer.apple.com/account/resources/authkeys/list)** portal → **Keys** → **+** → enable **Apple Push Notifications service (APNs)** → register → **download the `.p8` key**. Note the **Key ID** and your **Team ID**.
2. Firebase console → **Project settings → Cloud Messaging → Apple app configuration** → **APNs Authentication Key** → **Upload** the `.p8`, with its **Key ID** and **Team ID**.
3. Firebase console → **General → Add app → iOS**:
   - **Bundle ID** must be **`com.planscape.app`** (matches `app.config.js` → `ios.bundleIdentifier`).
   - Download **`GoogleService-Info.plist`**.
4. Point the Expo config at it. In `Planscape/app.config.js`, add to the `ios` block:
   ```js
   ios: {
     // …existing…
     googleServicesFile: process.env.GOOGLE_SERVICES_PLIST || './GoogleService-Info.plist',
   },
   ```
5. Rebuild: `npx expo run:ios` (local, needs a Mac) or `eas build -p ios`. Push on a **physical device** only (the iOS simulator can't receive push).

---

## Step 5 — Device-token registration (already wired)

You don't need to write anything — this is how it works:

- On sign-in the app calls `notificationService.register()`
  (`Planscape/src/services/notificationService.ts`): it asks for the OS push
  permission, gets a token via `Notifications.getExpoPushTokenAsync()`
  (Expo relay token), and **POSTs it to `/api/notifications/subscribe`** with
  `{ token, platform, deviceId, appVersion, model }`. The server stores it in
  the **`DevicePushToken`** table, scoped to that user.
- For a production standalone build that wants a **native FCM device token**
  instead of the Expo relay token, swap that one call to
  `Notifications.getDevicePushTokenAsync()` — the server accepts both (it sniffs
  the token shape: `ExponentPushToken[…]` → Expo relay, anything else → FCM/APNs
  direct).
- When the server sends a meeting invite it looks up every `DevicePushToken`
  for each invited user and fans the push out to all their devices.

---

## Step 6 — Test it end-to-end

### A. Quick server self-test (no meeting needed)

`POST /api/notifications/test` sends a sample push to *your own* devices:

```bash
TOKEN=...   # your access token (sign in via /api/auth/login)
curl -X POST http://localhost:5000/api/notifications/test \
  -H "Authorization: Bearer $TOKEN"
```

A registered device should buzz. If `pushConfigured` was true and no push
arrives, check the device actually registered a token
(`GET /api/notifications/devices`).

### B. The real flow — fire a meeting invite

1. Make sure the invitee has signed into the mobile app at least once (so their device token is registered).
2. Invite them (web `/app` → a meeting → **✉ Invite to meeting**, or the API):
   ```bash
   curl -X POST http://localhost:5000/api/projects/{projectId}/meetings/{meetingId}/invite \
     -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
     -d '{"userIds":["<invitee-user-id>"],"sendEmail":true}'
   ```
3. The response should show **`"pushConfigured": true`**.
4. The push lands on the invitee's phone. **Tapping it deep-links straight into
   the live meeting and auto-joins A/V** (if the account isn't activated yet it
   routes through set-password first, then into the meeting).

### C. Read the server log line

```bash
docker logs docker-api-1 2>&1 | grep "meeting-invite"
```

- **FCM on:** `[meeting-invite] meeting <id>: invited N member(s) — push=N email=…`
- **FCM off:** `[meeting-invite] push skipped (no FCM); notified in-app/email — meeting <id>`

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `pushConfigured: false` after setting the key | The var must be `PUSH_FIREBASE_SERVICE_ACCOUNT_JSON` in `docker/.env`; recreate the API (`docker compose up -d --force-recreate api`). Confirm the base64 decodes to JSON starting with `{`. |
| `pushConfigured: true` but no push on device | The device has no registered token — open the app signed-in once, then `GET /api/notifications/devices`. On iOS use a **physical** device. |
| Android push silent | `google-services.json` present + `android.googleServicesFile` set + package = `com.planscape.app`; rebuild the client. |
| iOS push silent | APNs `.p8` uploaded to Firebase with correct Key ID + Team ID; `GoogleService-Info.plist` set; physical device. |
| Invalid/expired tokens | The server prunes them automatically on send failure — no action needed. |

---

## Security notes

- The service-account JSON / `.p8` / `google-services.json` are **secrets**.
  Keep them in `.env` (gitignored) or EAS file secrets. Never commit or
  screenshot them.
- The server stores **no** Firebase secret at rest beyond the env var; tokens in
  `DevicePushToken` are device push tokens (not credentials).
