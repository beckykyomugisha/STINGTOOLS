// PlanscapeServerClient — project capabilities (#547 consumer, #558).
//
// WHAT THIS IS FOR
// ----------------
// The BCC used to have no idea what the signed-in user is allowed to do on a
// project, so every refused action arrived as a generic red failure that was
// indistinguishable from the server being unreachable. An operator who lacks a
// capability was told the system was broken rather than told they lack
// permission — and those warrant different actions ("call IT" vs "ask your PM").
//
// The fix is NOT for the client to re-derive the rule from projectRole /
// iso19650Role. Three surfaces each holding their own copy of the role rules is
// exactly how the eleven dead `ProjectRole == "PM"` gates happened. The server
// resolves it and serves the answer; this client renders what it returns.
//
// THREE STATES, NOT TWO
// ---------------------
//   Allowed  — the server said true.        Offer the control.
//   Denied   — the server said false, or    Disable it, and name the capability.
//              answered 404 (the caller
//              cannot see this project).
//   Unknown  — no answer at all: transport  LEAVE THE CONTROL ENABLED and let
//              failure, timeout, 5xx, or a  the attempt report what happens.
//              body that will not parse.
//
// Unknown is deliberately NOT rendered as denied. Capabilities drive
// AFFORDANCE; the server remains the gate, and every action still attempts and
// reports. Failing closed on a dropped connection locks out legitimate users and
// looks identical to a permissions problem — the precise confusion #558 exists
// to remove. It is also the house anti-pattern in a new costume: an absent
// answer displayed as a definite one, the same mistake as an empty list standing
// in for a failed load.
//
// A 404 IS authoritative-false: it says the caller cannot see the project at
// all, so nothing is possible on it. A dropped connection says nothing about
// permissions.
//
// See #634 for the correction to the #547 docstring, which originally said
// absence of an explicit `true` was `false` including network errors.

#nullable enable annotations
#nullable disable warnings

using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    /// <summary>One capability's answer. See the file header for why there are
    /// three of these and not two.</summary>
    public enum CapabilityState
    {
        /// <summary>No answer — leave the control enabled and let the attempt report.</summary>
        Unknown = 0,
        /// <summary>The server said yes.</summary>
        Allowed,
        /// <summary>The server said no, authoritatively.</summary>
        Denied,
    }

    /// <summary>
    /// The caller's capabilities on one project, as resolved by
    /// <c>GET /api/projects/{id}/members/capabilities</c>.
    ///
    /// Deliberately two fields, matching the two predicates that exist on the
    /// server's <c>ProjectRoles</c>. A third does not get added here first — it
    /// gets proposed on the server, the same way these two did.
    /// </summary>
    public sealed class ProjectCapabilities
    {
        /// <summary>Albums, checklists, distribution groups.</summary>
        public CapabilityState CurateProject { get; set; } = CapabilityState.Unknown;

        /// <summary>Photo approve / reject, share-link issuance, photo policy.</summary>
        public CapabilityState ApproveSitePhotos { get; set; } = CapabilityState.Unknown;

        /// <summary>Why the answer is what it is — shown in diagnostics, never
        /// used as a gate. Null when the server answered normally.</summary>
        public string? Note { get; set; }

        /// <summary>The safe default: we know nothing, so nothing is disabled.</summary>
        public static ProjectCapabilities Unknown(string? note = null)
            => new ProjectCapabilities { Note = note };

        /// <summary>Every capability authoritatively false. Only for a 404 —
        /// the caller cannot see this project at all.</summary>
        public static ProjectCapabilities AllDenied(string? note = null)
            => new ProjectCapabilities
            {
                CurateProject     = CapabilityState.Denied,
                ApproveSitePhotos = CapabilityState.Denied,
                Note              = note,
            };
    }

    public sealed partial class PlanscapeServerClient
    {
        /// <summary>
        /// <c>GET /api/projects/{projectId}/members/capabilities</c>.
        /// Never returns null and never throws — an unreachable server yields
        /// all-Unknown, which leaves every control enabled.
        /// </summary>
        public async Task<ProjectCapabilities> GetProjectCapabilitiesAsync(Guid projectId)
        {
            if (projectId == Guid.Empty)
                return ProjectCapabilities.Unknown("No project selected.");

            if (!await EnsureAuthenticatedAsync().ConfigureAwait(false))
                return ProjectCapabilities.Unknown(LastError ?? "Not connected to Planscape.");

            try
            {
                var resp = await GetAsync($"/api/projects/{projectId}/members/capabilities")
                    .ConfigureAwait(false);

                // 404 = the caller cannot see this project. Authoritative false.
                if (resp.status == 404)
                    return ProjectCapabilities.AllDenied("You do not have access to this project.");

                if (!resp.ok)
                {
                    // 5xx, 401 after a token race, a proxy error page — the server
                    // did not answer the question. Not a denial.
                    StingLog.Warn($"Capabilities: HTTP {resp.status} for {projectId}");
                    return ProjectCapabilities.Unknown($"Capabilities unavailable (HTTP {resp.status}).");
                }

                var j = JObject.Parse(resp.body);

                // Read only an EXPLICIT boolean. A missing or non-boolean field is
                // a contract mismatch, not a "no" — the server would have to have
                // changed shape for this to happen, and guessing on a shape change
                // is how a client silently disables half its own UI.
                return new ProjectCapabilities
                {
                    CurateProject     = ReadFlag(j, "canCurateProject"),
                    ApproveSitePhotos = ReadFlag(j, "canApproveSitePhotos"),
                };
            }
            catch (Exception ex)
            {
                // Includes transport failures and unparseable bodies. Unknown.
                StingLog.Warn($"GetProjectCapabilitiesAsync: {ex.Message}");
                return ProjectCapabilities.Unknown(ex.Message);
            }
        }

        private static CapabilityState ReadFlag(JObject j, string field)
        {
            var tok = j[field];
            if (tok == null || tok.Type == JTokenType.Null) return CapabilityState.Unknown;
            if (tok.Type != JTokenType.Boolean) return CapabilityState.Unknown;
            return tok.Value<bool>() ? CapabilityState.Allowed : CapabilityState.Denied;
        }
    }
}
