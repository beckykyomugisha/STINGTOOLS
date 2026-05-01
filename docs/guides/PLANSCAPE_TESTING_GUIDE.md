# Planscape Testing Guide — Plain English Walkthrough

A non-technical guide for trying Planscape from inside Revit using the
StingTools plugin. No coding knowledge needed.

---

## What is Planscape?

Planscape is the cloud service that backs the StingTools plugin. Think
of it as the "online" half of StingTools:

| StingTools (Revit plugin) | Planscape (cloud service) |
|---|---|
| Lives on your laptop | Lives on the internet |
| Tags Revit elements, makes drawings, builds reports | Stores your project's tags, issues, documents and shares them with your team |
| Works without internet | Lets phones, tablets and other team members see your work in real time |

When you "connect to Planscape", your Revit model can push its tagged
elements, issues, and reports to the cloud — and your team can view
them on their phone (the **Planscape mobile app**) or in a web browser
without needing Revit installed.

You don't have to use Planscape. StingTools works fine on its own. But
if you want to share with a team or work on-site from a phone, this is
how.

---

## What you need before starting

| Item | Why |
|---|---|
| Revit 2025, 2026, or 2027 | To run the plugin |
| StingTools installed and loaded | The plugin itself |
| Any Revit project file open | The plugin needs an active document |
| Internet connection | To reach the Planscape server |
| A Planscape login | Either the demo account or your own |

**Don't have a login?** Use the free demo account — it's pre-loaded
and ready to go:

| | |
|---|---|
| Email | `admin@planscape.demo` |
| Password | `admin123` |
| Server URL | `https://planscape-api.onrender.com` |

That's a public test server already running on the internet — you do
**not** need to install anything for it to work.

---

## Step 1 — Open the BIM Coordination Center

This is the screen where Planscape lives.

1. In Revit, open any project file.
2. Look at the right side of the Revit window — you'll see the
   **STING Tools** panel (if not, click `STING Panel` on the ribbon).
3. Click the **BIM** tab at the top of the panel.
4. Click the big **BIM Coordination Center** button.

A new window opens, titled "BIM Coordination Center". You'll see a
list of items down the left side: OVERVIEW, MODEL HEALTH, WARNINGS,
ISSUES, REVISIONS, **PLATFORM**, WORKFLOWS, and so on.

---

## Step 2 — Go to the Platform tab

1. Click **PLATFORM** in the left list.
2. The middle column shows a list of services. **Planscape ★** is at
   the top with a star — that's the one we want. It should already be
   selected; if not, click it.
3. The right side now shows a panel titled
   **"Planscape — Native Collaboration Hub"**.

---

## Step 3 — Connect

Scroll down on the right side until you see **SERVER CONNECTION**.
You'll see three boxes:

| Box | What to type |
|---|---|
| Server URL | `https://planscape-api.onrender.com` (already filled in) |
| Email | `admin@planscape.demo` |
| Password | `admin123` |

Click the green **Connect** button.

After a few seconds, a popup says:

> ✅ Successfully connected to Planscape

The status indicator above the boxes flips from a red dot to a green
one, and the **SERVER STATUS** box now shows your name and the account
tier (probably "Premium").

**That's it — you're connected.** Your Revit model can now talk to
the cloud.

> *If the connect button does nothing or you get an error*, see the
> Troubleshooting table at the bottom.

---

## Step 4 — Send your tagged elements to the cloud

Now let's prove the connection works by pushing some data up.

1. Still on the **PLATFORM** tab, scroll down to **SYNC OPTIONS**.
2. Click the **Sync Elements to Server** button (purple, near the bottom).
3. Wait a few seconds. A popup will tell you how many elements were
   sent.

Behind the scenes, every Revit element that has an ISO 19650 tag
(things like `M-BLD1-Z01-L02-HVAC-SUP-AHU-0003`) gets copied up to
your Planscape project.

> **Don't have any tagged elements yet?** Run **Tags → Auto-Tag**
> first to put tags on everything in the active view. Then come back
> and Sync.

---

## Step 5 — Verify on the mobile app (optional but satisfying)

If you want to *see* your data outside Revit, install the Planscape
mobile app and log in with the same credentials. The Dashboard will
show the compliance percentage you just pushed, and the Issues tab
will show anything you raised.

If you don't have a phone handy, the data is also visible in any web
browser by going to `https://planscape-api.onrender.com/swagger` —
this is a more technical view but it works for spot-checking. Less
critical for the layperson; skip if not interested.

---

## What else can I do once connected?

The BIM Coordination Center has a tab for each thing you can share.
After connecting in step 3, all of these "just work":

| Tab | What it does | Example |
|---|---|---|
| **OVERVIEW** | Sends a compliance snapshot to the cloud | Your team sees the same RAG status you see |
| **ISSUES** | Pushes RFIs / NCRs to Planscape | A site engineer raises a question on their phone, you see it in Revit |
| **REVISIONS** | Records each revision in the cloud | Approval history is preserved |
| **DELIVERABLES** | Sends documents up | Drawings show up in the mobile app's Documents tab |
| **MEETINGS** | Saves agendas + action items | Anyone on the project can read them later |
| **WORKFLOWS** | Logs every workflow you run | Audit trail you can show a client |

Each tab has its own buttons. The one consistent rule: **you have to
do Step 3 first** (connect to Planscape). Without that, the buttons
either do nothing or save locally only.

---

## Common problems

| What you see | What's wrong | How to fix |
|---|---|---|
| Connect button is greyed out | No Revit project is open | Open any `.rvt` file first |
| "Authentication failed" popup | Wrong email, password, or URL | Re-check the three boxes; copy-paste from this guide |
| "Network unreachable" | No internet, or VPN blocking it | Check your connection; try the public test server URL |
| Nothing happens after Connect | Status stays red | Wait 30s — first connect to the test server can be slow as it wakes up. Try again. |
| Sync says "0 elements" | No tagged elements in the model | Run **Tags → Auto-Tag** first |
| Mobile app dashboard is empty | You're logged into a different project | In the mobile app, tap the project switcher and pick the one matching your plugin |

---

## What's actually happening when you click "Connect"?

(Skip this section if you don't care — it's just for the curious.)

1. The plugin sends your email + password over an encrypted connection
   to the Planscape server.
2. The server checks your credentials and sends back a **token** — a
   long string that proves "this is a valid logged-in user".
3. The plugin saves this token in memory, plus a small file inside the
   project's `_BIM_COORD/` folder (called `planscape_connection.json`)
   so you don't have to re-type the URL and email next time.
   *Your password is never saved to disk.*
4. From then on, every button you press in the BIM Coordination Center
   that needs the cloud sends the token along to prove who you are.
5. Every 5 minutes, the plugin also tries to push any pending changes
   in the background — that's the little chip on the dock panel that
   sometimes turns green or amber.

---

## Logging out / disconnecting

There's no formal logout button — closing Revit clears the in-memory
token. To force a fresh login next time, delete the file
`<your project>/_BIM_COORD/planscape_connection.json` and restart
Revit.

---

## When you're ready for "real" use

Once you've tried the demo account and want to use Planscape with
your own team:

1. Sign up at `https://planscape.app/signup` (or click the "I don't
   have a Planscape account yet" link in the **Plugin Onboarding**
   wizard, BIM tab → **Plugin Onboarding**).
2. Create a project in the Planscape web app.
3. Come back to Revit, hit **Connect** with your real email/password,
   and you're set.

The wizard walks you through this in 3 steps if you'd rather not do
it manually:

> BIM tab → **Plugin Onboarding** → follow the prompts.

---

## Quick reference card

Print this and tape it to your monitor:

```
PLANSCAPE — DEMO LOGIN
URL:  https://planscape-api.onrender.com
User: admin@planscape.demo
Pass: admin123

WHERE: Revit → STING panel → BIM tab → BIM Coordination Center
       → PLATFORM tab → Connect

SUCCESS: red dot turns green, status shows your name
```

That's everything you need to start testing.
