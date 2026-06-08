# PR #306 — Pre-merge checklist

Branch `claude/optimistic-bell-EfjJw`. **Do not merge** until the runtime-pending
rows are human-verified. Legend: ✅ done & verified here · 🧪 code-done, runtime-
pending (needs Revit click / 2-browser A/V / device push / real send).

## Build / verify gates (WS7)

| Gate | State | Proof |
|---|---|---|
| Server `dotnet build` clean | ✅ | Full solution compiled (Shared/Core/MIM/Infrastructure/API/Tests); API build `0 Error(s)`. Only benign NU1603/CS0618/xUnit2009 warnings. |
| Email escaping unit tests | ✅ | `dotnet test --filter EmailEscapingTests` → 2 passed. |
| Viewer/web `node --check` | ✅ | `dashboard.js`, `meetings-core.js`, `meeting-sync.js`, `coordination-viewer.js` (wwwroot + `assets/viewer/`) all valid. |
| api image rebuild + SERVED markers | ✅ | `docker compose build api && up -d --force-recreate api`; health 200; markers below all served. |
| Plugin (StingTools.dll) build + DLL copy | 🧪 | C# changes (WS2, DataProtection) compile-clean by inspection; **Revit-API plugin build + CompiledPlugin copy can't run in this sandbox** — needs a Windows+Revit build, then user restarts Revit. |

### SERVED marker matrix (`curl localhost:5000`)
| File | Marker | Served |
|---|---|---|
| `js/dashboard.js` | `projectMemberOptions`, `md-del-attendee` | ✅ |
| `meeting-sync.js` | `ws1d-syncview`, `meetSyncView` | ✅ |
| `coordination-viewer.js` | `ws1d-syncbutton`, `syncToParticipants` | ✅ |
| `livekit-av.js` | `toggleRecording` (record controls) | ✅ |

## Workstreams

| WS | Item | State | Commit | Verify |
|---|---|---|---|---|
| WS2 | Access Management members persist on reopen | 🧪 | `a55cd8746` | Code: restore from same `STING_BIM_MANAGER/team_members.json` the save writes. **Human (Revit):** add/save members → reopen project → rows present. |
| WS1c | Editable attendee grid + member dropdowns + remove row | ✅ SERVED | `d9e28b49e` | Web: add attendee from member dropdown; ✕ removes; action-assignee dropdown. |
| WS1d | "🔗 Sync my view to participants" (host-gated) | ✅ SERVED | `d11e142a0`,`bd368a9aa` | **Human (2-tab):** host isolates → 🔗 → follower view updates. |
| WS1b | Record start/stop + recordings list | ✅ (web) | (pre-existing) | **Human (webcam):** record → end → `.mp4` plays in `/app` Recordings. |
| WS1a | Meeting invitations (in-app+email+push) | ✅ (server/web) · 🧪 mobile | (pre-existing) | **Human (device+Firebase):** push → tap → join. |
| WS3 | Email escaping regression test | ✅ | `f553656dd` | `<b>x</b>` project name renders as text; `{{Body}}` stays verbatim. |
| WS4 | EF reconcile (TemplateOpRecords + IFC cols) | ✅ (prior) | `7d458b641` | Boot log `[schema-drift] OK`; `ef migrations add` empty diff. |
| WS5 | Caddy+LE prod overlay, DataProtection, secrets, drift | ✅ | `3b50287a4` | `compose config` validates; `app.${DOMAIN}` proxied; dpkeys/caddy_data volumes; LiveKit secret rotated to env; `.env` untracked. **Human:** real VPS deploy. |
| WS6 | Firebase plug-and-play + `docs/PUSH_FIREBASE.md` | ✅ (prior) | `30365867d` | base64 SA JSON → `pushConfigured=true`; blank → graceful skip. |

## Runtime-pending (the human's tests — none blocking code review)
- [ ] **Revit plugin:** build `StingTools.dll` on Windows+Revit, copy full DLL set to `CompiledPlugin\`, restart Revit. Verify WS2 (members persist across reopen).
- [ ] **2-browser A/V:** WS1d surface-sync follower update; WS1b real-webcam record→play.
- [ ] **Device push:** WS1a meeting-invite FCM with `PUSH_FIREBASE_SERVICE_ACCOUNT_JSON` set.
- [ ] **Real email:** WS3/Resend live send from a verified domain (`docs/EMAIL_RESEND.md`).
- [ ] **VPS:** WS5 Phase-1 deploy (Caddy LE cert issues, LiveKit TURN/TLS-443).

## No-secrets check
- `.env` / `.env.local` gitignored + untracked (verified). No keys in code/commits.
- Prod paths derive every host from `DOMAIN`/`PUBLIC_BASE_URL`; only `127.0.0.1` is the deliberate MinIO-console loopback bind.
