using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    // ══════════════════════════════════════════════════════════════════════
    //  SheetQrConfig — the parts of the sheet QR a PROJECT gets to decide,
    //  without a code change and without an entry in STING_TITLE_BLOCKS.json.
    //
    //  WHY THIS EXISTS
    //  ---------------
    //  The first cut could only find a QR cell for title-block families listed in
    //  STING_TITLE_BLOCKS.json. Three sheets exported from a live project on
    //  2026-09-14 showed why that is not enough:
    //
    //    * all three title blocks ALREADY reserve a QR cell, drawn and labelled
    //      ("SCAN · VERIFY ISSUE" on the cover, "SCAN CURRENT ISSUE" on the rest),
    //    * the cell is in a DIFFERENT place on each sheet — (196,403), (767,109),
    //      (591,34) mm — so no single fallback corner can be right,
    //    * and none of the three families is in the spec, so the slot lookup finds
    //      nothing and the corner fallback would have missed all three.
    //
    //  The title-block guide already promised this: "the QR-code stamper ... never
    //  needs a hard-coded fallback — they read the family." This is that. A family
    //  that states its own cell needs no spec entry, no code change, and no
    //  agreement with anyone.
    //
    //  Everything here is Revit-free so the parsing is unit-tested; the Revit half
    //  only reads parameter strings and hands them over.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Where the QR anchor came from. Reported so "placed where the family
    /// says" and "placed in a corner because nothing said" never read alike.</summary>
    public enum QrAnchorSource
    {
        /// <summary>TB_QR_ANCHOR_JSON_TXT on the title-block instance.</summary>
        FamilyParameter,
        /// <summary>A `qr-code` entry in the family's own TB_VIEWPORT_SLOTS_JSON_TXT.</summary>
        FamilySlotMap,
        /// <summary>A `qr-code` slot in STING_TITLE_BLOCKS.json, by family id.</summary>
        SpecSlot,
        /// <summary>Nothing declared one.</summary>
        None,
    }

    /// <summary>A QR cell in millimetres from the sheet origin.</summary>
    public sealed class QrAnchor
    {
        public double XMm { get; set; }
        public double YMm { get; set; }
        public double SizeMm { get; set; }
        public QrAnchorSource Source { get; set; } = QrAnchorSource.None;

        public QrRect ToRect() => new QrRect(XMm, YMm, XMm + SizeMm, YMm + SizeMm);

        public override string ToString() =>
            $"({XMm:F1},{YMm:F1}) {SizeMm:F1}mm from {Source}";
    }

    public static class SheetQrConfig
    {
        /// <summary>Where a parse complaint goes. A no-op by default so this file
        /// stays Revit-free and unit-testable; the plugin points it at StingLog in
        /// SheetQrStamper's static constructor.
        ///
        /// It is a HOOK rather than a silent drop because a malformed anchor is a
        /// defect in the family — the operator authored something this cannot read,
        /// and the stamp then lands in a corner. That has to be sayable.</summary>
        public static Action<string> Warn { get; set; } = _ => { };

        /// <summary>Parse TB_QR_ANCHOR_JSON_TXT.
        ///
        /// Accepts <c>{"x":701,"y":85,"size":24}</c> and, because a person typing
        /// this into a Revit parameter box will not reach for JSON, the plain forms
        /// <c>701,85,24</c> and <c>701 85 24</c>.
        ///
        /// Returns null for anything it cannot read — INCLUDING a well-formed object
        /// with a zero or negative size. A zero-size anchor would place a stamp with
        /// no extent, which renders as nothing at all and looks exactly like the
        /// feature being switched off.</summary>
        public static QrAnchor ParseAnchor(string raw, double defaultSizeMm)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim();

            double x, y, size = defaultSizeMm;

            if (s.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    // BOTH keys must be PRESENT, not merely deserialisable. Binding to
                    // a DTO let {"x":701} produce y = 0 — the sheet's bottom edge, a
                    // plausible-looking position nobody wrote. A half-specified anchor
                    // is a typo, and a typo must not resolve to a real place.
                    var o = Newtonsoft.Json.Linq.JObject.Parse(s);
                    var xt = o["x"] ?? o["X"];
                    var yt = o["y"] ?? o["Y"];
                    if (xt == null || yt == null)
                    {
                        Warn($"SheetQrConfig: TB_QR_ANCHOR_JSON_TXT needs both \"x\" and \"y\": {Trim(s)}");
                        return null;
                    }
                    x = xt.ToObject<double>();
                    y = yt.ToObject<double>();
                    var st = o["size"] ?? o["Size"];
                    if (st != null)
                    {
                        var sv = st.ToObject<double>();
                        if (sv > 0) size = sv;
                    }
                }
                catch (Exception ex)
                {
                    // A malformed anchor is a defect in the family, not a reason to
                    // silently fall back — say so, then let the caller decide.
                    Warn($"SheetQrConfig: TB_QR_ANCHOR_JSON_TXT is not valid JSON ({ex.Message}): {Trim(s)}");
                    return null;
                }
            }
            else
            {
                var parts = s.Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return null;
                if (!TryNum(parts[0], out x) || !TryNum(parts[1], out y)) return null;
                if (parts.Length >= 3 && TryNum(parts[2], out var sz) && sz > 0) size = sz;
            }

            if (size <= 0) return null;
            return new QrAnchor { XMm = x, YMm = y, SizeMm = size, Source = QrAnchorSource.FamilyParameter };
        }

        /// <summary>Pull a `qr-code` cell out of the family's own
        /// TB_VIEWPORT_SLOTS_JSON_TXT — the slot map a STING title block already
        /// carries, per docs/guides/TITLE_BLOCK_CREATION_GUIDE.md.
        ///
        /// Matched on purposeTag first, then on a slot literally named "QR", so a
        /// family authored before the purposeTag vocabulary existed still works.</summary>
        public static QrAnchor ParseSlotMap(string rawJson, double defaultSizeMm)
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return null;
            List<SlotDto> slots;
            try
            {
                slots = JsonConvert.DeserializeObject<List<SlotDto>>(rawJson.Trim());
            }
            catch (Exception ex)
            {
                Warn($"SheetQrConfig: TB_VIEWPORT_SLOTS_JSON_TXT is not a slot array ({ex.Message}).");
                return null;
            }
            if (slots == null) return null;

            foreach (var s in slots)
            {
                if (s == null) continue;
                bool isQr = string.Equals(s.PurposeTag, "qr-code", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(s.Kind, "qr-code", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(s.Name, "QR", StringComparison.OrdinalIgnoreCase);
                if (!isQr) continue;

                // A slot carries a rectangle; a QR is square, so fit to the short
                // side rather than stretching one axis.
                double size = Math.Min(s.W > 0 ? s.W : defaultSizeMm, s.H > 0 ? s.H : defaultSizeMm);
                if (size <= 0) size = defaultSizeMm;
                return new QrAnchor { XMm = s.X, YMm = s.Y, SizeMm = size, Source = QrAnchorSource.FamilySlotMap };
            }
            return null;
        }

        /// <summary>Render a payload template. Blank returns null, which the caller
        /// reads as "use the standard STING deep link" — the default must stay the
        /// thing the Planscape scanner understands, so an unset template can never
        /// quietly produce a code our own app rejects.
        ///
        /// Unknown tokens are left as literal text rather than blanked, matching
        /// TitleBlockParamApplier: a typo then shows up on the drawing as
        /// <c>{sheetnumber}</c> instead of vanishing.</summary>
        public static string RenderTemplate(string template, IDictionary<string, string> tokens)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;
            string s = template.Trim();
            if (tokens == null) return s;

            foreach (var kv in tokens)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                s = s.Replace("{" + kv.Key + "}", kv.Value ?? "");
            }
            return s;
        }

        /// <summary>Parse TB_QR_SIZE_MM_TXT. Returns null when unset or unusable —
        /// never 0, because a 0 would place an invisible stamp.</summary>
        public static double? ParseSize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var t = raw.Trim().TrimEnd('m', 'M').Trim();
            return TryNum(t, out var v) && v > 0 ? v : (double?)null;
        }

        private static bool TryNum(string s, out double v) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v);

        private static string Trim(string s) => s.Length <= 120 ? s : s.Substring(0, 120) + "…";

        private sealed class SlotDto
        {
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("kind")] public string Kind { get; set; }
            [JsonProperty("purposeTag")] public string PurposeTag { get; set; }
            [JsonProperty("x")] public double X { get; set; }
            [JsonProperty("y")] public double Y { get; set; }
            [JsonProperty("w")] public double W { get; set; }
            [JsonProperty("h")] public double H { get; set; }
        }
    }
}
