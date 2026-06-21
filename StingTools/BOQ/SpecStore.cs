// ══════════════════════════════════════════════════════════════════════════
//  SpecStore.cs — Phase H1 (KUT lifecycle, max automation).
//
//  Pure, host-free reader for the SpecLink section store at
//  <project>/_BIM_COORD/speclink/sections.json — a normalised CSI section →
//  { title, description, unit } map produced by SpecLink_ImportFolder (Phase H4)
//  from an exported RIB/Deltek SpecLink project manual.
//
//  The BOQ engine consumes it so the SPEC writes the bill: when a priced element
//  carries a CSI section that the store describes, the line description becomes the
//  issued spec text (single source of truth) instead of a generated NRM2 template,
//  and the spec's preferred unit is carried as a measurement-basis advisory.
//
//  Zero Autodesk.Revit.* dependencies on purpose (unit-tested in StingTools.Boq.Tests).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core.Classification;

namespace StingTools.BOQ
{
    public sealed class SpecSection
    {
        public string Section = "";       // normalised CSI section
        public string Title = "";
        public string Description = "";    // Part 2/3 narrative — drives the BOQ line text
        public string Unit = "";           // spec measurement basis (m2/m3/m/kg/each)
    }

    public static class SpecStore
    {
        /// <summary>Parse the sections.json content into a normalised-section → SpecSection
        /// map. Accepts either a JSON object keyed by section, or an array of
        /// { section, title, description, unit } rows. Tolerant of missing fields and
        /// bad JSON (returns whatever parsed; empty on total failure).</summary>
        public static Dictionary<string, SpecSection> Parse(string json)
        {
            var d = new Dictionary<string, SpecSection>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return d;
            try
            {
                var tok = JToken.Parse(json);
                if (tok is JObject obj)
                {
                    // { "sections": [...] } or a bare section-keyed object.
                    if (obj["sections"] is JArray arr) AddArray(d, arr);
                    else foreach (var p in obj.Properties()) AddRow(d, p.Name, p.Value as JObject);
                }
                else if (tok is JArray topArr) AddArray(d, topArr);
            }
            catch { /* tolerant — partial/empty store is fine */ }
            return d;
        }

        private static void AddArray(Dictionary<string, SpecSection> d, JArray arr)
        {
            foreach (var t in arr)
                if (t is JObject o) AddRow(d, (string)o["section"], o);
        }

        private static void AddRow(Dictionary<string, SpecSection> d, string section, JObject o)
        {
            if (o == null) return;
            string sec = CsiMasterFormat.NormalizeSection(section ?? (string)o["section"]);
            if (sec.Length == 0 || d.ContainsKey(sec)) return;
            d[sec] = new SpecSection
            {
                Section = sec,
                Title = ((string)o["title"] ?? "").Trim(),
                Description = ((string)o["description"] ?? (string)o["text"] ?? "").Trim(),
                Unit = ((string)o["unit"] ?? "").Trim(),
            };
        }

        /// <summary>Look up the spec section for a (possibly un-normalised) CSI section.</summary>
        public static SpecSection Get(Dictionary<string, SpecSection> store, string csiSection)
        {
            if (store == null || string.IsNullOrWhiteSpace(csiSection)) return null;
            return store.TryGetValue(CsiMasterFormat.NormalizeSection(csiSection), out var s) ? s : null;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Phase H4 — manual-CSV import. SpecLink_ImportFolder feeds an exported
        //  project-manual CSV (the table of contents, optionally with a description
        //  / unit column) through ParseManualCsv → Serialize → sections.json, which
        //  is then what Parse() reads at BOQ-build time. Header-aware (maps Section /
        //  Title / Description|Text / Unit by name, case-insensitive); falls back to
        //  positional (col0=section, col1=title) when the header is absent.
        // ──────────────────────────────────────────────────────────────────────
        public static Dictionary<string, SpecSection> ParseManualCsv(IEnumerable<string> lines)
        {
            var d = new Dictionary<string, SpecSection>(StringComparer.Ordinal);
            int iSec = 0, iTitle = 1, iDesc = -1, iUnit = -1;
            bool headerSeen = false;
            foreach (var raw in lines ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string line = raw.TrimEnd('\r');
                if (line.TrimStart().StartsWith("#")) continue;
                var f = SplitCsv(line);
                if (f.Count == 0) continue;

                if (!headerSeen)
                {
                    // Treat the first non-comment row as a header iff it names a section column.
                    var lower = f.Select(x => x.Trim().ToLowerInvariant()).ToList();
                    int hs = lower.FindIndex(x => x == "section" || x == "csi" || x == "csisection");
                    if (hs >= 0)
                    {
                        iSec = hs;
                        iTitle = lower.FindIndex(x => x == "title" || x == "name");
                        iDesc = lower.FindIndex(x => x == "description" || x == "text" || x == "spec" || x == "body");
                        iUnit = lower.FindIndex(x => x == "unit" || x == "uom");
                        if (iTitle < 0) iTitle = 1;
                        headerSeen = true;
                        continue;   // consume the header row
                    }
                    headerSeen = true;  // no header → positional, and this row IS data
                }

                string sec = CsiMasterFormat.NormalizeSection(Field(f, iSec));
                if (sec.Length == 0 || d.ContainsKey(sec)) continue;
                d[sec] = new SpecSection
                {
                    Section = sec,
                    Title = Field(f, iTitle),
                    Description = Field(f, iDesc),
                    Unit = Field(f, iUnit),
                };
            }
            return d;
        }

        /// <summary>Serialize a store to the array-shaped sections.json that Parse reads.</summary>
        public static string Serialize(Dictionary<string, SpecSection> store)
        {
            var arr = new JArray();
            foreach (var s in (store ?? new Dictionary<string, SpecSection>()).Values
                         .OrderBy(v => v.Section, StringComparer.Ordinal))
            {
                arr.Add(new JObject
                {
                    ["section"] = s.Section,
                    ["title"] = s.Title ?? "",
                    ["description"] = s.Description ?? "",
                    ["unit"] = s.Unit ?? "",
                });
            }
            return new JObject { ["sections"] = arr }.ToString(Formatting.Indented);
        }

        private static string Field(IReadOnlyList<string> f, int i) =>
            i >= 0 && i < f.Count ? (f[i] ?? "").Trim() : "";

        /// <summary>Minimal RFC-4180-ish splitter (quoted fields, doubled quotes).</summary>
        private static List<string> SplitCsv(string line)
        {
            var outp = new List<string>();
            if (line == null) return outp;
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else q = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == ',') { outp.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            outp.Add(sb.ToString());
            return outp;
        }
    }
}
