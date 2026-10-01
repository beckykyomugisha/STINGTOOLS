// StingTools — Drawing Template Manager · managed view-template names (Revit-free)
//
// DT-R11-F: ManagedTemplateSyncer named its templates "STING:{packId}:{ViewType}".
// Revit refuses a ':' in an element name (RevitNameRules), so `View.Name = …` threw
// on the first mint and no managed template was ever created in a real model —
// managed V/G was silently off. The name is also the IDENTITY of a managed template:
// the syncer's own lookup and sweep, the catalogue, the worksharing pre-check, the
// detach command, the drift detector and two shipped drawing types all key on it.
// So build, parse and recognise live here, once.
//
//   canonical  "STING MANAGED - {packId} - {ViewType}"   e.g. "STING MANAGED - corp-coordination - FloorPlan"
//   legacy     "STING:{packId}:{ViewType}"                recognised, never minted
//
// The prefix is deliberately NOT "STING - ": the syncer picks its SEED template by
// that prefix (ResolveSeed), and a managed template must never be mistaken for a
// seed, nor a seed for a managed template. Pack ids carry no spaces
// ([A-Za-z0-9_.-]+), so the " - " before the view type is unambiguous even though a
// pack id may itself contain '-'.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public static class ManagedTemplateNames
    {
        /// <summary>Prefix of every managed template name STING mints.</summary>
        public const string Prefix = "STING MANAGED - ";

        /// <summary>Prefix of the pre-DT-R11-F form, which Revit refuses; recognised only.</summary>
        public const string LegacyPrefix = "STING:";

        private const string Separator = " - ";

        private static readonly Regex PackIdRx = new Regex(@"^[A-Za-z0-9_.\-]+$", RegexOptions.Compiled);
        private static readonly Regex ViewTypeRx = new Regex(@"^[A-Za-z][A-Za-z0-9]+$", RegexOptions.Compiled);

        private static readonly Regex CanonicalRx = new Regex(
            @"^STING MANAGED - (?<pack>[A-Za-z0-9_.\-]+) - (?<vt>[A-Za-z][A-Za-z0-9]+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // GAP-O: exactly three colon-separated segments, so a user template such as
        // "STING:my-favourite" is not swept up.
        private static readonly Regex LegacyRx = new Regex(
            @"^STING:(?<pack>[A-Za-z0-9_.\-]+):(?<vt>[A-Za-z][A-Za-z0-9]+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>True when <paramref name="packId"/> can be part of a managed template name.</summary>
        public static bool IsValidPackId(string packId) => !string.IsNullOrEmpty(packId) && PackIdRx.IsMatch(packId);

        /// <summary>
        /// The canonical (Revit-legal) managed template name for a pack and a Revit
        /// ViewType name. Throws on a pack id or view type the grammar cannot carry,
        /// rather than mint a name that would not parse back.
        /// </summary>
        public static string Build(string packId, string viewType)
        {
            if (!IsValidPackId(packId))
                throw new ArgumentException($"Pack id '{packId}' cannot name a managed template (allowed: A-Z a-z 0-9 _ . -).", nameof(packId));
            if (string.IsNullOrEmpty(viewType) || !ViewTypeRx.IsMatch(viewType))
                throw new ArgumentException($"View type '{viewType}' cannot name a managed template.", nameof(viewType));
            return Prefix + packId + Separator + viewType;
        }

        /// <summary>The legacy "STING:{packId}:{ViewType}" form — for recognising old data only.</summary>
        public static string BuildLegacy(string packId, string viewType) => LegacyPrefix + packId + ":" + viewType;

        /// <summary>
        /// Parse a managed template name, canonical or legacy, into its pack id and
        /// view type. False for anything else (including a "STING - " seed template).
        /// </summary>
        public static bool TryParse(string name, out string packId, out string viewType)
        {
            packId = null; viewType = null;
            if (string.IsNullOrWhiteSpace(name)) return false;
            var s = name.Trim();
            var m = CanonicalRx.Match(s);
            if (!m.Success) m = LegacyRx.Match(s);
            if (!m.Success) return false;
            packId = m.Groups["pack"].Value;
            viewType = m.Groups["vt"].Value;
            return true;
        }

        /// <summary>True when <paramref name="name"/> parses as a managed template name of either form.</summary>
        public static bool IsManagedTemplateName(string name) => TryParse(name, out _, out _);

        /// <summary>
        /// True when <paramref name="name"/> claims the managed namespace — starts with
        /// either prefix — even if it does not parse. Used where such a name must be
        /// left to the syncer rather than created as an ordinary template.
        /// </summary>
        public static bool HasManagedPrefix(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var s = name.TrimStart();
            return s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                || s.StartsWith(LegacyPrefix, StringComparison.Ordinal);
        }

        /// <summary>True when <paramref name="name"/> is a managed template of <paramref name="packId"/> (either form).</summary>
        public static bool BelongsToPack(string name, string packId, out string viewType)
        {
            viewType = null;
            if (!TryParse(name, out var p, out var vt)) return false;
            if (!string.Equals(p, packId, StringComparison.OrdinalIgnoreCase)) return false;
            viewType = vt;
            return true;
        }

        /// <summary>
        /// The canonical form of a managed name (legacy → canonical); any other name
        /// is returned unchanged.
        /// </summary>
        public static string Canonical(string name)
            => TryParse(name, out var p, out var vt) ? Build(p, vt) : name;

        /// <summary>
        /// Names to look for in a model for <paramref name="name"/>, canonical first:
        /// a managed name yields its canonical and its legacy form; any other name
        /// yields itself.
        /// </summary>
        public static IReadOnlyList<string> Candidates(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Array.Empty<string>();
            if (!TryParse(name, out var p, out var vt)) return new[] { name.Trim() };
            return new[] { Build(p, vt), BuildLegacy(p, vt) };
        }

        /// <summary>True when the model element named <paramref name="revitName"/> is <paramref name="dataName"/> (case-insensitive, either form).</summary>
        public static bool Matches(string revitName, string dataName)
        {
            if (string.IsNullOrWhiteSpace(revitName)) return false;
            var r = revitName.Trim();
            foreach (var c in Candidates(dataName))
                if (string.Equals(r, c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
