// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccFetchOutcome.cs
//
// Why this file exists: an ACC read that FAILED used to be indistinguishable
// from one that succeeded and found nothing. AccModelCoordSync "fails soft
// (logs the HTTP status, returns empty) - never throws", and the caller then
// reported zero clashes as "either the model set is clash-clean, or a clash
// test has not completed in ACC yet" and returned Succeeded. On a wrong
// container id or a changed APS sub-path, a fortnightly coordination cycle
// therefore PASSED while having checked nothing.
//
// This carries the outcome alongside the data so the two cannot be conflated.
//
// Deliberately Revit-free AND log-free (no StingLog): it is linked by
// StingTools.Acc.Tests via <Compile Include>, and StingLog drags in
// StingTools.Core.Drawing. Logging stays in the callers, where it already is.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StingTools.V6
{
    /// <summary>How an ACC read ended. The whole point is that <see cref="EmptyOk"/>
    /// - succeeded and genuinely nothing there - is a different value from every
    /// failure, so no caller can render a failure as "nothing found".</summary>
    public enum AccFetchStatus
    {
        /// <summary>Request succeeded, payload parsed, at least one item.</summary>
        Ok = 0,
        /// <summary>Request succeeded, payload parsed, genuinely nothing there.</summary>
        EmptyOk = 1,
        /// <summary>401/403 - token rejected, or the token could not be obtained at all.</summary>
        AuthFailed = 2,
        /// <summary>404 - wrong container id, wrong model set, or a changed APS sub-path.</summary>
        NotFound = 3,
        /// <summary>Any other HTTP error, a network exception, or a body that would not parse
        /// / did not carry the expected array. A schema change lands here.</summary>
        TransportFailed = 4,
    }

    /// <summary>An ACC read's data plus how it ended. <see cref="Value"/> is never null -
    /// an empty collection on failure - so callers cannot NRE their way past the status.</summary>
    public sealed class AccFetchResult<T>
    {
        public AccFetchStatus Status { get; set; } = AccFetchStatus.TransportFailed;
        public T Value { get; set; }
        /// <summary>HTTP status code when one was received; 0 when the request never completed.</summary>
        public int HttpStatus { get; set; }
        /// <summary>Human sentence naming what failed and where. Empty on success.</summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>True only for a request that actually succeeded - Ok or EmptyOk.
        /// Everything else is a failure and must not be reported as "nothing found".</summary>
        public bool Succeeded => Status == AccFetchStatus.Ok || Status == AccFetchStatus.EmptyOk;

        public static AccFetchResult<T> Success(T value, bool empty) => new AccFetchResult<T>
        {
            Status = empty ? AccFetchStatus.EmptyOk : AccFetchStatus.Ok,
            Value = value,
            HttpStatus = 200,
        };

        public static AccFetchResult<T> Failure(AccFetchStatus status, T empty, int httpStatus, string detail)
            => new AccFetchResult<T>
            {
                Status = status,
                Value = empty,
                HttpStatus = httpStatus,
                Detail = detail ?? string.Empty,
            };
    }

    /// <summary>Pure classification of a completed HTTP exchange. No I/O, no logging,
    /// no Revit - so the mapping itself is unit-testable independently of the client.</summary>
    public static class AccFetchOutcome
    {
        /// <summary>Classify by HTTP status plus the number of items the caller managed to
        /// parse. <paramref name="parsedCount"/> below zero means "the body did not parse,
        /// or carried no array where one was required" - a schema change, not an empty set.</summary>
        public static AccFetchStatus Classify(int httpStatus, int parsedCount)
        {
            if (httpStatus == 401 || httpStatus == 403) return AccFetchStatus.AuthFailed;
            if (httpStatus == 404) return AccFetchStatus.NotFound;
            if (httpStatus < 200 || httpStatus >= 300) return AccFetchStatus.TransportFailed;
            if (parsedCount < 0) return AccFetchStatus.TransportFailed;
            return parsedCount == 0 ? AccFetchStatus.EmptyOk : AccFetchStatus.Ok;
        }

        /// <summary>Classify a response whose success shape is "an array under one of
        /// <paramref name="arrayKeys"/>, or a bare array". A 200 whose body carries none of
        /// those keys is <see cref="AccFetchStatus.TransportFailed"/>, NOT EmptyOk - that is
        /// exactly the shape a changed APS sub-path returns.</summary>
        public static AccFetchStatus ClassifyArrayBody(int httpStatus, string body, params string[] arrayKeys)
            => Classify(httpStatus, httpStatus >= 200 && httpStatus < 300 ? CountArray(body, arrayKeys) : -1);

        /// <summary>Item count in the first array found under <paramref name="arrayKeys"/>
        /// (or the root when it is itself an array). -1 when the body will not parse or
        /// carries no such array.</summary>
        public static int CountArray(string body, params string[] arrayKeys)
        {
            var arr = FindArray(body, arrayKeys);
            return arr == null ? -1 : arr.Count;
        }

        /// <summary>The first array under <paramref name="arrayKeys"/>, or the root array.
        /// Null when the body will not parse or carries no such array.</summary>
        public static JArray FindArray(string body, params string[] arrayKeys)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            JToken root;
            try { root = JToken.Parse(body); }
            catch (Exception) { return null; }
            return FindArray(root, arrayKeys);
        }

        /// <summary>Overload for an already-parsed token, so a caller that has one need not
        /// re-serialise to re-classify.</summary>
        public static JArray FindArray(JToken root, IEnumerable<string> arrayKeys)
        {
            if (root == null) return null;
            if (root is JArray bare) return bare;
            var obj = root as JObject;
            if (obj == null) return null;
            if (arrayKeys != null)
                foreach (var key in arrayKeys)
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    if (obj[key] is JArray hit) return hit;
                }
            return null;
        }

        /// <summary>One-line reason suitable for a user-facing dialog. Never says
        /// "no results" for a failure.</summary>
        public static string Describe(AccFetchStatus status, int httpStatus) => status switch
        {
            AccFetchStatus.Ok => "request succeeded",
            AccFetchStatus.EmptyOk => "request succeeded and returned nothing",
            AccFetchStatus.AuthFailed => $"Autodesk rejected the token (HTTP {httpStatus}) - the sign-in or refresh token is not valid for this container",
            // Deliberately NOT the phrase "404 Not Found". That reason phrase invites the
            // exact misreading this whole split exists to prevent - a coordinator seeing
            // "not found" concludes ACC lost the data, rather than that our request was
            // wrong. AccCommandOutcomeTests asserts the words are absent.
            AccFetchStatus.NotFound => $"Autodesk returned HTTP {httpStatus} - the container id, model set or service sub-path is wrong",
            _ => httpStatus > 0
                ? $"the request failed (HTTP {httpStatus}) or returned a payload this client does not recognise"
                : "the request did not complete (network error, or an unreadable payload)",
        };
    }
}
