using System;

namespace StingTools.Core
{
    /// <summary>
    /// One row of a Revit keynote table: <c>Key &lt;TAB&gt; Text [&lt;TAB&gt; Parent]</c>.
    ///
    /// KeynoteSync wrote every row as <c>"{key}\t\t{name}"</c> — key, EMPTY text, and the
    /// name in the PARENT column — so every keynote it produced printed a blank and
    /// pointed at a parent key that does not exist. Built here so the column order is
    /// written down once and tested.
    /// </summary>
    public static class KeynoteTableFormat
    {
        public static string Row(string key, string text, string parent = null)
        {
            string k = Clean(key), t = Clean(text), p = Clean(parent);
            if (k.Length == 0) return null;
            return p.Length == 0 ? k + "\t" + t : k + "\t" + t + "\t" + p;
        }

        /// <summary>Tabs and line breaks would split the row; they become spaces.</summary>
        private static string Clean(string s)
            => (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
