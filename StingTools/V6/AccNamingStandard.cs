// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccNamingStandard.cs
//
// File names and ACC Docs, checked BEFORE any bytes are sent.
//
// Two problems this exists for:
//
//  1. ACC matches an existing item by FILE NAME (AccModelUpload.FindItemIdAsync). A name that
//     carries "-{Suitability}-{Revision}" (the Export Centre's 9-field ISO preset) is a
//     different name at every revision, so P02 becomes a NEW item instead of version 2 of the
//     P01 item — the item's history, markups and review lineage are split across items. ISO
//     19650 carries suitability and revision as metadata; KUT's BEP fixes a 7-field name
//     (Project-Originator-Volume-Level-Type-Role-Number). NameEmbedsStatus detects the
//     9-field shape so an upload into a 7-field project can refuse it with the reason.
//
//  2. A folder may enforce an ACC "naming standard". The folder's naming-standard ids are in
//     the Data Management folder record (data.attributes.extension.data.namingStandardIds);
//     the standard itself is GET /bim360/docs/v1/projects/{id}/naming-standards/{id}.
//
// NOT CONFIRMED against a live tenant: the naming-standard response shape. It is parsed
// defensively (a "delimiter" and a "fields" array wherever they appear). What is checked as a
// hard rule is only what survives an unknown shape: the delimiter and the NUMBER of fields.
// Anything this cannot interpret is REPORTED as "not validated", never guessed into a pass or
// a refusal.
//
// Revit-free and log-free: linked into StingTools.Acc.Tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core.Drawing;

namespace StingTools.V6
{
    /// <summary>One field of an ACC naming standard as far as it could be read.</summary>
    public sealed class AccNamingField
    {
        public string Name { get; set; } = string.Empty;
        /// <summary>Allowed values when the standard lists them; empty = free text / unknown.</summary>
        public List<string> Values { get; } = new List<string>();
    }

    /// <summary>An ACC naming standard, interpreted as far as the response allowed.</summary>
    public sealed class AccNamingStandardSpec
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>The delimiter character(s); empty when the response did not say.</summary>
        public string Delimiter { get; set; } = string.Empty;
        public List<AccNamingField> Fields { get; } = new List<AccNamingField>();
        /// <summary>True when both the delimiter and the field list were found — the minimum to
        /// validate a name. False means "could not interpret", which is reported, not guessed.</summary>
        public bool Interpretable => Delimiter.Length > 0 && Fields.Count > 0;
    }

    /// <summary>The answer for one file name against one standard.</summary>
    public sealed class AccNameCheck
    {
        /// <summary>True = conforms, false = does not, null = could not be decided.</summary>
        public bool? Conforms { get; set; }
        public string Detail { get; set; } = string.Empty;
        /// <summary>Soft findings (a value not in a listed set) — reported, not refused, because
        /// the value-list shape is unconfirmed.</summary>
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class AccNamingStandard
    {
        /// <summary>
        /// Does this file name END in "-{suitability}-{revision}"? With the values known the
        /// check is exact; without them (null/empty) the last two fields are tested for the
        /// shape — an ISO 19650 suitability code followed by a revision-like label (P01, C02,
        /// or the NOREV marker) — after the 7th field.
        /// </summary>
        public static bool NameEmbedsStatus(string fileNameOrPath, string suitability, string revision)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrPath)) return false;
            string stem;
            try { stem = Path.GetFileNameWithoutExtension(fileNameOrPath.Trim()); }
            catch (ArgumentException) { stem = fileNameOrPath; }
            var t = stem.Split('-');
            if (t.Length < 3) return false;
            string last = t[t.Length - 1].Trim(), prev = t[t.Length - 2].Trim();

            string s = Iso19650Suitability.ExtractCode(suitability ?? string.Empty);
            string r = (revision ?? string.Empty).Trim();
            if (s.Length > 0 && r.Length > 0 &&
                string.Equals(prev, s, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(last, r, StringComparison.OrdinalIgnoreCase))
                return true;

            bool prevIsStatus = prev.Equals(ExportIsoFields.NotSetSuitability, StringComparison.OrdinalIgnoreCase) ||
                                Iso19650Suitability.IsKnown(prev.ToUpperInvariant());
            bool lastIsRevision = last.Equals(ExportIsoFields.NotSetRevision, StringComparison.OrdinalIgnoreCase) ||
                                  System.Text.RegularExpressions.Regex.IsMatch(last, @"^[PCpc]\d{2,3}(\.\d+)?$");
            return t.Length >= 9 && prevIsStatus && lastIsRevision;
        }

        /// <summary>The naming-standard ids on a Data Management folder record. Empty when the
        /// folder has none; null when the body is not a folder record at all.</summary>
        public static List<string> ParseFolderNamingStandardIds(string folderJson)
        {
            JToken root;
            try { root = JToken.Parse(folderJson ?? string.Empty); }
            catch (JsonException) { return null; }
            var data = root?["data"];
            if (data == null || data.Type != JTokenType.Object) return null;
            var ids = data["attributes"]?["extension"]?["data"]?["namingStandardIds"] as JArray;
            return ids == null ? new List<string>()
                : ids.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }

        /// <summary>Interpret a naming-standard response as far as it goes. Never throws.</summary>
        public static AccNamingStandardSpec ParseStandard(string json, string id = "")
        {
            var spec = new AccNamingStandardSpec { Id = id ?? string.Empty };
            JToken root;
            try { root = JToken.Parse(json ?? string.Empty); }
            catch (JsonException) { return spec; }

            spec.Name = FindString(root, "name") ?? string.Empty;
            string delim = FindString(root, "delimiter") ?? FindString(root, "separator");
            spec.Delimiter = NormaliseDelimiter(delim);

            var fields = FindArray(root, "fields");
            if (fields != null)
                foreach (var f in fields.OfType<JObject>())
                {
                    var field = new AccNamingField
                    {
                        Name = (string)(f["name"] ?? f["label"] ?? f["title"]) ?? string.Empty,
                    };
                    var vals = (f["values"] ?? f["options"] ?? f["allowedValues"]) as JArray;
                    if (vals != null)
                        foreach (var v in vals)
                        {
                            string s = v.Type == JTokenType.Object
                                ? (string)(v["value"] ?? v["code"] ?? v["name"])
                                : v.ToString();
                            if (!string.IsNullOrWhiteSpace(s)) field.Values.Add(s.Trim());
                        }
                    string fixedValue = f["value"]?.Type == JTokenType.String ? (string)f["value"] : null;
                    if (field.Values.Count == 0 && !string.IsNullOrWhiteSpace(fixedValue)) field.Values.Add(fixedValue.Trim());
                    spec.Fields.Add(field);
                }
            return spec;
        }

        /// <summary>Validate a file name. Hard rule: the delimiter splits the name into exactly
        /// as many fields as the standard has. A value outside a listed set is a warning only.</summary>
        public static AccNameCheck Validate(AccNamingStandardSpec spec, string fileNameOrPath)
        {
            var c = new AccNameCheck();
            if (spec == null || !spec.Interpretable)
            {
                c.Detail = $"the ACC naming standard{(string.IsNullOrEmpty(spec?.Name) ? "" : " '" + spec.Name + "'")} " +
                           "could not be interpreted (its delimiter or field list was not found), so the name was NOT validated";
                return c;
            }
            string stem;
            try { stem = Path.GetFileNameWithoutExtension((fileNameOrPath ?? string.Empty).Trim()); }
            catch (ArgumentException) { stem = fileNameOrPath ?? string.Empty; }
            var parts = stem.Split(new[] { spec.Delimiter }, StringSplitOptions.None);
            string label = string.IsNullOrEmpty(spec.Name) ? "the folder's naming standard" : $"naming standard '{spec.Name}'";
            if (parts.Length != spec.Fields.Count)
            {
                c.Conforms = false;
                c.Detail = $"'{stem}' has {parts.Length} field(s) separated by '{spec.Delimiter}', but {label} has " +
                           $"{spec.Fields.Count} ({string.Join(spec.Delimiter, spec.Fields.Select(f => string.IsNullOrEmpty(f.Name) ? "?" : f.Name))})";
                return c;
            }
            for (int i = 0; i < parts.Length; i++)
            {
                var f = spec.Fields[i];
                if (f.Values.Count > 0 && !f.Values.Contains(parts[i], StringComparer.Ordinal))
                    c.Warnings.Add($"field {i + 1}{(string.IsNullOrEmpty(f.Name) ? "" : " (" + f.Name + ")")} is '{parts[i]}', " +
                                   "which is not in the value list read from the standard — ACC may reject it");
            }
            c.Conforms = true;
            c.Detail = $"'{stem}' has the {spec.Fields.Count} fields {label} requires";
            return c;
        }

        internal static string NormaliseDelimiter(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            switch (raw.Trim().ToUpperInvariant())
            {
                case "-": case "HYPHEN": case "DASH": return "-";
                case "_": case "UNDERSCORE": return "_";
                case ".": case "PERIOD": case "DOT": return ".";
                case "SPACE": return " ";
                default: return raw.Length == 1 ? raw : string.Empty;   // unknown word: not guessed
            }
        }

        private static string FindString(JToken t, string key)
        {
            if (t is JObject o)
            {
                if (o[key]?.Type == JTokenType.String) return (string)o[key];
                foreach (var p in o.Properties())
                {
                    if (p.Name == "fields") continue;          // field names are not the standard's
                    var s = FindString(p.Value, key);
                    if (s != null) return s;
                }
            }
            else if (t is JArray a)
                foreach (var x in a) { var s = FindString(x, key); if (s != null) return s; }
            return null;
        }

        private static JArray FindArray(JToken t, string key)
        {
            if (t is JObject o)
            {
                if (o[key] is JArray arr) return arr;
                foreach (var p in o.Properties()) { var r = FindArray(p.Value, key); if (r != null) return r; }
            }
            else if (t is JArray a)
                foreach (var x in a) { var r = FindArray(x, key); if (r != null) return r; }
            return null;
        }
    }
}
