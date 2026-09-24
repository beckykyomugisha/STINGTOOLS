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
        public const string MatchCaption    = "MatchLine.Caption";
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

        /// <summary>The host UniqueId a key was built from.</summary>
        public static string HostOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int i = key.IndexOf(Sep);
            return i < 0 ? key : key.Substring(0, i);
        }

        /// <summary>The part a key was built with, or null.</summary>
        public static string PartOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int i = key.IndexOf(Sep);
            return i < 0 ? null : key.Substring(i + 1);
        }
    }
}
