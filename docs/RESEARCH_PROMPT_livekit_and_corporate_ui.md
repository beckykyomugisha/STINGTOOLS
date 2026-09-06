# Research prompt — LiveKit meetings on Render + corporate ACC-style UI + multi-firm support

Paste this whole file's content into a new session. Do research and produce a scoped plan —
**do not start building yet.**

## Context (verified facts, not assumptions — re-verify before acting on them since time has passed)

- Repo: `beckykyomugisha/STINGTOOLS`, server at `Planscape.Server/`, web app at `planscape-web/`
  (Next.js, deployed to Render as `planscape-web-free`), mobile app at `Planscape/` (Expo).
- Two Render Blueprints exist: `render.yaml` (paid, ~$54/mo, never deployed — owner's card keeps
  declining Render's verification charge) and `render.free.yaml` (free tier, deployed and working
  as of 2026-07-31 — see `docs/PLANSCAPE_IDENTITY_HANDOFF.md` and the git history around commit
  `2abc332`/PR #511 for the most recent fixes).
- **LiveKit (the video/WebRTC meeting layer) is substantially built already** — read
  `docs/MEETINGS_AUDIT.md` in full before doing anything else. It documents features M1 through M5
  and N1 through N5: join/leave A/V, device picker, screen share, collaborative document markup,
  chat/reactions/raise-hand/roster/host-moderation, recording via LiveKit Egress to S3/MinIO
  (proven end-to-end locally — a real 5.5MB .mp4 was recorded and played back), AEC-specific tools
  (raise issue from a meeting, clash-review stepping, viewpoint snapshots, meeting minutes).
- **The gap**: almost everything in that audit is marked "SERVED-proven" (meaning: the API/JS
  bundle was confirmed deployed and responds correctly) but "PENDING-HUMAN-VERIFY" for the actual
  two-real-browser-tabs-with-webcams test — and that testing only ever happened against a local
  `docker compose` stack on `localhost:5000` with LiveKit running in `--dev` mode. **Neither Render
  deployment (paid or free) has LiveKit configured at all.** `render.yaml` declares
  `LiveKit__ApiKey`, `LiveKit__ApiSecret`, `LiveKit__ServerUrl`, `LiveKit__Url` as `sync: false`
  (i.e. never set) with a comment pointing at "LiveKit Cloud (or self-host)". `render.free.yaml`
  has no LiveKit keys at all.
- LiveKit itself (the actual meeting/media server — an SFU handling real-time WebRTC) is a
  **stateful, low-latency, always-on service** — this is architecturally different from the
  request/response API and matters for whether it can run on Render's free tier (which sleeps
  after ~15 min idle) or needs either a paid always-on instance or an external LiveKit Cloud
  account.
- Separately, the user wants a **corporate, ACC-(Autodesk Construction Cloud)-style UI shell**:
  left-side navigation tabs, a projects list, editable grids on the right — they say this has been
  designed/discussed multiple times before in this project's history. Search prior conversation
  history / any design docs in the repo (grep for "ACC" or check `docs/` and any Figma/design
  references) before assuming nothing exists.
- The user also wants to know what it takes to **reliably support at least two separate firms**
  (tenants) at once — real multi-tenant isolation, not just the schema-level tenant scoping that
  may already exist. Check `Planscape.Server` for existing tenant-isolation tests
  (`TenantIsolation*` tests in `Planscape.Tests`) and any known gaps.

## What to research (produce findings, not code)

1. **LiveKit hosting options and real cost**, compared honestly:
   - LiveKit Cloud managed free tier: exact limits (concurrent participants, minutes/month,
     recording/egress inclusion), and paid tier pricing once they'd exceed it with 2 firms actively
     using video meetings.
   - Self-hosting LiveKit on Render: does Render support the always-on background service type
     needed (a `pserv` or `worker` with the right networking/UDP support — LiveKit needs UDP for
     WebRTC media, which needs checking against Render's supported service types), what tier/cost,
     and whether it composes with the existing `render.yaml` LiveKit Egress + MinIO/S3 recording
     pipeline already coded.
   - Whether the free-tier deploy can support LiveKit *at all* even in a limited/demo capacity
     (e.g., LiveKit Cloud free tier reached via env vars, no self-hosted media server needed) — this
     may be the fastest unblock for "I want to see cameras working now" without full paid
     infrastructure.
2. **What's actually left to build vs. just wire up.** Read `docs/MEETINGS_AUDIT.md` completely,
   list every item still marked PENDING-HUMAN-VERIFY, and separate "needs LiveKit connected then
   just verify" from "needs new code" (the audit itself flags a few real gaps: no stroke/hand-state
   replay on late join, mute/remove are client-honored signals not server-enforced, mobile parity
   for markup/chat/roster is incomplete).
3. **Corporate ACC-style UI shell** — scope as a distinct UI/UX project:
   - Search this repo's history/docs for prior ACC-style design work already done (don't assume a
     blank slate if the user says it's been designed before).
   - Identify what changes: is this a reskin of `planscape-web` (Next.js) layout/nav, or does it
     touch the mobile app and BIM Coordination Center (WPF) too?
   - Rough size the effort (component count, whether an existing design system exists to extend).
4. **Two-firm multi-tenancy readiness**:
   - Audit existing tenant isolation (DB-level scoping, the `TenantIsolation*` test suite mentioned
     in the CI run history, auth/JWT tenant claims, the identity handoff's tenant-per-user
     constraint noted in `docs/PLANSCAPE_IDENTITY_HANDOFF.md` "Deliberately out of scope" section —
     it currently only carries ONE tenant per handoff).
   - What's missing for two firms to safely coexist on one deployment (billing separation is
     already handled by the Cloudflare D1 side per `project-planscape-billing` — focus on the
     Postgres/API side: data isolation, storage bucket separation, meeting/LiveKit room isolation).

## Deliverable

A written plan (not code) covering:
- Fastest path to a working camera demo (even if temporary/limited) vs. the properly-scoped
  production path, with honest cost figures for each.
- A step-by-step scope for the ACC-style UI shell as its own workstream, sized realistically.
- What's required, concretely, to safely run two firms on this platform at once.
- Clear sequencing recommendation: what should happen first, what can run in parallel, and what
  depends on the still-unresolved Render paid-tier card block.

Do not start implementation until this plan is reviewed with the user.
