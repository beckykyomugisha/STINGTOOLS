// ============================================================================
// AuthoringNoteMatcher.cs — text left in a family FOR THE AUTHOR, not the reader.
//
// WHY THIS EXISTS
//
// A screenshot of "STING - Duct Tag" in the Family Editor on 2026-09-17 showed a
// red text note in the family view, of the kind that ends "delete this note
// before saving". Propagation clones the master's contents into every target, so
// a note like that in the master rides into all 206 families and then prints on
// drawings. Nothing looks at it: it is not a parameter, not a label row, and not
// something any gate reads.
//
// WHAT THIS DOES
//
// Decides whether one piece of text is an instruction to the family's author
// rather than content for its reader. Revit-free so the decision is testable:
// the risk here is not failing to read a note, it is calling a legitimate note
// an instruction and nagging about it forever.
//
// The bar is deliberately high. A note only matches when it says, in so many
// words, that it is temporary — "delete this note", "remove before saving",
// "TODO", "FIXME". A note that merely begins "Note:" is a real annotation and
// must not match; drawings are full of them.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// Whether a text note is scaffolding the author meant to remove. Used to stop
    /// it being propagated into 206 families and printed on drawings.
    /// </summary>
    public static class AuthoringNoteMatcher
    {
        // Each entry has to be a phrase that only appears in text written for the
        // author. "before saving" alone does not qualify — a specification note can
        // legitimately say "verify before saving the record copy" — so the
        // delete/remove verb is required with it.
        private static readonly string[] SelfDeclaredTemporary =
        {
            "delete this note",
            "delete this text",
            "delete note",
            "remove this note",
            "remove this text",
            "delete before sav",
            "remove before sav",
            "delete prior to sav",
            "todo:",
            "todo ",
            "fixme",
            "xxx:",
            "placeholder - ",
            "placeholder text",
            "temporary note",
            "author note",
            "authoring note",
        };

        // Requires BOTH halves: a verb and a deadline.
        private static readonly string[] DeleteVerbs = { "delete", "remove", "erase" };
        private static readonly string[] Deadlines = { "before saving", "before sav", "before issue", "before propagat" };

        /// <summary>
        /// True when this text reads as an instruction to whoever is editing the
        /// family, rather than as annotation for whoever reads the drawing.
        /// </summary>
        public static bool LooksLikeAuthoringInstruction(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().ToLowerInvariant();

            if (SelfDeclaredTemporary.Any(phrase => t.Contains(phrase))) return true;

            bool hasVerb = DeleteVerbs.Any(v => t.Contains(v));
            bool hasDeadline = Deadlines.Any(d => t.Contains(d));
            return hasVerb && hasDeadline;
        }

        /// <summary>
        /// The matching notes, each trimmed to one readable line so a dialog or a
        /// report cell can carry it. Order is preserved; duplicates are collapsed
        /// because a family often repeats the same note per view.
        /// </summary>
        public static List<string> Flag(IEnumerable<string> notes, int maxChars = 80)
        {
            var hits = new List<string>();
            if (notes == null) return hits;
            if (maxChars < 8) maxChars = 8;

            foreach (string note in notes)
            {
                if (!LooksLikeAuthoringInstruction(note)) continue;
                string one = string.Join(" ", (note ?? "")
                    .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Trim())
                    .Where(part => part.Length > 0));
                if (one.Length > maxChars) one = one.Substring(0, maxChars - 1) + "…";
                if (!hits.Contains(one)) hits.Add(one);
            }
            return hits;
        }
    }
}
