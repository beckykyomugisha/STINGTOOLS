using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>
    /// In-plugin Autodesk (APS) 3-legged OAuth — the "Sign in with Autodesk"
    /// flow that removes the manual refresh-token step. It opens the Autodesk
    /// authorise page in the user's browser, captures the redirect on a
    /// short-lived loopback listener (http://localhost:&lt;port&gt;/callback),
    /// exchanges the one-time code for access + refresh tokens, and stores them
    /// into <see cref="AccCredentials"/> (so <see cref="AccIssueSync"/> can keep
    /// silently refreshing from then on).
    ///
    /// One-time setup the operator does in their APS app:
    ///   • Add the exact callback URL <see cref="RedirectUri"/> (default
    ///     http://localhost:8910/callback) to the app's allowed callback list.
    ///   • Client type "Traditional Web App" (confidential — uses Client Secret),
    ///     matching the Basic-auth token exchange AccIssueSync already uses.
    ///
    /// A raw <see cref="TcpListener"/> on the loopback address is used instead of
    /// HttpListener to avoid the Windows URL-ACL ("Access is denied") footgun for
    /// non-admin users.
    /// </summary>
    public static class AccOAuthFlow
    {
        private const string AuthorizeUrl = "https://developer.api.autodesk.com/authentication/v2/authorize";
        private const string TokenUrl     = "https://developer.api.autodesk.com/authentication/v2/token";

        /// <summary>Default loopback callback port. Must match the URL registered in the APS app.</summary>
        public const int DefaultCallbackPort = 8910;

        /// <summary>Default scopes — enough for ACC Issues + Model Coordination.</summary>
        public const string DefaultScope = "data:read data:write account:read";

        private static readonly HttpClient _http = new HttpClient();

        /// <summary>The exact redirect URI to register in the APS app for a given port.</summary>
        public static string RedirectUri(int port = DefaultCallbackPort) => $"http://localhost:{port}/callback";

        public sealed class SignInResult
        {
            public bool Ok { get; set; }
            public string Message { get; set; } = "";
        }

        /// <summary>
        /// Run the interactive sign-in. Requires <c>creds.ClientId</c> +
        /// <c>creds.ClientSecret</c> to be set; fills AccessToken / RefreshToken /
        /// AccessTokenExpiry and persists via <see cref="AccIssueSync.SaveCredentials"/>.
        /// </summary>
        public static async Task<SignInResult> SignInAsync(
            AccCredentials creds,
            int port = DefaultCallbackPort,
            string scope = DefaultScope,
            CancellationToken ct = default)
        {
            if (creds == null) return Fail("No credentials.");
            if (string.IsNullOrWhiteSpace(creds.ClientId) || string.IsNullOrWhiteSpace(creds.ClientSecret))
                return Fail("Enter Client ID and Client Secret first.");

            string redirect = RedirectUri(port);
            string state = Guid.NewGuid().ToString("N");
            string authUrl =
                $"{AuthorizeUrl}?response_type=code" +
                $"&client_id={Uri.EscapeDataString(creds.ClientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
                $"&scope={Uri.EscapeDataString(scope)}" +
                $"&state={state}&prompt=login";

            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            }
            catch (Exception ex)
            {
                return Fail($"Couldn't open local port {port} ({ex.Message}). Close any other sign-in and retry.");
            }

            try
            {
                try { Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex) { return Fail($"Couldn't open the browser: {ex.Message}"); }

                // Wait up to 3 minutes for Autodesk to redirect back.
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(3));

                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(timeoutCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return Fail("Sign-in timed out (no response within 3 minutes)."); }

                string code, returnedState, error;
                using (client)
                {
                    var query = await ReadCallbackQueryAsync(client, ct).ConfigureAwait(false);
                    query.TryGetValue("code", out code);
                    query.TryGetValue("state", out returnedState);
                    query.TryGetValue("error", out error);

                    string body = string.IsNullOrEmpty(code)
                        ? $"<h2>Autodesk sign-in failed</h2><p>{WebUtility.HtmlEncode(error ?? "no authorization code returned")}</p>"
                        : "<h2>Signed in to Autodesk &#10003;</h2><p>You can close this tab and return to Revit.</p>";
                    await WriteHttpResponseAsync(client, body).ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(error)) return Fail($"Autodesk returned: {error}");
                if (string.IsNullOrEmpty(code))   return Fail("No authorization code returned.");
                if (returnedState != state)       return Fail("State mismatch — sign-in aborted for safety.");

                // Exchange the code for tokens (same Basic-auth scheme AccIssueSync uses).
                var form = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "authorization_code"),
                    new KeyValuePair<string, string>("code", code),
                    new KeyValuePair<string, string>("redirect_uri", redirect),
                });
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{creds.ClientId}:{creds.ClientSecret}"));
                using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = form };
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

                var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                string respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    StingLog.Warn($"AccOAuthFlow: token exchange HTTP {(int)resp.StatusCode}: {respBody}");
                    return Fail($"Token exchange failed (HTTP {(int)resp.StatusCode}). " +
                                $"Confirm {redirect} is registered as a callback URL in your APS app.");
                }

                var json = JObject.Parse(respBody);
                creds.AccessToken       = (string)json["access_token"] ?? "";
                creds.RefreshToken      = (string)json["refresh_token"] ?? creds.RefreshToken;
                int expiresIn           = (int?)json["expires_in"] ?? 3600;
                creds.AccessTokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);
                AccIssueSync.SaveCredentials(creds);

                StingLog.Info("AccOAuthFlow: Autodesk sign-in succeeded; tokens stored.");
                return new SignInResult { Ok = true, Message = "Signed in to Autodesk — tokens stored." };
            }
            catch (Exception ex)
            {
                StingLog.Error("AccOAuthFlow.SignInAsync failed", ex);
                return Fail(ex.Message);
            }
            finally
            {
                try { listener.Stop(); } catch { /* ignore */ }
            }
        }

        private static SignInResult Fail(string msg) => new SignInResult { Ok = false, Message = msg };

        /// <summary>Read the first HTTP request line off the loopback socket and parse its query string.</summary>
        private static async Task<Dictionary<string, string>> ReadCallbackQueryAsync(TcpClient client, CancellationToken ct)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var reader = new StreamReader(client.GetStream(), Encoding.ASCII, false, 2048, leaveOpen: true);
                string requestLine = await reader.ReadLineAsync().ConfigureAwait(false); // "GET /callback?code=..&state=.. HTTP/1.1"
                if (string.IsNullOrEmpty(requestLine)) return result;

                var parts = requestLine.Split(' ');
                if (parts.Length < 2) return result;
                string target = parts[1];
                int q = target.IndexOf('?');
                if (q < 0) return result;

                foreach (var pair in target.Substring(q + 1).Split('&'))
                {
                    if (pair.Length == 0) continue;
                    int eq = pair.IndexOf('=');
                    string key = eq < 0 ? pair : pair.Substring(0, eq);
                    string val = eq < 0 ? "" : pair.Substring(eq + 1);
                    result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(val);
                }
            }
            catch (Exception ex) { StingLog.Warn($"AccOAuthFlow: read callback failed — {ex.Message}"); }
            return result;
        }

        /// <summary>Write a minimal HTML 200 response so the browser shows a friendly message.</summary>
        private static async Task WriteHttpResponseAsync(TcpClient client, string htmlBody)
        {
            try
            {
                string html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>Planscape · Autodesk</title>" +
                              "<style>body{font-family:Segoe UI,Arial,sans-serif;margin:60px;color:#1A237E}</style></head>" +
                              $"<body>{htmlBody}</body></html>";
                byte[] bodyBytes = Encoding.UTF8.GetBytes(html);
                string header =
                    "HTTP/1.1 200 OK\r\n" +
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
