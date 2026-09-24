using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>
    /// Which tag rules may run on one view, and which elements each may skip.
    ///
    /// The runner used to tag each CATEGORY at most once per view, and to skip any
    /// element that carried ANY tag. Both were right while one category had one tag,
    /// and both silently dropped the second of two rules on a category — a Room Tag
    /// plus a Pressure Regime Tag on Rooms, or two Specialty Equipment rules split
    /// by familyMatch onto a Pendant Tag and a Bedhead Trunking Tag. The healthcare
    /// drawing types cannot be expressed without that.
    ///
    /// A rule is PRIMARY when it names no tag family of its own (it uses the pack
    /// map) and SPECIALIST when it does. The rules:
    ///   • Dedup: two rules are the same work when category, rule tag family and
    ///     familyMatch all agree. That keeps the old collapse of Rooms AutoTag /
    ///     AutoTagRoomName / AutoTagRoomNumber onto one pass.
    ///   • A specialist rule skips an element only if it already carries a tag of
    ///     the family this rule resolved to — so it adds its tag beside the
    ///     category's ordinary one, and never twice.
    ///   • A primary rule skips an element that carries any tag other than one
    ///     placed by a specialist family of this pack — so a user's own tag still
    ///     stops STING adding a second, but a pressure-regime tag does not stop the
    ///     room tag, whichever ran first.
    /// Family names compare on their base: a size variant ("… 2.5mm") is the same
    /// tag as its base.
    /// </summary>
    public static class TagRuleIdentity
    {
        /// <summary>The key two rules must share to be one piece of work.</summary>
        public static string DedupKey(long categoryId, string ruleTagFamily, string familyMatch)
            => categoryId + "|" + BaseFamily(ruleTagFamily).ToUpperInvariant() + "|" + (familyMatch ?? "").Trim();

        /// <summary>"STING - Door Tag 2.5mm" → "STING - Door Tag". Anything else unchanged.</summary>
        public static string BaseFamily(string familyName)
        {
            var f = (familyName ?? "").Trim();
            int sp = f.LastIndexOf(' ');
            if (sp > 0 && TagSizeVariant.ParseToken(f.Substring(sp + 1)) != null)
                return f.Substring(0, sp).TrimEnd();
            return f;
        }

        /// <summary>Base names of every family a rule of this pack asks for by name.</summary>
        public static HashSet<string> SpecialistFamilies(IEnumerable<string> ruleTagFamilies)
            => new HashSet<string>(
                (ruleTagFamilies ?? Enumerable.Empty<string>())
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Select(BaseFamily),
                StringComparer.OrdinalIgnoreCase);

        /// <param name="existingFamilies">Families of the tags already on the element in this view.</param>
        /// <param name="isSpecialistRule">The rule names its own tag family.</param>
        /// <param name="resolvedFamily">The family this rule is about to place.</param>
        /// <param name="specialistFamilies">From <see cref="SpecialistFamilies"/>.</param>
        public static bool ShouldSkip(IEnumerable<string> existingFamilies, bool isSpecialistRule,
            string resolvedFamily, ISet<string> specialistFamilies)
        {
            if (existingFamilies == null) return false;
            var resolved = BaseFamily(resolvedFamily);
            foreach (var raw in existingFamilies)
            {
                var f = BaseFamily(raw);
                if (f.Length > 0 && string.Equals(f, resolved, StringComparison.OrdinalIgnoreCase)) return true;
                if (isSpecialistRule) continue;
                // An unreadable family ("") is someone's tag all the same.
                if (specialistFamilies == null || !specialistFamilies.Contains(f)) return true;
            }
            return false;
        }
    }
}
