# Meetings (Track B) — audit & build log

LiveKit = **media plane** (camera/mic/screen). SignalR `MeetingHub` = **co-presence plane**
(surface/markup/chat/roster/state). Never duplicate transports. Secrets via env only.

Verify with **two InPrivate tabs** in one `?meeting=<id>` session.

---

## M1 — unblock + verify A/V

### M1-1 (real bug, fixed) — `/livekit-token` returned HTTP 500, not 501  ✅ served `M1-livekit`
The dev config was already correct (`LiveKit__Url=ws://localhost:7880`, `ApiKey=devkey`,
`ApiSecret=secret`; the `livekit --dev` container uses the same `devkey`/`secret`). The real
failure was the token **factory**: `LiveKitTokenFactory.Create` signed via Microsoft.IdentityModel
(`SymmetricSecurityKey` + `SigningCredentials`), which **rejects keys < 128 bits** (`IDX10653`).
LiveKit's dev secret `secret` is 6 bytes → every token request 500'd.

**Fix:** build + sign the LiveKit JWT with **raw `HMACSHA256`** (System.Security.Cryptography) —
no key-length minimum, and exactly what LiveKit validates with. Works for the dev secret AND any
production secret. **Proven end-to-end against the running stack:** login → create session → POST
`livekit-token` → **HTTP 200** with a valid HS256 JWT (`iss=devkey`, `video.room` = session,
host gets `camera/microphone/screen_share/screen_share_audio`, `url=ws://localhost:7880`).

Creds documented in `Planscape.Server/docker/.env.template` (LiveKit section): dev falls back to
`devkey`/`secret` + the local `--dev` server on `:7880`; staging/cloud set `LIVEKIT_*` in `.env`.

### M1 remaining (client UX) — PENDING
Verify in two tabs that the existing `livekit-av.js` flow works now that tokens mint: Meet → Start
meeting reloads into `?meeting=`; connects; cam/mic prompt; own + 2nd participant tiles; screen-share
in the central pane; surface switching broadcasts. Add/confirm clear in-viewer Join/Leave + cam/mic/
screen controls + an "in a meeting" state. (Tracked below as it's implemented.)
