// StingTools — which annotations did STING place, and for what?
//
// Three annotation passes worked this out after the fact, each differently,
// each with a residual:
//
//   * dimensions      — "a dimension in this view references this host". A
//                       dimension whose references Revit reports unavailable
//                       counts as unknown, so a re-run could duplicate it.
//   * match captions  — text shape + proximity to the boundary end. A boundary
//                       that moved left its old caption behind as an orphan.
//   * drainage ILs    — text shape + proximity to the pipe end. A pipe that
//                       moved left its old IL behind as an orphan.
//
// Now each is STAMPED when created (Extensible Storage, see
// Core/Storage/StingAnnotationProvenanceSchema.cs) with a producer and a key
// naming the thing it annotates. A re-run finds its own annotations exactly,
// wherever they have moved. The old heuristics remain as the fallback for
// annotations placed before stamping existed — they are read, never removed.
//
// This file is the Revit-free half: producer names and key composition, so
// the key format is one definition under test rather than string literals
// spread across three engines.

using System;

namespace StingTools.Core.Drawing
{
    public static class AnnotationProvenance
    {
        // Producers. A stamp is only ever matched against its own producer, so two
        // passes can annotate the same host without mistaking each other's work.
        public const string DimWallLength   = "Dim.WallLength";
        public const string DimOpeningChain = "Dim.OpeningChain";
        public const string DimColumnGrid   = "Dim.ColumnGrid";
        public const string DimGridChain    = "Dim.GridChain";
        public const string DimLevelChain   = "Dim.LevelChain";
        public const string DimMepRun       = "Dim.MepRun";
        public const string DimMepGridDrop  = "Dim.MepGridDrop";
        public const string MatchCaption    = "MatchLine.Caption";
        public const string DecoMatchlineFrame = "Deco.MatchlineFrame";
        public const string DrainageIl      = "Drainage.IL";

        private const char Sep = '|';

        /// <summary>
        /// Key for an annotation of <paramref name="hostUniqueId"/>, optionally one
        /// <paramref name="part"/> of it ("US", "DS", "GRAD", an axis, an end index).
        /// UniqueId, not ElementId: it survives worksharing and is what the
        /// annotation would still be about after a Save As.
        /// </summary>
        public static string Key(string hostUniqueId, string part = null)
        {
            if (string.IsNullOrWhiteSpace(hostUniqueId))
                throw new ArgumentException("A provenance key needs the host's UniqueId.", nameof(hostUniqueId));
            if (hostUniqueId.IndexOf(Sep) >= 0 || (part != null && part.IndexOf(Sep) >= 0))
                throw new ArgumentException($"Provenance key parts may not contain '{Sep}'.");
            return string.IsNullOrEmpty(part) ? hostUniqueId : hostUniqueId + Sep + part;
        }

        private const string LinkedPrefix = "link:";
        private const char LinkedSep = '/';

        /// <summary>
        /// DTW-102: the host part of a key for an annotation of an element in a linked
        /// model — the link instance's UniqueId and the linked element's UniqueId. The
        /// same element seen in the host, or through a second instance of the same link,
        /// is a different host, so a re-run recognises exactly its own linked dimensions.
        /// </summary>
        public static string LinkedHost(string linkInstanceUniqueId, string linkedElementUniqueId)
        {
            if (string.IsNullOrWhiteSpace(linkInstanceUniqueId) || string.IsNullOrWhiteSpace(linkedElementUniqueId))
                throw new ArgumentException("A linked provenance host needs the link instance's and the linked element's UniqueIds.");
            foreach (var p in new[] { linkInstanceUniqueId, linkedElementUniqueId })
                if (p.IndexOf(Sep) >= 0 || p.IndexOf(LinkedSep) >= 0)
                    throw new ArgumentException($"Linked provenance host parts may not contain '{Sep}' or '{LinkedSep}'.");
            return LinkedPrefix + linkInstanceUniqueId + LinkedSep + linkedElementUniqueId;
        }

        /// <summary>True when <paramref name="host"/> was built by <see cref="LinkedHost"/>.</summary>
        public static bool IsLinkedHost(string host) => TryParseLinkedHost(host, out _, out _);

        /// <summary>The link instance and linked element UniqueIds a linked host was built from.</summary>
        public static bool TryParseLinkedHost(string host, out string linkInstanceUniqueId, out string linkedElementUniqueId)
        {
            linkInstanceUniqueId = linkedElementUniqueId = null;
            if (string.IsNullOrEmpty(host) || !host.StartsWith(LinkedPrefix, StringComparison.Ordinal)) return false;
            var rest = host.Substring(LinkedPrefix.Length);
            int i = rest.IndexOf(LinkedSep);
            if (i <= 0 || i >= rest.Length - 1) return false;
            linkInstanceUniqueId = rest.Substring(0, i);
            linkedElementUniqueId = rest.Substring(i + 1);
            return true;
        }

        /// <summary>The host UniqueId a key was built from.</summary>
        public static string HostOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int i = key.IndexOf(Sep);
            return i < 0 ? key : key.Substring(0, i);
        }

        /// <summary>
        /// Warning for a run in which some stamps could not be written; null when
        /// every stamp landed. See <see cref="ProvenanceStampTally"/>.
        /// </summary>
        public static string StampFailureWarning(string pass, int attempted, int failed, string firstReason)
        {
            if (failed <= 0) return null;
            var reason = string.IsNullOrWhiteSpace(firstReason) ? "no reason given" : firstReason.Trim();
            var msg = $"{pass}: {failed} of {attempted} annotation(s) were placed but could not be provenance-stamped ({reason}). " +
                      "The next run cannot recognise them as its own: they will not follow a moved host, and will be " +
                      "reported as older, unstamped annotations.";
            // Revit's refusal for a vendor-locked schema. The provenance schema is
            // write-locked to the StingTools add-in's VendorId, so a caller running
            // under another add-in (a test harness, Dynamo, a script host) is refused.
            if (reason.IndexOf("not allowed to the current add-in", StringComparison.OrdinalIgnoreCase) >= 0)
                msg += $" The provenance schema only accepts writes from an add-in whose VendorId is \"{SchemaVendorId}\".";
            return msg;
        }

        /// <summary>
        /// VendorId the provenance schema is write-locked to. Mirrors
        /// StingSchemaBuilder.VendorId (Revit-bound) and the .addin manifest.
        /// </summary>
        public const string SchemaVendorId = "Planscape";

        /// <summary>The part a key was built with, or null.</summary>
        public static string PartOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int i = key.IndexOf(Sep);
            return i < 0 ? null : key.Substring(i + 1);
        }
    }

    /// <summary>
    /// Counts the provenance stamps a pass tried to write and how many failed, so
    /// a failure reaches the run's warnings instead of only the log. An annotation
    /// whose stamp failed is still placed, but the next run cannot recognise it as
    /// its own: it cannot follow a moved host and is reported as an older,
    /// unstamped note. Silent, that reads as a bug in the matching; it is not.
    /// </summary>
    public sealed class ProvenanceStampTally
    {
        public int Attempted { get; private set; }
        public int Failed { get; private set; }
        public string FirstReason { get; private set; }

        public void Record(bool stamped, string reason = null)
        {
            Attempted++;
            if (stamped) return;
            Failed++;
            if (FirstReason == null && !string.IsNullOrWhiteSpace(reason)) FirstReason = reason.Trim();
        }

        public string Warning(string pass) =>
            AnnotationProvenance.StampFailureWarning(pass, Attempted, Failed, FirstReason);
    }
}
