// StingTools — Niagara / oBIX points-feed shape handling.
//
// Deliberately free of StingLog and in its own file so it can be <Compile Include>d
// into StingTools.Boq.Tests. NiagaraJsonClient.cs logs, and StingLog carries a
// StingTools.Core.Drawing using plus a user32 DllImport, so nothing in that file was
// reachable by an automated test.
//
// That mattered more here than the file size suggests. HasPresentValue decides
// NiagaraPoint.HasValue, which decides BmsValuation.IsCommissioned, which decides
// whether a BOQ line is certified for payment. A configured-but-dead point answers
// with a status and no value; reading that as "has a value" certifies money against
// equipment that is not running, and nothing downstream can tell the difference.
//
// This parser NEVER throws and NEVER logs: it returns what it understood plus what it
// could not, and NiagaraJsonClient does the logging. A station feed is third-party
// data, so a shape we have not seen must degrade to "no points" rather than take out
// the valuation run.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Twin
{
    /// <summary>One station point: its raw Niagara status and whether it is reporting a
    /// present value. Both halves are needed — see <see cref="BmsValuation.IsCommissioned"/>,
    /// which requires an in-service status AND a value.</summary>
    public sealed class NiagaraPoint
    {
        public string Status = "";
        public bool HasValue;
    }

    /// <summary>What a parse understood, and what it did not. <see cref="Error"/> is
    /// non-null only when the payload was not JSON at all; a shape we simply did not
    /// recognise yields zero points and no error, because "the feed parsed but held
    /// nothing we know how to read" is a different fact from "the feed is corrupt".</summary>
    public sealed class NiagaraPointParseResult
    {
        public Dictionary<string, NiagaraPoint> Points =
            new Dictionary<string, NiagaraPoint>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Parse failure message, or null when the payload was valid JSON.</summary>
        public string Error;

        /// <summary>Entries that looked like points but carried no id we could key on.
        /// Surfaced because a feed naming its id field something we do not read produces
        /// an EMPTY result that is otherwise indistinguishable from an empty station.</summary>
        public int SkippedNoId;

        public bool Failed => Error != null;
    }

    public static class NiagaraPointParser
    {
        /// <summary>Parse a Niagara / oBIX points feed into deviceId → point. Tolerates the
        /// three shapes stations actually emit: a bare array, <c>{ "points": [...] }</c>, and
        /// an object keyed by point id. Never throws.</summary>
        public static NiagaraPointParseResult Parse(string json)
        {
            var r = new NiagaraPointParseResult();
            if (string.IsNullOrWhiteSpace(json)) return r;
            try
            {
                var tok = JToken.Parse(json);
                JArray arr = tok as JArray ?? (tok as JObject)?["points"] as JArray;
                if (arr != null)
                {
                    foreach (var t in arr) Add(r, t as JObject);
                }
                else if (tok is JObject obj)
                {
                    foreach (var pr in obj.Properties()) Add(r, pr.Value as JObject, pr.Name);
                }
                // Anything else (a bare string, number or bool) is valid JSON that holds no
                // points. Zero points, no error — the caller reports "no live data".
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
            return r;
        }

        /// <summary>
        /// Whether a point's value token counts as a present value — the certification
        /// decision, exposed so it can be asserted directly.
        ///
        /// <para>Absent or JSON null is a configured-but-dead point: FALSE. A nested oBIX
        /// object (<c>&lt;real val="21.4"/&gt;</c> arrives as <c>{ "val": 21.4 }</c>) is read
        /// one level down. Everything else is stringified rather than cast, because a cast
        /// of a JArray to string throws and a station is free to send us a shape we did not
        /// plan for.</para>
        ///
        /// <para>A numeric <c>0</c> and a boolean <c>false</c> ARE present values. A point
        /// reading zero is reporting; only an empty or whitespace payload is not.</para>
        /// </summary>
        public static bool HasPresentValue(JToken val)
        {
            if (val == null || val.Type == JTokenType.Null) return false;
            if (val.Type == JTokenType.Object)
            {
                var inner = val["val"];
                return inner != null && inner.Type != JTokenType.Null
                       && !string.IsNullOrWhiteSpace(inner.ToString());
            }
            return !string.IsNullOrWhiteSpace(val.ToString());
        }

        /// <summary>The value token for a point, across the field names stations use.</summary>
        public static JToken ValueToken(JObject o) =>
            o == null ? null
                      : (o["present_value"] ?? o["presentValue"] ?? o["out"] ?? o["value"] ?? o["val"]);

        /// <summary>The id to key a point on: an explicit deviceId, else id, else name, else
        /// the property name when the feed is an object keyed by id.</summary>
        public static string PointId(JObject o, string keyFallback = null)
        {
            if (o == null) return (keyFallback ?? "").Trim();
            return ((string)o["deviceId"] ?? (string)o["id"] ?? (string)o["name"] ?? keyFallback ?? "").Trim();
        }

        private static void Add(NiagaraPointParseResult r, JObject o, string keyFallback = null)
        {
            if (o == null) return;
            string id = PointId(o, keyFallback);
            if (id.Length == 0) { r.SkippedNoId++; return; }

            // A duplicate id in one feed is a station data problem, not ours to resolve.
            // Last wins — arbitrary, but deterministic, and pinned by a test so it cannot
            // drift into "whichever the dictionary happened to keep".
            r.Points[id] = new NiagaraPoint
            {
                Status = (string)o["status"] ?? "",
                HasValue = HasPresentValue(ValueToken(o))
            };
        }
    }
}
