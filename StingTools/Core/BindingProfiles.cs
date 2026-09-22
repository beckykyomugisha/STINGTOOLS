// BindingProfiles - named, opt-in parameter->category binding sets.
//
// Revit-free on purpose: the parse and the merge are pure data, so they can be
// tested against the shipped profile file without opening Revit. Turning the
// merged names into BuiltInCategory values stays in SharedParamGuids.
//
// WHY THIS EXISTS
//
// LoadSharedParamsCommand binds each parameter to ITS OWN category set, and
// says why at LoadSharedParamsCommand.cs:395 - "This stops cross-discipline
// leakage (params showing on Ducts, etc.)". That is the right default and it is
// load-bearing: the corporate CATEGORY_BINDINGS.csv is deliberately narrow.
//
// But some parameters legitimately need a WIDE category set in the minority of
// projects that use a feature. Measured 2026-09-21: the three Multi-Category
// LPS reuse tag families display 13 ELC_LPS_* parameters across 15 host
// categories, and 91 of those 93 bindings do not exist. Those tags therefore
// place and render BLANK - the "looks correct, does nothing" failure this
// codebase specialises in.
//
// Adding the 93 rows to CATEGORY_BINDINGS.csv would fix the tags and put 13
// electrical parameters on every Roof, Wall, Fascia, Gutter, Soffit, Foundation
// and rebar element in EVERY project, most of which have no lightning
// protection system at all. That is the leakage the design exists to prevent.
//
// So: corporate baseline stays narrow, and a project that needs the wide set
// switches on a named profile. Same corporate-baseline + project-override shape
// as drawing types, visibility presets, MEP sizing rules, climate data and tag
// schemes - nothing new to learn, and one tested definition instead of 93 rows
// hand-copied per project.
//
// A project that enables nothing is byte-identical to before this existed.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core
{
    /// <summary>One parameter and the categories a profile binds it to.</summary>
    public class BindingProfileEntry
    {
        [JsonProperty("param")]
        public string Param { get; set; }

        [JsonProperty("categories")]
        public List<string> Categories { get; set; }
    }

    /// <summary>A named set of additional bindings a project can switch on.</summary>
    public class BindingProfile
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>Tag families that render blank without this profile.</summary>
        [JsonProperty("requiredBy")]
        public List<string> RequiredBy { get; set; }

        [JsonProperty("bindings")]
        public List<BindingProfileEntry> Bindings { get; set; }
    }

    /// <summary>Root of STING_BINDING_PROFILES.json.</summary>
    public class BindingProfileLibrary
    {
        [JsonProperty("profiles")]
        public List<BindingProfile> Profiles { get; set; }
    }

    /// <summary>What a project has switched on: <c>{ "enabled": ["id", ...] }</c>.</summary>
    public class EnabledBindingProfiles
    {
        [JsonProperty("enabled")]
        public List<string> Enabled { get; set; }
    }

    /// <summary>Parses profile JSON and merges profiles over a baseline.</summary>
    public static class BindingProfiles
    {
        /// <summary>
        /// Parses the corporate profile library. Returns an empty library rather
        /// than null on bad JSON, and reports why through <paramref name="error"/>
        /// so the caller can log it - a silently empty library would look exactly
        /// like "no profiles are enabled".
        /// </summary>
        public static BindingProfileLibrary ParseLibrary(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "profile library is empty";
                return new BindingProfileLibrary { Profiles = new List<BindingProfile>() };
            }

            try
            {
                var lib = JsonConvert.DeserializeObject<BindingProfileLibrary>(json)
                          ?? new BindingProfileLibrary();
                if (lib.Profiles == null) lib.Profiles = new List<BindingProfile>();
                return lib;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return new BindingProfileLibrary { Profiles = new List<BindingProfile>() };
            }
        }

        /// <summary>
        /// Parses a project's enabled list. An unreadable file yields nothing
        /// enabled, which is the safe direction: the project behaves as it did
        /// before profiles existed, rather than gaining bindings nobody asked for.
        /// </summary>
        public static List<string> ParseEnabled(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();

            try
            {
                var e = JsonConvert.DeserializeObject<EnabledBindingProfiles>(json);
                return e?.Enabled?.Where(s => !string.IsNullOrWhiteSpace(s))
                                  .Select(s => s.Trim())
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .ToList()
                       ?? new List<string>();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return new List<string>();
            }
        }

        /// <summary>
        /// Merges the enabled profiles over <paramref name="baseline"/>, returning a
        /// NEW map. Purely additive per parameter: a profile can widen a category
        /// set, never narrow one, so enabling a profile can hide nothing that was
        /// visible before. Baseline categories keep their order and come first.
        ///
        /// <para><paramref name="unknown"/> collects ids the project enabled that
        /// the library does not define - a typo in an enabled list must be
        /// reported, not treated as "nothing to add".</para>
        /// </summary>
        public static Dictionary<string, List<string>> Merge(
            Dictionary<string, List<string>> baseline,
            BindingProfileLibrary library,
            IEnumerable<string> enabledIds,
            out List<string> unknown,
            out int addedBindings)
        {
            unknown = new List<string>();
            addedBindings = 0;

            var merged = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (baseline != null)
                foreach (var kv in baseline)
                    merged[kv.Key] = kv.Value == null ? new List<string>() : new List<string>(kv.Value);

            var ids = (enabledIds ?? Enumerable.Empty<string>())
                      .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (ids.Count == 0) return merged;

            var byId = new Dictionary<string, BindingProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in library?.Profiles ?? new List<BindingProfile>())
                if (!string.IsNullOrWhiteSpace(p?.Id) && !byId.ContainsKey(p.Id))
                    byId[p.Id] = p;

            foreach (string id in ids)
            {
                if (!byId.TryGetValue(id.Trim(), out var profile))
                {
                    unknown.Add(id.Trim());
                    continue;
                }

                foreach (var entry in profile.Bindings ?? new List<BindingProfileEntry>())
                {
                    if (string.IsNullOrWhiteSpace(entry?.Param) || entry.Categories == null) continue;

                    string param = entry.Param.Trim();
                    if (!merged.TryGetValue(param, out var cats))
                    {
                        cats = new List<string>();
                        merged[param] = cats;
                    }

                    foreach (string cat in entry.Categories)
                    {
                        if (string.IsNullOrWhiteSpace(cat)) continue;
                        string c = cat.Trim();
                        if (cats.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase))) continue;
                        cats.Add(c);
                        addedBindings++;
                    }
                }
            }

            return merged;
        }
    }
}
