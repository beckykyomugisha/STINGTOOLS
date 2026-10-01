using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Commands.Electrical.ArcFlash
{
    /// <summary>sRGB colour from a "#RRGGBB" data value.</summary>
    public readonly struct ArcRgb : IEquatable<ArcRgb>
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
        public ArcRgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public bool Equals(ArcRgb o) => R == o.R && G == o.G && B == o.B;
        public override bool Equals(object obj) => obj is ArcRgb o && Equals(o);
        public override int GetHashCode() => (R << 16) | (G << 8) | B;
        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

        internal static bool TryParse(string s, out ArcRgb rgb)
        {
            rgb = default;
            string hex = (s ?? "").Trim().TrimStart('#');
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
                return false;
            rgb = new ArcRgb((byte)(v >> 16), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
            return true;
        }
    }

    /// <summary>One incident-energy band: energies up to and including <see cref="MaxCalCm2"/>
    /// (null = unbounded, the top band). A presentation band — never a PPE category.</summary>
    public sealed class ArcEnergyBand
    {
        public int Index { get; set; }
        public double? MaxCalCm2 { get; set; }
        public string Label { get; set; } = "";
        public ArcRgb ViewColour { get; set; }
    }

    /// <summary>ANSI Z535.4 signal-word panel: the word and its colours.</summary>
    public sealed class ArcLabelHeader
    {
        public string SignalWord { get; set; } = "";
        public ArcRgb Background { get; set; }
        public ArcRgb Text { get; set; }
    }

    /// <summary>
    /// Arc-flash presentation values from STING_ARC_FLASH_PPE.json (DSCH-25) — the one
    /// owner of the band colours and the label header. When <see cref="LoadError"/> is set
    /// nothing is coloured and no signal word is printed; the callers show the error.
    /// </summary>
    public sealed class ArcFlashPresentationSet
    {
        public IReadOnlyList<ArcEnergyBand> Bands { get; set; } = new ArcEnergyBand[0];
        public ArcLabelHeader Warning { get; set; }
        public ArcLabelHeader Danger { get; set; }
        /// <summary>DANGER when incident energy is above this; null = always WARNING.</summary>
        public double? DangerAboveCalCm2 { get; set; }
        /// <summary>Null when loaded; otherwise every problem found, joined.</summary>
        public string LoadError { get; set; }
        public bool Loaded => LoadError == null;

        /// <summary>The band an incident energy falls in, or null when not loaded.</summary>
        public ArcEnergyBand BandFor(double incidentEnergyCalCm2)
        {
            if (!Loaded) return null;
            foreach (var b in Bands)
                if (!b.MaxCalCm2.HasValue || incidentEnergyCalCm2 <= b.MaxCalCm2.Value) return b;
            return null;   // unreachable: Parse requires an unbounded top band
        }

        /// <summary>The Z535.4 header for an incident energy, or null when not loaded.</summary>
        public ArcLabelHeader HeaderFor(double incidentEnergyCalCm2)
        {
            if (!Loaded) return null;
            return DangerAboveCalCm2.HasValue && incidentEnergyCalCm2 > DangerAboveCalCm2.Value ? Danger : Warning;
        }
    }

    /// <summary>A 2D point in label coordinates: x to the right, y UP, origin at the
    /// top-left corner of the label (so the header strip occupies y in [-H, 0]).</summary>
    public readonly struct ArcPt
    {
        public double X { get; }
        public double Y { get; }
        public ArcPt(double x, double y) { X = x; Y = y; }
    }

    /// <summary>
    /// ANSI Z535 safety-alert symbol (DSCH-32): an equilateral triangle, apex up, with an
    /// exclamation mark in it, at the left of the signal-word header strip. Drawn as filled
    /// regions, never a font glyph (fonts could not be relied on for it). Revit-free so the
    /// geometry is tested; ArcFlashLabelSheetCommand turns it into filled regions.
    /// <para>Colours, per ANSI Z535.4 as we read it (VERIFY against the printed standard):
    /// the triangle takes the signal-word TEXT colour and the exclamation mark the panel
    /// BACKGROUND colour — black triangle / orange mark on WARNING, white triangle / red mark
    /// on DANGER.</para>
    /// Every polygon is returned counter-clockwise (y up), the order a filled region needs.
    /// </summary>
    public sealed class ArcSafetyAlertSymbol
    {
        /// <summary>Triangle height as a fraction of the header strip height.</summary>
        public const double HeightFraction = 0.8;

        public ArcPt[] Triangle { get; private set; }
        /// <summary>The exclamation bar: a quadrilateral, slightly wider at the top.</summary>
        public ArcPt[] Bar { get; private set; }
        public ArcPt DotCentre { get; private set; }
        public double DotRadius { get; private set; }
        /// <summary>Where the signal-word text may start (x), clear of the triangle.</summary>
        public double TextStartX { get; private set; }

        /// <param name="left">x of the triangle's left base corner.</param>
        /// <param name="headerHeight">height of the header strip (same unit as the result).</param>
        /// <param name="gap">space between the triangle and the signal word.</param>
        public static ArcSafetyAlertSymbol Compute(double left, double headerHeight, double gap)
        {
            if (headerHeight <= 0) throw new ArgumentOutOfRangeException(nameof(headerHeight));
            double h = headerHeight * HeightFraction;
            double s = 2.0 * h / Math.Sqrt(3.0);          // equilateral side
            double top = -(headerHeight - h) / 2.0;          // apex y (centred vertically)
            double baseY = top - h;
            double cx = left + s / 2.0;

            double barTop = top - 0.30 * h, barBottom = top - 0.68 * h;
            double halfTop = 0.07 * s, halfBottom = 0.05 * s;
            return new ArcSafetyAlertSymbol
            {
                Triangle = new[] { new ArcPt(left, baseY), new ArcPt(left + s, baseY), new ArcPt(cx, top) },
                Bar = new[]
                {
                    new ArcPt(cx - halfBottom, barBottom), new ArcPt(cx + halfBottom, barBottom),
                    new ArcPt(cx + halfTop, barTop), new ArcPt(cx - halfTop, barTop)
                },
                DotCentre = new ArcPt(cx, top - 0.82 * h),
                DotRadius = 0.065 * s,
                TextStartX = left + s + gap,
            };
        }

        /// <summary>Twice the signed area: positive when counter-clockwise (y up).</summary>
        public static double SignedArea2(IReadOnlyList<ArcPt> pts)
        {
            double a = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % pts.Count];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a;
        }

        /// <summary>True when <paramref name="p"/> lies strictly inside the triangle.</summary>
        public bool InsideTriangle(ArcPt p)
        {
            for (int i = 0; i < 3; i++)
            {
                var a = Triangle[i]; var b = Triangle[(i + 1) % 3];
                if ((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X) <= 0) return false;
            }
            return true;
        }
    }

    public static class ArcFlashPresentation
    {
        public const string FileName = "STING_ARC_FLASH_PPE.json";

        private static readonly Lazy<ArcFlashPresentationSet> _current =
            new Lazy<ArcFlashPresentationSet>(LoadFromDataFolder);

        /// <summary>The shipped presentation values, loaded once per session.</summary>
        public static ArcFlashPresentationSet Current => _current.Value;

        private static ArcFlashPresentationSet LoadFromDataFolder()
        {
            string path = null;
            try { path = StingTools.Core.StingToolsApp.FindDataFile(FileName); }
            catch (Exception ex) { return Failed($"{FileName} could not be located: {ex.Message}"); }
            return Load(path);
        }

        public static ArcFlashPresentationSet Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return Failed($"{FileName} not found");
            JObject root;
            try { root = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { return Failed($"{FileName} could not be read: {ex.Message}"); }
            var set = Parse(root);
            if (!set.Loaded) StingTools.Core.StingLog.Error("ArcFlashPresentation: " + set.LoadError);
            return set;
        }

        private static ArcFlashPresentationSet Failed(string error)
        {
            var set = new ArcFlashPresentationSet { LoadError = error + " — arc-flash colours and label headers are not applied." };
            StingTools.Core.StingLog.Error("ArcFlashPresentation: " + set.LoadError);
            return set;
        }

        /// <summary>
        /// Validates and reads <paramref name="root"/>. Any problem makes the whole set
        /// unloaded (with every problem listed): a half-read colour scheme is not shown.
        /// </summary>
        public static ArcFlashPresentationSet Parse(JObject root)
        {
            var errors = new List<string>();
            var bands = new List<ArcEnergyBand>();
            if (!(root?["energyBands"] is JArray arr) || arr.Count == 0)
                errors.Add("energyBands is missing or empty");
            else
            {
                double last = double.NegativeInfinity;
                for (int i = 0; i < arr.Count; i++)
                {
                    var row = arr[i] as JObject;
                    if (row == null) { errors.Add($"energyBands[{i}] is not an object"); continue; }
                    var max = row["maxCalCm2"];
                    double? maxVal = null;
                    if (max == null || max.Type == JTokenType.Null)
                    {
                        if (i != arr.Count - 1) errors.Add($"energyBands[{i}]: only the last band may be unbounded (maxCalCm2 null)");
                    }
                    else if (max.Type == JTokenType.Integer || max.Type == JTokenType.Float)
                    {
                        maxVal = max.Value<double>();
                        if (maxVal <= last) errors.Add($"energyBands[{i}]: maxCalCm2 {maxVal} is not above the previous band");
                        else last = maxVal.Value;
                        if (i == arr.Count - 1) errors.Add("the last energy band must be unbounded (maxCalCm2 null)");
                    }
                    else errors.Add($"energyBands[{i}]: maxCalCm2 is not a number");
                    string label = row["label"]?.Type == JTokenType.String ? row["label"].Value<string>() : null;
                    if (string.IsNullOrWhiteSpace(label)) errors.Add($"energyBands[{i}]: label is missing");
                    if (!ArcRgb.TryParse(row["viewColour"]?.ToString(), out var colour))
                        errors.Add($"energyBands[{i}]: viewColour '{row["viewColour"]}' is not #RRGGBB");
                    bands.Add(new ArcEnergyBand { Index = i, MaxCalCm2 = maxVal, Label = label ?? "", ViewColour = colour });
                }
            }

            var header = root?["labelHeader"] as JObject;
            ArcLabelHeader warning = null, danger = null;
            double? dangerAbove = null;
            if (header == null) errors.Add("labelHeader is missing");
            else
            {
                warning = ReadHeader(header["warning"] as JObject, "warning", "WARNING", errors);
                danger = ReadHeader(header["danger"] as JObject, "danger", "DANGER", errors);
                var t = header["dangerAboveCalCm2"];
                if (t == null) errors.Add("labelHeader.dangerAboveCalCm2 is missing (null = always WARNING)");
                else if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
                {
                    dangerAbove = t.Value<double>();
                    if (dangerAbove <= 0) errors.Add("labelHeader.dangerAboveCalCm2 must be above 0");
                }
                else if (t.Type != JTokenType.Null) errors.Add("labelHeader.dangerAboveCalCm2 is not a number or null");
            }

            return new ArcFlashPresentationSet
            {
                Bands = bands,
                Warning = warning,
                Danger = danger,
                DangerAboveCalCm2 = dangerAbove,
                LoadError = errors.Count == 0 ? null : $"{FileName}: " + string.Join("; ", errors)
            };
        }

        private static ArcLabelHeader ReadHeader(JObject o, string key, string expectedWord, List<string> errors)
        {
            if (o == null) { errors.Add($"labelHeader.{key} is missing"); return null; }
            string word = o["signalWord"]?.Type == JTokenType.String ? o["signalWord"].Value<string>() : null;
            // The signal word is fixed by ANSI Z535.4; the data carries it to keep the
            // label text and its colours together, not so it can be renamed.
            if (!string.Equals(word, expectedWord, StringComparison.Ordinal))
                errors.Add($"labelHeader.{key}.signalWord must be '{expectedWord}' (ANSI Z535.4), found '{word}'");
            if (!ArcRgb.TryParse(o["background"]?.ToString(), out var bg))
                errors.Add($"labelHeader.{key}.background is not #RRGGBB");
            if (!ArcRgb.TryParse(o["text"]?.ToString(), out var fg))
                errors.Add($"labelHeader.{key}.text is not #RRGGBB");
            return new ArcLabelHeader { SignalWord = word ?? "", Background = bg, Text = fg };
        }
    }
}
