// StingTools — the ONE rule for an instance value that follows its seed type.
// Revit-free; unit-tested by StingTools.Tags.Tests.
//
// A seed declares some INSTANCE parameters per type ("followsType": the product
// code, a gas, a fire rating). Revit treats a type's value of an instance parameter
// as the default a NEW instance receives, so an element that changes type keeps the
// old value. Two paths change an element's type behind the user's back or in front
// of it — a type swap (SeedTypeSwapUpdater) and a type rename/merge on Build Seeds
// (SeedTypeMigrator) — and both ask this rule what to write.
//
// The rule: write the NEW type's declared value, but only over a value nobody chose.
// "Nobody chose it" means the instance holds nothing, or holds a value the seed
// itself put there (the old type's declared value, when the caller knows the old
// type; otherwise any value the seed declares for that parameter). Anything else was
// typed by a user or written by a command, and survives. When the new type declares
// nothing, the same condition clears it.
//
// Numbers are compared by value ("630" == "630.0"), so a numeric parameter read back
// from Revit as a double matches the string the seed declares.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core.Symbols
{
    public static class SeedFollowTypeRule
    {
        /// <summary>
        /// True when the instance value must change after a type change, with the value to
        /// write in <paramref name="write"/> ("" clears it).
        /// </summary>
        /// <param name="current">The instance's value now (null/blank = empty).</param>
        /// <param name="seedValues">Values the seed put there: the old type's declared value,
        /// or — when the old type is unknown — every value the seed declares for the parameter.</param>
        /// <param name="newTypeValue">What the new type declares; null/blank = nothing.</param>
        public static bool Decide(string current, IEnumerable<string> seedValues, string newTypeValue, out string write)
        {
            write = null;
            string cur = Normalize(current);
            string target = Normalize(newTypeValue);
            if (cur == target) return false;                     // already right: no write

            bool untouched = cur.Length == 0
                || (seedValues ?? Enumerable.Empty<string>()).Any(v => Normalize(v) == cur);
            if (!untouched) return false;                        // a user's value survives

            write = (newTypeValue ?? "").Trim();
            return true;
        }

        /// <summary>Trimmed text; a number is reduced to its canonical invariant form.</summary>
        public static string Normalize(string value)
        {
            string s = (value ?? "").Trim();
            if (s.Length == 0) return s;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                && !double.IsNaN(d) && !double.IsInfinity(d))
                return d.ToString("R", CultureInfo.InvariantCulture);
            return s;
        }
    }

    /// <summary>How a followsType parameter stores its value (Revit's StorageType, restated Revit-free).</summary>
    public enum FollowStorage { Text, Integer, Double, ElementId, Other }

    /// <summary>What <see cref="SeedFollowTypeWrite.Plan"/> tells the Revit layer to do.</summary>
    public enum FollowWriteAction
    {
        /// <summary>Parameter.Set(string).</summary>
        SetText,
        /// <summary>Parameter.Set(int) with the parsed value.</summary>
        SetInteger,
        /// <summary>Parameter.Set(double) with the parsed value (unitless Number only).</summary>
        SetDouble,
        /// <summary>Parameter.ClearValue(): a numeric or id value has no "" to write.</summary>
        Clear,
        /// <summary>Nothing to do: a clear on a parameter that already has no value.</summary>
        AlreadyClear,
        /// <summary>The value cannot be written to this storage (unparseable, unit-bearing, id).</summary>
        Invalid,
    }

    /// <summary>
    /// The storage → write mapping for a followsType decision. A "clear" (write == "")
    /// used to be parsed as a number on an Integer / Double parameter, so it always
    /// failed: the value was never cleared and every swap logged a warning. A clear on
    /// numeric or id storage is now Parameter.ClearValue() when the parameter holds a
    /// value, and nothing when it does not; text is still cleared by writing "".
    /// </summary>
    public static class SeedFollowTypeWrite
    {
        public static FollowWriteAction Plan(FollowStorage storage, bool unitless, bool hasValue, string value,
            out int intValue, out double doubleValue)
        {
            intValue = 0; doubleValue = 0;
            var v = (value ?? "").Trim();
            if (storage == FollowStorage.Text) return FollowWriteAction.SetText;
            if (storage == FollowStorage.Other) return FollowWriteAction.Invalid;
            // A unit-bearing Double (Length, ...) is never read or written from seed text:
            // the seed's value is not in Revit's internal units.
            if (storage == FollowStorage.Double && !unitless) return FollowWriteAction.Invalid;
            if (v.Length == 0)
                return hasValue ? FollowWriteAction.Clear : FollowWriteAction.AlreadyClear;
            switch (storage)
            {
                case FollowStorage.Integer:
                    if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                        return FollowWriteAction.SetInteger;
                    // "630.0" for an Integer parameter: a whole number written as a double.
                    if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                        && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
                    { intValue = (int)d; return FollowWriteAction.SetInteger; }
                    return FollowWriteAction.Invalid;
                case FollowStorage.Double:
                    return unitless && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue)
                        ? FollowWriteAction.SetDouble : FollowWriteAction.Invalid;
                default:
                    return FollowWriteAction.Invalid;   // an ElementId cannot be written from text
            }
        }
    }
}
