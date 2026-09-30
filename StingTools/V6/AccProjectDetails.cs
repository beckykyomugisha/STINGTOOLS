// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccProjectDetails.cs
//
// The ACC project's own record (name, job number, address, dates, timezone, type, value,
// currency, status, phase) — read so ACC_SyncProjectInfo can offer it to Revit Project
// Information instead of someone retyping it.
//
// SOURCES, IN ORDER (APS docs, read 2026-09-30):
//   1. GET construction/admin/v1/projects/{projectId}   (bare GUID; scope account:read;
//      2- or 3-legged, "user context optional"). Returns the full record. Whether a plain
//      project MEMBER may call it with a 3-legged token is NOT stated in the reference; the
//      Admin API is documented as an administrator surface (e.g. users/{id}/roles says
//      "only users with hub admin permissions"), so a 403 is expected for a non-admin and
//      is reported as exactly that — never as "the project has no details". UNCONFIRMED
//      against a live KUT tenant.
//   2. On 403 only: GET project/v1/hubs/{hubId}/projects/{b.projectId} (Data Management,
//      data:read, any member). Carries the project NAME and nothing else useful, so the
//      result is marked Partial with the reason, and every other field is absent (empty),
//      not invented.
//
// Revit-free and log-free; StingTools.Acc.Tests links it and drives it over loopback.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StingTools.V6
{
    /// <summary>The ACC project's own record. Empty string = ACC did not give it.</summary>
    public sealed class AccProjectDetails
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string JobNumber { get; set; } = string.Empty;
        public string AddressLine1 { get; set; } = string.Empty;
        public string AddressLine2 { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string StateOrProvince { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CurrentPhase { get; set; } = string.Empty;
        public string ConstructionType { get; set; } = string.Empty;
        public string DeliveryMethod { get; set; } = string.Empty;
        public string ContractType { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public string Latitude { get; set; } = string.Empty;
        public string Longitude { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;

        /// <summary>"admin" (full record) or "data-management" (name only).</summary>
        public string Source { get; set; } = string.Empty;
        /// <summary>Set when only part of the record could be read, saying why.</summary>
        public string Limitation { get; set; } = string.Empty;
        public bool Partial => Limitation.Length > 0;

        /// <summary>The address as one block, lines in the conventional order; empty parts dropped.</summary>
        public string AddressBlock
        {
            get
            {
                var parts = new List<string>();
                void Add(string s) { if (!string.IsNullOrWhiteSpace(s)) parts.Add(s.Trim()); }
                Add(AddressLine1); Add(AddressLine2); Add(City); Add(StateOrProvince); Add(PostalCode); Add(Country);
                return string.Join(", ", parts);
            }
        }
    }

    public static class AccProjectDetailsClient
    {
        /// <summary>What a 403 from the Admin API means, in words a coordinator can act on.</summary>
        public const string AdminForbidden =
            "the ACC Admin API refused this sign-in (HTTP 403) — reading project details, members, companies and roles " +
            "through construction/admin needs an Account Admin or Project Admin sign-in (or a Custom Integration for a " +
            "2-legged app); a normal project member is not allowed";

        public static async Task<AccFetchResult<AccProjectDetails>> GetAsync(AccCredentials creds)
        {
            creds = creds ?? new AccCredentials();
            if (string.IsNullOrWhiteSpace(creds.ProjectId))
                return AccFetchResult<AccProjectDetails>.Failure(AccFetchStatus.NotFound, new AccProjectDetails(), 0,
                    "no ACC project is configured for this model");

            string url = $"{AccIssueSync.Host}/construction/admin/v1/projects/{Uri.EscapeDataString(AccIds.ForAcc(creds.ProjectId))}";
            var resp = await AccHttp.SendAsync(() => Get(url, creds), creds, idempotent: true,
                timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            if (resp.IsSuccess)
            {
                JObject o;
                try { o = JObject.Parse(resp.Body); }
                catch (Exception ex)
                {
                    return AccFetchResult<AccProjectDetails>.Failure(AccFetchStatus.TransportFailed, new AccProjectDetails(),
                        resp.Status, "the Admin project record was not valid JSON: " + ex.Message);
                }
                if (string.IsNullOrEmpty((string)o["id"]) && string.IsNullOrEmpty((string)o["name"]))
                    return AccFetchResult<AccProjectDetails>.Failure(AccFetchStatus.TransportFailed, new AccProjectDetails(),
                        resp.Status, "the Admin project record carried no id or name — the construction/admin/v1 payload shape has changed");
                var d = ParseAdmin(o);
                d.Source = "admin";
                return AccFetchResult<AccProjectDetails>.Success(d, false);
            }

            if (resp.Auth != null && !resp.Auth.Ok)
                return AccFetchResult<AccProjectDetails>.Failure(resp.Auth.Status, new AccProjectDetails(), 0, resp.Auth.Detail);

            if (resp.Status != 403)
                return AccFetchResult<AccProjectDetails>.Failure(resp.Classify(), new AccProjectDetails(), resp.Status,
                    "reading the ACC Admin project record failed: " + resp.Describe());

            // 403: the Admin surface is closed to this sign-in. The name is still readable.
            if (string.IsNullOrWhiteSpace(creds.HubId))
                return AccFetchResult<AccProjectDetails>.Failure(AccFetchStatus.AuthFailed, new AccProjectDetails(), 403,
                    AdminForbidden + ". No hub id is saved, so the project name could not be read from Data Management either.");

            string dmUrl = $"{AccIssueSync.Host}/project/v1/hubs/{Uri.EscapeDataString(AccIds.ForDataManagement(creds.HubId))}" +
                           $"/projects/{Uri.EscapeDataString(AccIds.ForDataManagement(creds.ProjectId))}";
            var dm = await AccHttp.SendAsync(() => Get(dmUrl, creds), creds, idempotent: true,
                timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            if (!dm.IsSuccess)
                return AccFetchResult<AccProjectDetails>.Failure(AccFetchStatus.AuthFailed, new AccProjectDetails(), 403,
                    AdminForbidden + ". The Data Management fallback also failed: " + dm.Describe());
            string name;
            try { name = (string)JObject.Parse(dm.Body)["data"]?["attributes"]?["name"] ?? string.Empty; }
            catch (Exception) { name = string.Empty; }
            if (name.Length == 0)
                return AccFetchResult<AccProjectDetails>.Failure(AccFetchStatus.TransportFailed, new AccProjectDetails(), dm.Status,
                    AdminForbidden + ". The Data Management fallback returned no project name.");

            var partial = new AccProjectDetails
            {
                Id = AccIds.ForAcc(creds.ProjectId),
                Name = name,
                Source = "data-management",
                Limitation = AdminForbidden + ". Only the project NAME was read (Data Management); job number, address, " +
                             "dates, status and value were NOT read and are shown as absent, not as blank in ACC.",
            };
            return AccFetchResult<AccProjectDetails>.Success(partial, false);
        }

        internal static AccProjectDetails ParseAdmin(JObject o)
        {
            string S(string k)
            {
                var t = o[k];
                if (t == null || t.Type == JTokenType.Null) return string.Empty;
                if (t.Type == JTokenType.Date) return ((DateTime)t).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return (t.ToString() ?? string.Empty).Trim();
            }
            var pv = o["projectValue"] as JObject;
            string value = string.Empty, currency = string.Empty;
            if (pv != null)
            {
                var v = pv["value"];
                if (v != null && v.Type != JTokenType.Null)
                    value = Convert.ToString(v.Type == JTokenType.Float || v.Type == JTokenType.Integer ? (object)(double)v : v.ToString(),
                                             CultureInfo.InvariantCulture);
                currency = (string)pv["currency"] ?? string.Empty;
            }
            return new AccProjectDetails
            {
                Id = S("id"),
                Name = S("name"),
                JobNumber = S("jobNumber"),
                AddressLine1 = S("addressLine1"),
                AddressLine2 = S("addressLine2"),
                City = S("city"),
                StateOrProvince = S("stateOrProvince"),
                PostalCode = S("postalCode"),
                Country = S("country"),
                StartDate = DateOnly(S("startDate")),
                EndDate = DateOnly(S("endDate")),
                Timezone = S("timezone"),
                Type = S("type"),
                Status = S("status"),
                CurrentPhase = S("currentPhase"),
                ConstructionType = S("constructionType"),
                DeliveryMethod = S("deliveryMethod"),
                ContractType = S("contractType"),
                Value = value,
                Currency = currency,
                Latitude = S("latitude"),
                Longitude = S("longitude"),
                Platform = S("platform"),
            };
        }

        private static string DateOnly(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return iso;
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
                ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : iso;
        }

        private static HttpRequestMessage Get(string url, AccCredentials c)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return AccIssueSync.WithRegion(req, c);
        }
    }

    // ── The diff: ACC value vs what Revit Project Information holds ─────────────

    /// <summary>Where a row writes. BuiltIn = a Revit Project Information built-in
    /// (name/number/address/status); Shared = a STING shared parameter; None = shown for
    /// information only, STING has no parameter for it.</summary>
    public enum AccInfoTargetKind { BuiltIn, Shared, None }

    public enum AccInfoRowState { Same, Differs, AccEmpty, NoTarget }

    public sealed class AccProjectInfoRow
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public AccInfoTargetKind TargetKind { get; set; }
        /// <summary>Built-in key (PROJECT_NAME, PROJECT_NUMBER, PROJECT_ADDRESS, PROJECT_STATUS)
        /// or the shared-parameter name.</summary>
        public string Target { get; set; } = string.Empty;
        public string AccValue { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public AccInfoRowState State { get; set; }
        /// <summary>True when the person may tick it: a target exists, ACC has a value, and it differs.</summary>
        public bool Writable => State == AccInfoRowState.Differs;
    }

    public static class AccProjectInfoDiff
    {
        public const string BuiltInName = "PROJECT_NAME";
        public const string BuiltInNumber = "PROJECT_NUMBER";
        public const string BuiltInAddress = "PROJECT_ADDRESS";
        public const string BuiltInStatus = "PROJECT_STATUS";

        /// <summary>Build the diff. <paramref name="current"/> reads the model's value for a
        /// target (built-in key or shared-parameter name) and returns null when the target
        /// does not exist in this model (e.g. the shared parameter is not bound).</summary>
        /// <param name="projectCodeParam">ParamRegistry.ORG_PROJECT_CODE</param>
        /// <param name="currencyParam">ParamRegistry.PRJ_ORG_CURRENCY_TXT</param>
        public static List<AccProjectInfoRow> Build(AccProjectDetails d, Func<AccInfoTargetKind, string, string> current,
            string projectCodeParam, string currencyParam)
        {
            d = d ?? new AccProjectDetails();
            var rows = new List<AccProjectInfoRow>();
            void Row(string key, string label, AccInfoTargetKind kind, string target, string acc)
            {
                var r = new AccProjectInfoRow { Key = key, Label = label, TargetKind = kind, Target = target ?? "", AccValue = (acc ?? "").Trim() };
                if (kind == AccInfoTargetKind.None) { r.State = AccInfoRowState.NoTarget; rows.Add(r); return; }
                string cur = current?.Invoke(kind, target);
                if (cur == null) { r.State = AccInfoRowState.NoTarget; r.CurrentValue = "(parameter not in this model)"; rows.Add(r); return; }
                r.CurrentValue = cur.Trim();
                if (r.AccValue.Length == 0) r.State = AccInfoRowState.AccEmpty;
                else r.State = string.Equals(Norm(r.AccValue), Norm(r.CurrentValue), StringComparison.Ordinal)
                    ? AccInfoRowState.Same : AccInfoRowState.Differs;
                rows.Add(r);
            }

            Row("name", "Project name", AccInfoTargetKind.BuiltIn, BuiltInName, d.Name);
            Row("jobNumber", "Project number (ACC job number)", AccInfoTargetKind.BuiltIn, BuiltInNumber, d.JobNumber);
            Row("projectCode", "ISO 19650 project code (from ACC job number)", AccInfoTargetKind.Shared, projectCodeParam, d.JobNumber);
            Row("address", "Project address", AccInfoTargetKind.BuiltIn, BuiltInAddress, d.AddressBlock);
            Row("status", "Project status", AccInfoTargetKind.BuiltIn, BuiltInStatus, d.Status);
            Row("currency", "Project currency", AccInfoTargetKind.Shared, currencyParam, d.Currency);
            // Shown, never written: STING has no parameter for these, and inventing a home
            // for them would be a guess about what the project wants.
            Row("startDate", "Start date", AccInfoTargetKind.None, "", d.StartDate);
            Row("endDate", "End date", AccInfoTargetKind.None, "", d.EndDate);
            Row("timezone", "Time zone", AccInfoTargetKind.None, "", d.Timezone);
            Row("type", "Project type", AccInfoTargetKind.None, "", d.Type);
            Row("currentPhase", "Current phase", AccInfoTargetKind.None, "", d.CurrentPhase);
            Row("value", "Project value", AccInfoTargetKind.None, "", d.Value.Length == 0 ? "" : (d.Value + " " + d.Currency).Trim());
            Row("location", "Latitude / longitude", AccInfoTargetKind.None, "",
                d.Latitude.Length == 0 && d.Longitude.Length == 0 ? "" : d.Latitude + ", " + d.Longitude);
            return rows;
        }

        /// <summary>Compare ignoring case, surrounding and repeated whitespace, and line breaks
        /// (Revit's address box keeps newlines; ACC's lines are comma-joined here).</summary>
        internal static string Norm(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", ", ").Trim().ToUpperInvariant();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*,\s*", ", ");
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
        }
    }
}
