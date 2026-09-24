using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StingTools.Commands.Electrical.Export
{
    /// <summary>
    /// Allocates document-unique rdf:ID values. Revit circuit numbers ("1", "2" …)
    /// repeat on every panel, so an ID built from the circuit number alone
    /// collided across panels; IDs are now scoped (panel + circuit), made
    /// NCName-safe (must not start with a digit) and de-duplicated with a suffix
    /// when two different sources still sanitise to the same text.
    /// Revit-free for unit testing.
    /// </summary>
    public sealed class CimIdAllocator
    {
        private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _byKey = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The ID for <paramref name="key"/> (same key → same ID, so references
        /// resolve), built from <paramref name="parts"/>.
        /// </summary>
        public string For(string key, params string[] parts)
        {
            if (key != null && _byKey.TryGetValue(key, out var existing)) return existing;
            string baseId = "_" + Regex.Replace(string.Join("_", parts ?? Array.Empty<string>()), @"[^A-Za-z0-9_\-.]", "_");
            if (baseId == "_") baseId = "_ID";
            string id = baseId;
            for (int n = 2; !_used.Add(id); n++) id = $"{baseId}_{n}";
            if (key != null) _byKey[key] = id;
            return id;
        }
    }
}
