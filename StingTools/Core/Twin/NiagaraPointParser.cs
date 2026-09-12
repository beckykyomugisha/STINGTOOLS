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
using System.Linq;
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
    /// nothing we know how to read" is a different fact from "the feed is corrupt".
    ///
    /// Both of those are different again from "a points feed with nothing on it", and
    /// until <see cref="RecognisedShape"/> existed they were the SAME VALUE downstream:
    /// a JSON error envelope such as <c>{"error":"unauthorized"}</c> parses, yields zero
    /// points and sets no error, so a rejected station read arrived at the commissioning
    /// valuation as a successful live reading in which nothing is commissioned.</summary>
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

        /// <summary>True when the payload was a shape this parser recognises AS A POINTS
        /// FEED: a bare array, <c>{ "points": [...] }</c>, an object whose values are point
        /// objects, or an empty object. FALSE for a JSON error envelope, and false for a
        /// bare string / number / bool.
        ///
        /// An empty array or an empty object IS recognised — a station with nothing
        /// commissioned yet is a real and expected state early in Stage 3, and turning it
        /// into an error would make a legitimately empty station unreportable.</summary>
        public bool RecognisedShape;

        /// <summary>How many entries looked like point candidates (array elements that are
        /// objects, or object properties whose value is an object). Zero on a genuinely
        /// empty feed; non-zero with zero <see cref="Points"/> means entries were present
        /// and NONE of them was understood.</summary>
        public int CandidateEntries;

        public bool Failed => Error != null;

        /// <summary>True when this body cannot be trusted as a points feed, and so must not
        /// reach a consumer as a successful empty read. Three ways:
        ///   - it was not JSON at all (an HTML error page),
        ///   - it was JSON but not a points-feed shape (an error envelope),
        ///   - it WAS a feed, entries were present, and not one of them was understood
        ///     (a station naming its id field something we do not read).
        /// A feed with zero candidate entries is NOT unusable: that is the empty station.</summary>
        public bool Unusable => Failed || !RecognisedShape || (CandidateEntries > 0 && Points.Count == 0);
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
                    // A bare array or a {points:[...]} wrapper IS a feed, empty or not.
                    r.RecognisedShape = true;
                    foreach (var t in arr)
                    {
                        if (t is JObject) r.CandidateEntries++;
                        Add(r, t as JObject);
                    }
                }
                else if (tok is JObject obj)
                {
                    // An object keyed by point id. Every value should be a point object.
                    // An error envelope ({"error":"unauthorized"}) has scalar values only,
                    // and is NOT a points feed - that is the case that used to arrive
                    // downstream as "live, nothing commissioned".
                    var props = obj.Properties().ToList();
                    r.CandidateEntries = props.Count(pr => pr.Value is JObject);
                    r.RecognisedShape = props.Count == 0 || r.CandidateEntries > 0;
                    foreach (var pr in props) Add(r, pr.Value as JObject, pr.Name);
                }
                // Anything else (a bare string, number or bool) is valid JSON that holds no
                // points. Deliberately NOT an Error - Error means "not JSON at all" - but
                // RecognisedShape stays false, so the caller treats it as a failed read
                // rather than as an empty station.
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
