// §5.3 — reader for the nine routing / MEP-hint parameters.
//
// AutoConduitDrop / AutoPipeDrop / AutoDuctDrop and the related validators
// (FillValidator, SlopeValidator, SeparationValidator, TerminationValidator)
// now read through this helper for per-family overrides. Empty values mean
// "use the discipline default" — existing behaviour is unchanged.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Routing
{
    public class RoutingHints
    {
        public int    ConnectorCount      { get; set; }
        public string ConnectorTypes      { get; set; } = "";  // e.g. "HWS,DCW,SAN"
        public string PreferredDropDir    { get; set; } = "";  // Up / Down / Side
        public double SlopeMinPct         { get; set; }
        public double SlopeMaxPct         { get; set; }
        public double FillMaxPct          { get; set; }        // overrides discipline default
        public string TerminationType     { get; set; } = "";  // Cap / Open / Elbow90 / Transition
        public double SegmentLenMaxMm     { get; set; }
        public double SupportPitchMm      { get; set; }
    }

    public static class RoutingParamReader
    {
        public static RoutingHints Read(Element el)
        {
            var r = new RoutingHints();
            if (el == null) return r;
            Element type = null;
            try { type = el.Document.GetElement(el.GetTypeId()); } catch { }
            Element primary = type ?? el;

            r.ConnectorCount   = ReadInt(primary, "CONN_COUNT_INT");
            r.ConnectorTypes   = ReadString(primary, "CONN_TYPES_TXT");
            r.PreferredDropDir = ReadString(primary, "PREF_DROP_DIR_TXT");
            r.SlopeMinPct      = ReadNumber(primary, "SLOPE_MIN_PCT");
            r.SlopeMaxPct      = ReadNumber(primary, "SLOPE_MAX_PCT");
            r.FillMaxPct       = ReadNumber(primary, "FILL_MAX_PCT");
            r.TerminationType  = ReadString(primary, "TERM_TYPE_TXT");
            r.SegmentLenMaxMm  = ReadLengthMm(primary, "SEGMENT_LEN_MAX_MM");
            r.SupportPitchMm   = ReadLengthMm(primary, "SUPPORT_PITCH_MM");
            return r;
        }

        private static string ReadString(Element el, string name)
        {
            try { return el?.LookupParameter(name)?.AsString() ?? ""; }
            catch { return ""; }
        }

        private static int ReadInt(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.Double) return (int)Math.Round(p.AsDouble());
            }
            catch { }
            return 0;
        }

        private static double ReadNumber(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
            }
            catch { }
            return 0;
        }

        private static double ReadLengthMm(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble() * 304.8;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
            }
            catch { }
            return 0;
        }
    }
}
