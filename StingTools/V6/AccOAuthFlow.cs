using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>
    /// In-plugin Autodesk (APS) 3-legged OAuth — "Sign in with Autodesk". Opens the Autodesk
    /// authorise page in the browser, captures the redirect on a short-lived loopback
    /// listener (http://localhost:&lt;port&gt;/callback), exchanges the one-time code for
    /// access + refresh tokens and stores them (DPAPI-protected) so every ACC command can
    /// refresh silently from then on.
    ///
    /// TWO APP TYPES ARE SUPPORTED, chosen by whether a Client Secret is entered:
    ///   • "Desktop, Mobile, Single-Page App" (public client) — leave the secret EMPTY.
    ///     The flow uses PKCE (RFC 7636, S256): no secret exists anywhere on the
    ///     workstation, which is the right shape for a plugin installed on many machines.
    ///     RECOMMENDED for new registrations.
    ///   • "Traditional Web App" (confidential) — enter the secret; the token exchange uses
    ///     HTTP Basic, as before. Existing registrations keep working unchanged. PKCE is
    ///     sent here too (harmless, and it binds the code to this sign-in attempt).
    ///
    /// Register the exact callback <see cref="RedirectUri"/> (default
    /// http://localhost:8910/callback) on the APS app.
    ///
    /// Raw TcpListeners (IPv4 AND IPv6 loopback, since a browser may resolve "localhost" to
    /// either) instead of HttpListener, which needs a URL ACL for non-admin users.
    /// </summary>
    public static class AccOAuthFlow
    {
        private static string AuthorizeUrl => AccIssueSync.Host + "/authentication/v2/authorize";

        /// <summary>Default loopback callback port. Must match the URL registered in the APS app.</summary>
        public const int DefaultCallbackPort = 8910;

        /// <summary>
        /// Default scopes, derived from what the clients call:
        ///   data:read    issues, issue types, model sets, clash tests, folders
        ///   data:write   POST an issue; custom attributes on documents
        ///   data:create  storage, items and versions (upload) — without it every upload 403s
        ///   account:read hubs / projects discovery
        /// Scopes are fixed at consent: a token minted with fewer scopes cannot be widened by
        /// a refresh, so a scope change here means signing in again.
        /// </summary>
        public const string DefaultScope = "data:read data:write data:create account:read";

        /// <summary>The exact redirect URI to register in the APS app for a given port.</summary>
        public static string RedirectUri(int port = DefaultCallbackPort) => $"http://localhost:{port}/callback";

        public sealed class SignInResult
        {
            public bool Ok { get; set; }
            public string Message { get; set; } = "";
        }

        // ── PKCE (RFC 7636) ──────────────────────────────────────────────────

        internal static string NewCodeVerifier()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Base64Url(bytes);                 // 43 chars, within RFC 7636's 43..128
        }

        internal static string CodeChallengeS256(string verifier)
            => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>The authorise URL. Pure, so the PKCE parameters are tested.</summary>
        internal static string BuildAuthorizeUrl(string clientId, string redirect, string scope, string state, string challenge)
            => $"{AuthorizeUrl}?response_type=code" +
               $"&client_id={Uri.EscapeDataString(clientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
               $"&scope={Uri.EscapeDataString(scope)}" +
               $"&state={state}" +
               $"&code_challenge={challenge}&code_challenge_method=S256" +
               "&prompt=login";

        /// <summary>
        /// Run the interactive sign-in. Requires <c>creds.ClientId</c>; <c>ClientSecret</c> only
        /// for a Traditional Web App registration.
        /// </summary>
        public static async Task<SignInResult> SignInAsync(
            AccCredentials creds,
            int port = DefaultCallbackPort,
            string scope = DefaultScope,
            CancellationToken ct = default)
        {
            if (creds == null) return Fail("No credentials.");
            if (string.IsNullOrWhiteSpace(creds.ClientId))
                return Fail("Enter the APS Client ID first. (Client Secret only for a 'Traditional Web App' registration; " +
                            "leave it empty for a 'Desktop app' registration, which is recommended.)");

            string redirect = RedirectUri(port);
            string state = Guid.NewGuid().ToString("N");
            string verifier = NewCodeVerifier();
            string authUrl = BuildAuthorizeUrl(creds.ClientId, redirect, scope, state, CodeChallengeS256(verifier));

            var listeners = new List<TcpListener>();
            foreach (var addr in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
            {
                try { var l = new TcpListener(addr, port); l.Start(); listeners.Add(l); }
                catch (Exception ex) { StingLog.Warn($"AccOAuthFlow: loopback {addr} port {port}: {ex.Message}"); }
            }
            if (listeners.Count == 0)
                return Fail($"Couldn't open local port {port}. Close any other sign-in (or the program using the port) and retry.");

            try
            {
                try { Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex) { return Fail($"Couldn't open the browser: {ex.Message}"); }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

                Dictionary<string, string> query = null;
                try { query = await AwaitCallbackAsync(listeners, timeoutCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return Fail("Sign-in timed out (no response within 5 minutes)."); }

                query.TryGetValue("code", out string code);
                query.TryGetValue("state", out string returnedState);
                query.TryGetValue("error", out string error);
                query.TryGetValue("error_description", out string errorDescription);

                if (!string.IsNullOrEmpty(error)) return Fail($"Autodesk returned: {error} {errorDescription}".Trim());
                if (string.IsNullOrEmpty(code)) return Fail("No authorization code returned.");
                if (returnedState != state) return Fail("State mismatch — sign-in aborted for safety.");

                var tok = await AccIssueSync.TokenRequestAsync(creds, new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("grant_type", "authorization_code"),
                    new KeyValuePair<string, string>("code", code),
                    new KeyValuePair<string, string>("redirect_uri", redirect),
                    new KeyValuePair<string, string>("code_verifier", verifier),
                }, ct).ConfigureAwait(false);
                if (!tok.Ok)
                    return Fail(tok.Outcome.Detail +
                                $" Confirm {redirect} is registered as a callback URL in the APS app, and that the app type " +
                                (creds.IsPublicClient ? "is 'Desktop, Mobile, Single-Page App' (no secret)."
                                                      : "is 'Traditional Web App' and the secret is current."));

                AccIssueSync.ApplyTokenResponse(creds, tok.Json);
                if (string.IsNullOrEmpty(creds.RefreshToken) || creds.RefreshTokenIssuedAt == default)
                    creds.RefreshTokenIssuedAt = DateTime.UtcNow;
                if (!AccIssueSync.SaveCredentials(creds, out string saveErr))
                    return new SignInResult
                    {
                        Ok = true,
                        Message = "Signed in, but the tokens could NOT be saved (" + saveErr + "). This Revit session " +
                                  "can use ACC; the next one will need to sign in again.",
                    };

                StingLog.Info($"AccOAuthFlow: Autodesk sign-in succeeded ({(creds.IsPublicClient ? "PKCE public client" : "confidential client")}); tokens stored.");
                return new SignInResult { Ok = true, Message = "Signed in to Autodesk — tokens stored (encrypted for this Windows user)." };
            }
            catch (Exception ex)
            {
                StingLog.Error("AccOAuthFlow.SignInAsync failed", ex);
                return Fail(ex.Message);
            }
            finally
            {
                foreach (var l in listeners) { try { l.Stop(); } catch (Exception) { /* closing */ } }
            }
        }

        /// <summary>Accept connections until one carries the /callback request. A browser may
        /// first ask for /favicon.ico or pre-connect an empty socket; those are answered and
        /// ignored rather than ending the sign-in with "no code".</summary>
        private static async Task<Dictionary<string, string>> AwaitCallbackAsync(List<TcpListener> listeners, CancellationToken ct)
        {
            var pending = listeners.ToDictionary(l => l.AcceptTcpClientAsync(ct).AsTask(), l => l);
            while (true)
            {
                var done = await Task.WhenAny(pending.Keys).ConfigureAwait(false);
                var listener = pending[done];
                pending.Remove(done);
                TcpClient client = await done.ConfigureAwait(false);   // throws on cancellation
                using (client)
                {
                    var (path, query) = await ReadRequestAsync(client, ct).ConfigureAwait(false);
                    bool isCallback = path != null && path.StartsWith("/callback", StringComparison.OrdinalIgnoreCase);
                    if (isCallback)
                    {
                        query.TryGetValue("code", out string code);
                        query.TryGetValue("error", out string err);
                        string body = string.IsNullOrEmpty(code)
                            ? $"<h2>Autodesk sign-in failed</h2><p>{WebUtility.HtmlEncode(err ?? "no authorization code returned")}</p>"
                            : "<h2>Signed in to Autodesk &#10003;</h2><p>You can close this tab and return to Revit.</p>";
                        await WriteHttpResponseAsync(client, 200, body).ConfigureAwait(false);
                        return query;
                    }
                    if (path != null) await WriteHttpResponseAsync(client, 404, "").ConfigureAwait(false);
                }
                pending[listener.AcceptTcpClientAsync(ct).AsTask()] = listener;
            }
        }

        private static SignInResult Fail(string msg) => new SignInResult { Ok = false, Message = msg };

        /// <summary>Read the request line; returns (path, query). Path null for an empty connection.</summary>
        private static async Task<(string, Dictionary<string, string>)> ReadRequestAsync(TcpClient client, CancellationToken ct)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                client.ReceiveTimeout = 10000;
                using var reader = new StreamReader(client.GetStream(), Encoding.ASCII, false, 2048, leaveOpen: true);
                string requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(requestLine)) return (null, result);
                var parts = requestLine.Split(' ');
                if (parts.Length < 2) return (null, result);
                string target = parts[1];
                int q = target.IndexOf('?');
                string path = q < 0 ? target : target.Substring(0, q);
                if (q >= 0)
                    foreach (var pair in target.Substring(q + 1).Split('&'))
                    {
                        if (pair.Length == 0) continue;
                        int eq = pair.IndexOf('=');
                        string key = eq < 0 ? pair : pair.Substring(0, eq);
                        string val = eq < 0 ? "" : pair.Substring(eq + 1);
                        result[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(val.Replace('+', ' '));
                    }
                return (path, result);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                StingLog.Warn($"AccOAuthFlow: read callback failed — {ex.Message}");
                return (null, result);
            }
        }

        private static async Task WriteHttpResponseAsync(TcpClient client, int status, string htmlBody)
        {
            try
            {
                string html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>Planscape · Autodesk</title>" +
                              "<style>body{font-family:Segoe UI,Arial,sans-serif;margin:60px;color:#1A237E}</style></head>" +
                              $"<body>{htmlBody}</body></html>";
                byte[] bodyBytes = Encoding.UTF8.GetBytes(html);
                string header =
                    $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\n" +
                    "Connection: close\r\n\r\n";
                var stream = client.GetStream();
                byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { StingLog.Warn($"AccOAuthFlow: write response failed — {ex.Message}"); }
        }
    }
}
