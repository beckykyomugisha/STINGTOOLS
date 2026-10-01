// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccCdeRouting.cs
//
// Why this file exists: when STING uploads a deliverable to ACC Docs, the folder it
// lands in IS the ISO 19650 CDE state. PUBLISHED is a contractual statement, so filing
// an S3 drawing there (or an A1 drawing in WIP) is not a cosmetic mistake. This answers
// "which ACC folder does this document go in?" from the ONE input ISO 19650 has — the
// suitability code — through the codebase's single mapping,
// StingTools.Core.Drawing.Iso19650Suitability.CdeStateFor, and a per-project map of
// CDE state -> ACC folder URN.
//
// NO GUESSING. An unrecognised suitability, or a state the project has not mapped to a
// folder, is a named "not routed" result. There is no default folder: a fallback would
// put a document in a CDE state nobody chose, and the upload would report success.
//
// It also defines the STING metadata attribute set stamped on each uploaded version as
// ACC Docs custom attributes (see AccDocsMetadata.cs), and builds the values from a
// small input record. The CDE state attribute is DERIVED from the suitability, never
// supplied separately, so the two cannot disagree (the Iso19650Suitability header
// explains why that matters).
//
// Revit-free and log-free: StingTools.Acc.Tests links it via <Compile Include>.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Drawing;

namespace StingTools.V6
{
    /// <summary>How a CDE routing request ended.</summary>
    public enum AccCdeRouteStatus
    {
        /// <summary>Suitability recognised, its state mapped to a folder URN.</summary>
        Routed = 0,
        /// <summary>No ISO 19650 suitability code could be read from the input. Nothing
        /// is uploaded to a CDE state that was never decided.</summary>
        UnknownSuitability = 1,
        /// <summary>The suitability is valid, but the project has no ACC folder mapped
        /// for its CDE state (acc_settings.json "cdeFolders").</summary>
        StateNotConfigured = 2,
        /// <summary>A value is mapped for the state, but it is not a folder URN. A
        /// project name, a web URL or a folder path pasted into the setting lands here
        /// rather than as a 404 at upload time.</summary>
        MalformedFolderUrn = 3,
    }

    /// <summary>The routing answer. <see cref="FolderUrn"/> is empty unless
    /// <see cref="IsRouted"/>; there is no fallback folder.</summary>
    public sealed class AccCdeRoute
    {
        public AccCdeRouteStatus Status { get; set; }
        /// <summary>The suitability code as normalised ("S4 - FOR APPROVAL" -> "S4"); empty when unrecognised.</summary>
        public string SuitabilityCode { get; set; } = string.Empty;
        /// <summary>WIP / SHARED / PUBLISHED / ARCHIVE; empty when the suitability is unrecognised.</summary>
        public string CdeState { get; set; } = string.Empty;
        public string FolderUrn { get; set; } = string.Empty;
        /// <summary>A sentence naming what failed (the state, the setting). Empty when routed.</summary>
        public string Detail { get; set; } = string.Empty;
        public bool IsRouted => Status == AccCdeRouteStatus.Routed;
    }

    public static class AccCdeRouting
    {
        /// <summary>The four CDE states a folder map may name. SUPERSEDED/WITHDRAWN are
        /// retirement transitions, not containers — CdeStateFor files AB/AR in ARCHIVE.</summary>
        public static readonly IReadOnlyList<string> CdeStates = new[] { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" };

        /// <summary>
        /// Route a document to its ACC folder by suitability.
        /// </summary>
        /// <param name="suitability">The code, or a cell holding code and description
        /// ("S4", "S4 - FOR APPROVAL"). Normalised through Iso19650Suitability.ExtractCode.</param>
        /// <param name="cdeFolders">State -> folder URN (keys case-insensitive), from
        /// acc_settings.json "cdeFolders". Null is treated as "nothing configured".</param>
        public static AccCdeRoute Resolve(string suitability, IReadOnlyDictionary<string, string> cdeFolders)
        {
            string code = Iso19650Suitability.ExtractCode(suitability);
            string state = code.Length > 0 ? Iso19650Suitability.CdeStateFor(code) : null;
            if (string.IsNullOrEmpty(state))
                return new AccCdeRoute
                {
                    Status = AccCdeRouteStatus.UnknownSuitability,
                    Detail = string.IsNullOrWhiteSpace(suitability)
                        ? "the document carries no ISO 19650 suitability code, so its CDE state (and ACC folder) is undecided — set the suitability before uploading"
                        : $"'{suitability}' is not an ISO 19650 suitability code (S0-S7, A1-An, B1-Bn, CR, AB, AR), so its CDE state (and ACC folder) is undecided",
                };

            string urn = Lookup(cdeFolders, state);
            if (string.IsNullOrWhiteSpace(urn))
                return new AccCdeRoute
                {
                    Status = AccCdeRouteStatus.StateNotConfigured,
                    SuitabilityCode = code,
                    CdeState = state,
                    Detail = $"suitability {code} files in the {state} CDE state, but no ACC folder is configured for {state} " +
                             $"(acc_settings.json \"cdeFolders\".\"{state}\"). Nothing was uploaded to a substitute folder.",
                };

            urn = urn.Trim();
            if (!LooksLikeFolderUrn(urn))
                return new AccCdeRoute
                {
                    Status = AccCdeRouteStatus.MalformedFolderUrn,
                    SuitabilityCode = code,
                    CdeState = state,
                    Detail = $"the ACC folder configured for {state} ('{urn}') is not a folder URN — expected " +
                             "'urn:adsk.wipprod:fs.folder:co.…' (the Data Management folder id, not a name, path or web link)",
                };

            return new AccCdeRoute { Status = AccCdeRouteStatus.Routed, SuitabilityCode = code, CdeState = state, FolderUrn = urn };
        }

        /// <summary>The states in <see cref="CdeStates"/> the map does not cover, so a
        /// settings check can name every gap up front instead of one per failed upload.</summary>
        public static IReadOnlyList<string> UnconfiguredStates(IReadOnlyDictionary<string, string> cdeFolders)
            => CdeStates.Where(s => string.IsNullOrWhiteSpace(Lookup(cdeFolders, s))).ToList();

        /// <summary>Data Management folder ids are "urn:adsk.wip…:fs.folder:…" (wipprod,
        /// wipemea, …). Only the documented shape is accepted; the region segment is not
        /// pinned because it varies by hub.</summary>
        /// <summary>
        /// C9: with NO cdeFolders mapped, every upload goes to one folder (the configured upload
        /// folder, else 'Project Files'). A WIP (S0) file there sits beside shared and published
        /// documents — ISO 19650 keeps work in progress out of the shared areas. Returns why such
        /// a file is not sent, or null when it may go (routed, or not WIP).
        /// </summary>
        public static string UnroutedWipRefusal(string suitability, IReadOnlyDictionary<string, string> cdeFolders)
        {
            if (cdeFolders != null && cdeFolders.Count > 0) return null;   // routed per state by Resolve
            string code = Iso19650Suitability.ExtractCode(suitability ?? string.Empty);
            if (string.IsNullOrEmpty(code)) return null;
            if (!string.Equals(Iso19650Suitability.CdeStateFor(code), "WIP", StringComparison.OrdinalIgnoreCase)) return null;
            return $"{code} is work in progress, and this project maps no \"cdeFolders\", so it would land in the same " +
                   "ACC folder as shared and published documents. Map cdeFolders (WIP → its own folder) or share the " +
                   "sheet at an S1–S7 suitability first";
        }

        public static bool LooksLikeFolderUrn(string urn)
            => !string.IsNullOrWhiteSpace(urn)
               && urn.StartsWith("urn:adsk.", StringComparison.Ordinal)
               && urn.IndexOf(":fs.folder:", StringComparison.Ordinal) > 0;

        private static string Lookup(IReadOnlyDictionary<string, string> map, string state)
        {
            if (map == null) return null;
            foreach (var kv in map)
                if (string.Equals((kv.Key ?? "").Trim(), state, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            return null;
        }
    }

    // ── The STING metadata attribute set ───────────────────────────────────────

    /// <summary>One custom attribute STING stamps: its ACC name and type.</summary>
    public sealed class AccAttributeSpec
    {
        public string Name { get; }
        /// <summary>ACC type: "string", "date" or "array".</summary>
        public string Type { get; }
        /// <summary>Drop-list values; only for Type "array".</summary>
        public IReadOnlyList<string> ArrayValues { get; }
        /// <summary>Other ACC types an EXISTING definition may have and still be used — e.g.
        /// Suitability as a drop-down ("array") the admin created. STING creates <see cref="Type"/>.</summary>
        public IReadOnlyList<string> AlsoAccepts { get; }

        public AccAttributeSpec(string name, string type, IReadOnlyList<string> arrayValues = null,
                                IReadOnlyList<string> alsoAccepts = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Type = type ?? "string";
            ArrayValues = arrayValues ?? Array.Empty<string>();
            AlsoAccepts = alsoAccepts ?? Array.Empty<string>();
        }

        /// <summary>Can an existing definition of type <paramref name="type"/> hold this attribute?</summary>
        public bool Accepts(string type) =>
            string.Equals(type, Type, StringComparison.OrdinalIgnoreCase) ||
            AlsoAccepts.Any(t => string.Equals(type, t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The ACC custom-attribute NAMES STING writes to. One set per project, from
    /// acc_settings.json "docsAttributeNames" — so STING writes to the columns the ACC admin
    /// created instead of adding a second "suitability" column beside them that drifts.
    /// Defaults are exactly the names docs/KUT_ACC_DAY1_PLAYBOOK.md §3.4 tells the admin to create.
    /// </summary>
    public sealed class AccAttributeNames
    {
        public string DocumentNumber { get; private set; } = "Document Number";
        public string Suitability    { get; private set; } = "Suitability";
        public string Revision       { get; private set; } = "Revision";
        public string CdeState       { get; private set; } = "CDE State";
        public string Originator     { get; private set; } = "Originator";
        public string TransmittalId  { get; private set; } = "STING Transmittal Id";

        public static readonly AccAttributeNames Default = new AccAttributeNames();

        /// <summary>The settings keys, in the order the attributes are listed.</summary>
        public static readonly IReadOnlyList<string> Keys =
            new[] { "documentNumber", "suitability", "revision", "cdeState", "originator", "transmittalId" };

        /// <summary>Build from the "docsAttributeNames" object. Keys not given keep their default;
        /// an unknown key, or two roles sharing one attribute name, is a FormatException — a
        /// typo would otherwise silently write to the default column.</summary>
        public static AccAttributeNames FromSettings(IReadOnlyDictionary<string, string> map)
        {
            var n = new AccAttributeNames();
            if (map == null) return n;
            foreach (var kv in map)
            {
                string k = (kv.Key ?? string.Empty).Trim();
                string v = (kv.Value ?? string.Empty).Trim();
                if (v.Length == 0) throw new FormatException($"'docsAttributeNames.{k}' is empty");
                switch (k.ToLowerInvariant())
                {
                    case "documentnumber": n.DocumentNumber = v; break;
                    case "suitability":    n.Suitability = v; break;
                    case "revision":       n.Revision = v; break;
                    case "cdestate":       n.CdeState = v; break;
                    case "originator":     n.Originator = v; break;
                    case "transmittalid":  n.TransmittalId = v; break;
                    default:
                        throw new FormatException($"'docsAttributeNames' key '{k}' is not one of {string.Join(", ", Keys)}");
                }
            }
            var dup = n.All().GroupBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (dup != null) throw new FormatException($"'docsAttributeNames' uses '{dup.Key}' for more than one attribute");
            return n;
        }

        public IEnumerable<string> All()
        {
            yield return DocumentNumber; yield return Suitability; yield return Revision;
            yield return CdeState; yield return Originator; yield return TransmittalId;
        }

        /// <summary>The definitions STING needs on a folder. Text ("string"), because a drop-list
        /// STING created would reject any code the admin had not pre-listed; Suitability and CDE
        /// State may ALSO be drop-downs the admin created — a value is then checked against the
        /// drop-list before anything is sent (AccDocsMetadata.BuildBatchBody).</summary>
        public IReadOnlyList<AccAttributeSpec> Specs() => new[]
        {
            new AccAttributeSpec(DocumentNumber, "string"),
            new AccAttributeSpec(Suitability,    "string", null, new[] { "array" }),
            new AccAttributeSpec(Revision,       "string"),
            new AccAttributeSpec(CdeState,       "string", null, new[] { "array" }),
            new AccAttributeSpec(Originator,     "string"),
            new AccAttributeSpec(TransmittalId,  "string"),
        };
    }

    /// <summary>What STING knows about one deliverable when it uploads it.</summary>
    public sealed class AccDocMetadataInput
    {
        /// <summary>ISO 19650 document number / identifier (e.g. KUT-PLN-ZZ-01-DR-A-0001).</summary>
        public string DocumentNumber { get; set; }
        /// <summary>Suitability code (S0..S7, A1.., B1.., CR, AB, AR); the CDE state is derived from it.</summary>
        public string Suitability { get; set; }
        /// <summary>Revision label (P01, C01, …).</summary>
        public string Revision { get; set; }
        /// <summary>STING transmittal id, when the upload is part of a transmittal. Optional.</summary>
        public string TransmittalId { get; set; }
        /// <summary>ISO 19650 originator code. Optional.</summary>
        public string Originator { get; set; }
    }

    /// <summary>The values to stamp, plus everything that was NOT stamped and why.</summary>
    public sealed class AccDocMetadataValues
    {
        /// <summary>Attribute NAME -> value. Only attributes that have a real value.</summary>
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Attribute names with no value supplied. Left UNSET on ACC — not written as "".</summary>
        public List<string> Omitted { get; } = new List<string>();
        /// <summary>Problems that make the stamp untrustworthy (unknown suitability, over-long value).
        /// A caller should not report the metadata as stamped while this is non-empty.</summary>
        public List<string> Problems { get; } = new List<string>();
        public bool IsClean => Problems.Count == 0;
    }

    /// <summary>The STING attribute set and its value builder.</summary>
    public static class AccDocsAttributeSet
    {
        // The DEFAULT names (AccAttributeNames.Default). A project may rename every one in
        // acc_settings.json "docsAttributeNames"; code that writes must use the policy's names.
        public const string DocumentNumber = "Document Number";
        public const string Suitability    = "Suitability";
        public const string Revision       = "Revision";
        public const string CdeState       = "CDE State";
        public const string Originator     = "Originator";
        public const string TransmittalId  = "STING Transmittal Id";

        /// <summary>ACC's documented max length for a text ("string") attribute value.</summary>
        public const int MaxStringLength = 255;

        /// <summary>The definitions STING needs on a folder under the DEFAULT names.</summary>
        public static readonly IReadOnlyList<AccAttributeSpec> All = AccAttributeNames.Default.Specs();

        /// <summary>Build attribute values under the default names.</summary>
        public static AccDocMetadataValues Build(AccDocMetadataInput input) => Build(input, AccAttributeNames.Default);

        /// <summary>Build attribute values for one deliverable, keyed by the project's attribute
        /// names. The suitability is normalised to its code and the CDE state derived from it;
        /// an unrecognised suitability writes NEITHER and is reported as a problem. Blank inputs
        /// are omitted (left unset on ACC), never written as empty strings.</summary>
        public static AccDocMetadataValues Build(AccDocMetadataInput input, AccAttributeNames names)
        {
            names ??= AccAttributeNames.Default;
            string DocumentNumber = names.DocumentNumber, Suitability = names.Suitability, Revision = names.Revision,
                   CdeState = names.CdeState, Originator = names.Originator, TransmittalId = names.TransmittalId;
            var r = new AccDocMetadataValues();
            if (input == null) { r.Problems.Add("no metadata input was supplied"); return r; }

            Put(r, DocumentNumber, input.DocumentNumber);
            Put(r, Revision, input.Revision);
            Put(r, Originator, input.Originator);
            Put(r, TransmittalId, input.TransmittalId);

            if (string.IsNullOrWhiteSpace(input.Suitability))
            {
                r.Omitted.Add(Suitability);
                r.Omitted.Add(CdeState);
                r.Problems.Add("no suitability code — the CDE state cannot be derived, so neither is stamped");
            }
            else
            {
                string code = Iso19650Suitability.ExtractCode(input.Suitability);
                string state = code.Length > 0 ? Iso19650Suitability.CdeStateFor(code) : null;
                if (string.IsNullOrEmpty(state))
                {
                    r.Omitted.Add(Suitability);
                    r.Omitted.Add(CdeState);
                    r.Problems.Add($"'{input.Suitability}' is not an ISO 19650 suitability code — neither suitability nor CDE state is stamped");
                }
                else
                {
                    r.Values[Suitability] = code;
                    r.Values[CdeState] = state;
                }
            }

            if (!r.Values.ContainsKey(DocumentNumber))
                r.Problems.Add("no document number — the upload would carry no ISO 19650 identifier");
            return r;
        }

        private static void Put(AccDocMetadataValues r, string name, string raw)
        {
            string v = (raw ?? string.Empty).Trim();
            if (v.Length == 0) { r.Omitted.Add(name); return; }
            if (v.Length > MaxStringLength)
            {
                // Truncating would stamp a different identifier than the drawing carries.
                r.Omitted.Add(name);
                r.Problems.Add($"{name} is {v.Length} characters; ACC text attributes hold at most {MaxStringLength} — not stamped rather than truncated");
                return;
            }
            r.Values[name] = v;
        }
    }
}
