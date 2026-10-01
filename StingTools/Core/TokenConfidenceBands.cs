using System;

namespace StingTools.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    // TAGACC-18 — how much to trust a LOC / ZONE / SYS value, and why.
    //
    // TokenAutoPopulator.PopulateAll records where each spatial token came from
    // (ASS_LOC_SOURCE_TXT / ASS_ZONE_SOURCE_TXT) and which SYS detection layer
    // fired (ASS_SYS_DETECT_LAYER_INT). The Token Confidence Audit reads those
    // back. Its classifier lived inside the command and had drifted from the
    // writer: ZONE "ScopeBox" (STING-ZONE:: boxes) and the "Proximity" source of
    // both tokens fell through to Low, so a zone drawn as a scope box was
    // reported as a silent default. One table here, written against the values
    // the writer emits, and tested without Revit.
    //
    // Every band also carries a REASON, so a fallback is never just a count.
    // ─────────────────────────────────────────────────────────────────────────

    internal enum ConfidenceBand { High, Medium, Low }

    internal readonly struct TokenConfidence
    {
        public TokenConfidence(ConfidenceBand band, string reason)
        {
            Band = band;
            Reason = reason ?? "";
        }

        public ConfidenceBand Band { get; }
        public string Reason { get; }
    }

    internal static class TokenConfidenceBands
    {
        // The source strings PopulateAll writes. Kept here so a test can hold the
        // classifier and the writer to the same vocabulary.
        public const string TypeOverride = "TYPE_OVERRIDE";
        public const string Room = "Room";
        public const string Workset = "Workset";
        public const string ScopeBox = "ScopeBox";
        public const string ProjectInfo = "ProjectInfo";
        public const string Proximity = "Proximity";
        public const string Default = "Default";

        public static TokenConfidence ClassifyLoc(string source)
        {
            switch (Norm(source))
            {
                case "TYPE_OVERRIDE": return High("set on the family type");
                case "ROOM": return High("detected from the room / space");
                case "WORKSET": return High("detected from the workset name");
                case "SCOPEBOX": return High("inside a STING-LOC:: scope box");
                case "PROJECTINFO": return Medium("taken from Project Information — one building assumed for the whole model");
                case "PROXIMITY": return Medium("copied from the nearest tagged element of the same category — not detected");
                case "DEFAULT": return Low("no room, workset or STING-LOC:: scope box located it and Project Information names no LOC — the token policy fallback was written");
                case "": return Low("no source recorded — tagged before provenance was written, or the token policy refused LOC");
                default: return Low($"unrecognised source '{source.Trim()}'");
            }
        }

        public static TokenConfidence ClassifyZone(string source)
        {
            switch (Norm(source))
            {
                case "TYPE_OVERRIDE": return High("set on the family type");
                case "ROOM": return High("detected from the room / space or its department");
                case "SCOPEBOX": return High("inside a STING-ZONE:: scope box");
                case "PROXIMITY": return Medium("copied from the nearest tagged element of the same category — not detected");
                case "DEFAULT": return Low("no room, department or STING-ZONE:: scope box gave a zone — the token policy fallback was written");
                case "": return Low("no source recorded — tagged before provenance was written, or the token policy refused ZONE");
                default: return Low($"unrecognised source '{source.Trim()}'");
            }
        }

        // SYS layers: 1–5 genuine detection, 6 category fallback, 7 discipline default, 0 unset.
        public static TokenConfidence ClassifySys(int layer)
        {
            if (layer >= 1 && layer <= 5) return High($"detected (layer {layer})");
            if (layer == 6) return Medium("category fallback (layer 6) — no MEP system, family or connector named a system");
            if (layer == 7) return Low("discipline default (layer 7) — nothing about the element named a system");
            return Low("no detection layer recorded");
        }

        /// <summary>
        /// True when LOC was filled by the fallback rather than detected — whatever
        /// value the fallback wrote. The audit used to test for the literal "BLD1",
        /// which missed every project whose token policy names another fallback.
        /// </summary>
        public static bool IsLocDefault(string source)
            => Norm(source) == "DEFAULT";

        private static string Norm(string s) => (s ?? "").Trim().ToUpperInvariant();
        private static TokenConfidence High(string r) => new TokenConfidence(ConfidenceBand.High, r);
        private static TokenConfidence Medium(string r) => new TokenConfidence(ConfidenceBand.Medium, r);
        private static TokenConfidence Low(string r) => new TokenConfidence(ConfidenceBand.Low, r);
    }
}
