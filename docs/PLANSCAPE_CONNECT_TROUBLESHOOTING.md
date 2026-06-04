# Planscape Connect — login troubleshooting

Quick reference for the STING Tools "Planscape Connect" (BIM tab) login flow and
the actionable error messages it produces.

## The #1 first-setup mistake: http vs https

**The local Planscape docker stack serves PLAIN HTTP on port 5000.** It does
**not** do TLS. So the Server URL for the local stack is:

```
http://localhost:5000          ✅  correct
https://localhost:5000         ❌  TLS handshake fails — see below
```

If you enter `https://localhost:5000`, the request **reaches** the server but the
TLS handshake fails (the server answers plaintext, the client expects TLS). This
is **not** an auth failure and **not** "server stopped" — it's a scheme mismatch.

Since the fix in this branch the dialog tells you exactly that:

> **Login failed: couldn't complete a secure (HTTPS) handshake with
> https://localhost:5000. This usually means an http/https mismatch — the local
> Planscape docker stack serves PLAIN HTTP, so use http://localhost:5000 (not
> https). If you're on a corporate network, a TLS-inspection proxy may also be
> blocking the request. (detail: …)**

**Fix:** change the Server URL to `http://localhost:5000`.

### You can usually just type the host

If you leave the scheme off entirely (`localhost:5000`), the plugin now fills in
`http://` for you (`NormalizeServerUrl`, the docker default). It will **never**
silently rewrite an `https://` you typed down to `http://` — an explicit scheme
is always preserved so a real cloud (HTTPS) config isn't masked.

## Cloud / HTTPS server

If you point at a cloud server that **requires** HTTPS but you typed `http://`,
the connection breaks before a response and you'll get:

> **Login failed: the request to http://… reached the network but the connection
> broke before a response (HTTP/transport error). If this server requires HTTPS,
> use https:// instead of http://. A corporate TLS-inspection proxy can also
> cause this. (detail: …)**

**Fix:** use `https://…` for that server.

## Corporate networks / TLS-inspection proxies

A corporate TLS-inspection (MITM) proxy re-signs HTTPS traffic with a corporate
root CA. If that CA isn't trusted by the machine, the TLS handshake to a genuine
`https://` server fails the same way a scheme mismatch does — hence the proxy
note in both messages above. Ask IT to trust the corporate root CA on the
machine, or connect from a network without the inspecting proxy.

## Other error messages (unchanged, for reference)

| You see… | Means | Do |
|---|---|---|
| `email or password is incorrect` | HTTP 401 — reached the API, bad credentials | Re-enter credentials |
| `nothing is listening on …` (local) | ConnectionRefused on localhost | `docker compose up -d` in `Planscape.Server/docker`; wait for `api` healthy |
| `… refused the connection` (remote) | ConnectionRefused on a remote host | Server stopped or firewall blocking the port |
| `could not resolve '…'` | DNS failure | Check URL spelling / DNS / internet |
| `connection … timed out` / `timed out before the server responded` | socket timeout / request cancelled | Server slow/unreachable; firewall dropping packets |
| `did not recognise /api/auth/login (HTTP 404)` | Reachable host, not the Planscape API (e.g. the retired Render deployment) | Use `http://localhost:5000` for the local stack |

## Root cause (for maintainers)

The dialog used to surface the raw `HttpRequestException.Message`
("An error occurred while sending the request.") via the fall-through in
`BuildConnectivityHint`. That classifier handled `SocketException`
(refused/DNS/timeout/unreachable) and `TaskCanceledException`, but **not** the
TLS/handshake family — which is what an http/https mismatch produces (no
`SocketException`; the transport is reached and breaks at TLS). So the single
most common first-setup mistake produced the least useful message.

The fix (`StingTools/BIMManager/PlanscapeServerClient.Connectivity.cs`):

- **FIX 1** — `BuildConnectivityHint` now classifies the handshake family
  (`AuthenticationException`, `IOException`, or a bare `HttpRequestException`
  whose inner is **not** a `SocketException`) ahead of the final fall-through,
  and returns a scheme-aware actionable hint (https → "use http"; http → "use
  https"), preserving the raw `ex.Message` as a trailing `(detail: …)`. All
  existing socket/timeout branches are unchanged and still take precedence.
- **FIX 2** — `LoginAsync` calls `NormalizeServerUrl`, which fills a **missing**
  scheme with `http://` (never rewrites an explicit scheme). The login dialog
  default (`BIMCoordinationCenter.cs`) is already `http://localhost:5000`, and
  `LoadConnectionSettings` drops the dead `planscape-api.onrender.com` URL so the
  dialog falls back to that default.
- **FIX 3** — on a failed login, `LoginAsync` logs the resolved server URL +
  scheme + inner-exception type chain, e.g.
  `serverUrl=https://localhost:5000 scheme=https inner=HttpRequestException->AuthenticationException`,
  so scheme-mismatch / proxy / genuine socket failures are distinguishable from
  the log file alone.

Verified by `StingTools.Connectivity.Tests` (25 assertions over synthetic
exceptions — TLS `AuthenticationException`, bare `HttpRequestException`,
`IOException`, `SocketException(ConnectionRefused)`, `TaskCanceledException`,
and the `NormalizeServerUrl` rules incl. the "never rewrite https" constraint)
and a full `dotnet build` of the StingTools plugin against the Revit 2025 API
(0 errors).
