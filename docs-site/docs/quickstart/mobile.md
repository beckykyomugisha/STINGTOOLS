# Install the mobile app

The Planscape mobile app is offline-first by design. Site coordinators use it to view federated models, raise and respond to issues, scan QR-tagged assets, and capture site photos — all without needing a constant internet connection. When connectivity returns, the app syncs queued changes automatically.

## Download

- **iOS 15+**: [App Store link]
- **Android 10+**: [Play Store link]

The app is around 35 MB on first download. The bulk of the data — your project list, model index, issue list — downloads on first sync.

## Sign in

Open the app and sign in with your Planscape credentials. These are the same credentials as the web dashboard and the Revit plugin — one account, three surfaces.

If your firm uses Single Sign-On (Enterprise plan), follow the SSO flow in the in-app prompt.

## Permissions

The app asks for two permissions on first launch:

- **Camera** — required for scanning QR-coded asset tags and for attaching photos to issues. Without camera access, scanning and photo capture do not work; everything else still does.
- **Location** — optional. When granted, the app GPS-stamps issue pins so the office can see exactly where on site a problem was raised. Disable this in Settings if you don't want location stamping.

We do not request notification permission until you've created your first issue or been assigned to one — that way we only prompt when the value is obvious.

## Offline mode

When you open a project, the app downloads the latest project state — model index, issue list, document register, team members. After that you can:

- View the federated model in 3D
- Browse and filter issues
- Create new issues with photos, voice notes, and GPS pins
- Update existing issues — change status, add comments
- Scan QR-coded asset tags to look up element history
- Transition documents through CDE states (subject to permission)

When a write happens offline, the app stores it in a local action queue. As soon as connectivity returns, the queue replays automatically — usually in seconds. The sync indicator at the top of the screen shows:

- **Green** — fully synced, all queued actions confirmed by the server
- **Amber** — sync in progress
- **Red** — connection error or conflict that needs human review

## Push notifications

Once you're signed in we ask for permission to send push notifications. We send them for:

- New RFI assignments to you
- Replies on issues you raised
- Document transmittals where you're a recipient
- Stage-gate approval requests where you're an approver
- SLA breaches on issues you own

Notifications respect the quiet hours you set in **Settings → Notifications**.

## Biometric lock

If your device supports Face ID or fingerprint, you can require biometric unlock to open the app. Enable in **Settings → Security → Require biometric**.  This is independent of your device lock — even if your phone is unlocked, the Planscape app stays locked until you authenticate. Useful when sharing a tablet on a busy site.

## Next steps

- [Create your first project](first-project.md) on the web dashboard, then open it on the mobile app
- Review [Offline-first design](../concepts/offline.md) to understand what works without signal and how conflicts are resolved
