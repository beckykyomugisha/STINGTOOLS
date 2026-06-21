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
    }
}
