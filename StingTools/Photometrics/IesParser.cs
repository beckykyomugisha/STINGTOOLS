using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace StingTools.Photometrics
{
    /// <summary>
    /// IESNA LM-63 file parser. Handles the common LM-63-1986 / 1991 / 1995 /
    /// 2002 variants — the format is plain text with optional header keywords
    /// (LM-63-1991+) followed by TILT directive, lamp/luminaire counts and
    /// the candela grid. Pure I/O — no Revit references, unit-testable.
    ///
    /// Format references:
    ///  - Paul Bourke, "IES file format" (canonical informal description)
    ///  - IESNA LM-63-2002 specification (paywalled)
    ///  - Sample files in the IESNA / NIST validation set
    /// </summary>
    public static class IesParser
    {
        private static readonly char[] Whitespace = { ' ', '\t', '\r', '\n' };

        public static PhotometricFile ParseFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("IES file not found", path);
            return Parse(File.ReadAllText(path), path);
        }

        public static PhotometricFile Parse(string text, string sourcePath = "")
        {
            var p = new PhotometricFile { FileFormat = "IES", FilePath = sourcePath };
            if (string.IsNullOrEmpty(text))
            { p.Warnings.Add("Empty file"); return p; }

            // 1. Split into lines, remove BOM / blank trailing.
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            int idx = 0;

            // 2. Optional version header — IESNA:LM-63-XXXX on the first line.
            if (idx < lines.Count && lines[idx].TrimStart().StartsWith("IESNA",
                StringComparison.OrdinalIgnoreCase))
            { p.Keywords["VERSION"] = lines[idx].Trim(); idx++; }

            // 3. Keyword block — lines like "[KEYWORD] value" until "TILT=".
            while (idx < lines.Count)
            {
                string line = lines[idx];
                if (line.TrimStart().StartsWith("TILT=", StringComparison.OrdinalIgnoreCase))
                    break;
                if (line.TrimStart().StartsWith("["))
                {
                    int rb = line.IndexOf(']');
                    if (rb > 1)
                    {
                        string key = line.Substring(line.IndexOf('[') + 1, rb - line.IndexOf('[') - 1).Trim();
                        string val = line.Substring(rb + 1).Trim();
                        if (!string.IsNullOrEmpty(key))
                            p.Keywords[key] = val;
                        ApplyKeywordToFile(p, key, val);
                    }
                }
                idx++;
            }

            // 4. TILT directive (NONE | INCLUDE | <filename>). Skip the embedded
            //    tilt block when INCLUDE — production files rarely use it.
            if (idx >= lines.Count)
            { p.Warnings.Add("No TILT directive found"); return p; }
            string tiltLine = lines[idx].Trim();
            idx++;
            string tilt = tiltLine.Substring(tiltLine.IndexOf('=') + 1).Trim().ToUpperInvariant();
            if (tilt == "INCLUDE")
            {
                int lampToLumGeometry = ParseInt(NextToken(lines, ref idx));
                int numAnglePairs = ParseInt(NextToken(lines, ref idx));
                for (int i = 0; i < numAnglePairs; i++) NextToken(lines, ref idx);
                for (int i = 0; i < numAnglePairs; i++) NextToken(lines, ref idx);
            }

            // 5. Numeric block — flat token stream, whitespace-separated.
            var tokens = new Queue<string>(lines.Skip(idx)
                .SelectMany(l => l.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries)));
            try
            {
                int lampCount        = ParseInt(Pop(tokens));
                double lumensPerLamp = ParseDouble(Pop(tokens));
                double multiplier    = ParseDouble(Pop(tokens));
                int nVert            = ParseInt(Pop(tokens));
                int nHoriz           = ParseInt(Pop(tokens));
                int photoType        = ParseInt(Pop(tokens));   // 1=C 2=B 3=A
                int unitsType        = ParseInt(Pop(tokens));   // 1=feet 2=metres
                double width         = ParseDouble(Pop(tokens));
                double length        = ParseDouble(Pop(tokens));
                double height        = ParseDouble(Pop(tokens));
                double ballastFactor = ParseDouble(Pop(tokens));
                double ballastLamp   = ParseDouble(Pop(tokens)); // future-use field
                double inputWatts    = ParseDouble(Pop(tokens));

                p.LampCount   = lampCount > 0 ? lampCount : 1;
                p.TotalLumens = lumensPerLamp > 0 ? lumensPerLamp * p.LampCount : 0;
                p.TotalWatts  = inputWatts;

                double mToFeet = 0.3048;
                if (unitsType == 1) // feet → metres
                { p.WidthM = width * mToFeet; p.LengthM = length * mToFeet; p.HeightM = height * mToFeet; }
                else                // already metres
                { p.WidthM = width; p.LengthM = length; p.HeightM = height; }

                for (int i = 0; i < nVert; i++) p.VerticalAngles.Add(ParseDouble(Pop(tokens)));
                for (int i = 0; i < nHoriz; i++) p.HorizontalAngles.Add(ParseDouble(Pop(tokens)));

                double peak = 0;
                for (int h = 0; h < nHoriz; h++)
                {
                    var row = new List<double>();
                    for (int v = 0; v < nVert; v++)
                    {
                        double cd = ParseDouble(Pop(tokens)) * multiplier * ballastFactor;
                        row.Add(cd);
                        if (cd > peak) peak = cd;
                    }
                    p.Candela.Add(row);
                }
                p.PeakCandela = peak;
                p.Symmetry = ResolveSymmetry(p.HorizontalAngles);
                ResolveBeamAndFieldAngles(p);
            }
            catch (Exception ex)
            {
                p.Warnings.Add($"Numeric block parse error: {ex.Message}");
            }
            return p;
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static void ApplyKeywordToFile(PhotometricFile p, string key, string val)
        {
            switch (key.ToUpperInvariant())
            {
                case "MANUFAC":
                case "MANUFACTURER":
                    p.Manufacturer = val; break;
                case "LUMCAT":
                case "LUMINAIRE":
                    p.LuminaireName = val; break;
                case "LUMINAIRENAME":
                    p.LuminaireName = val; break;
                case "ORDERINGCODE":
                case "CATALOG":
                    p.CatalogNumber = val; break;
                case "LAMPCAT":
                    if (string.IsNullOrEmpty(p.LuminaireName)) p.LuminaireName = val; break;
                case "CCT":
                case "COLORTEMP":
                case "COLOR_TEMP":
                {
                    if (TryParseDouble(val, out double cct)) p.CCT = cct;
                    break;
                }
                case "CRI":
                case "COLORRENDERING":
                case "COLOR_RENDERING":
                {
                    if (TryParseDouble(val, out double cri)) p.CRI = cri;
                    break;
                }
            }
        }

        private static string ResolveSymmetry(List<double> horizAngles)
        {
            if (horizAngles == null || horizAngles.Count == 0) return "none";
            double last = horizAngles[horizAngles.Count - 1];
            // Convention from LM-63: last horizontal angle indicates symmetry.
            if (Math.Abs(last) < 0.01) return "rotational";       // axially symmetric
            if (Math.Abs(last - 90)  < 0.01) return "quadrant";   // four-fold symmetry
            if (Math.Abs(last - 180) < 0.01) return "axial";      // bilateral / lateral
            return "none";
        }

        /// <summary>
        /// Beam angle = first vertical angle pair where intensity falls to
        /// 50 % of peak (sym about peak axis). Field angle = same at 10 %.
        /// </summary>
        private static void ResolveBeamAndFieldAngles(PhotometricFile p)
        {
            try
            {
                if (p.Candela.Count == 0 || p.Candela[0].Count == 0) return;
                var col = p.Candela[0]; // first horizontal slice
                int peakI = 0; double peak = 0;
                for (int i = 0; i < col.Count; i++) if (col[i] > peak) { peak = col[i]; peakI = i; }
                if (peak <= 0) return;
                double half = peak * 0.5;
                double tenth = peak * 0.10;
                p.BeamAngleDeg  = SpanWhereCandelaExceeds(col, p.VerticalAngles, peakI, half);
                p.FieldAngleDeg = SpanWhereCandelaExceeds(col, p.VerticalAngles, peakI, tenth);
            }
            catch { /* best-effort */ }
        }

        private static double SpanWhereCandelaExceeds(List<double> col,
            List<double> angles, int peakI, double threshold)
        {
            int low = peakI, high = peakI;
            while (low > 0 && col[low - 1] >= threshold) low--;
            while (high < col.Count - 1 && col[high + 1] >= threshold) high++;
            return Math.Abs(angles[high] - angles[low]);
        }

        private static int ParseInt(string s) =>
            int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        private static double ParseDouble(string s) =>
            TryParseDouble(s, out double v) ? v : 0;

        private static bool TryParseDouble(string s, out double v) =>
            double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static string Pop(Queue<string> q) => q.Count > 0 ? q.Dequeue() : "";

        private static string NextToken(List<string> lines, ref int idx)
        {
            while (idx < lines.Count)
            {
                var parts = lines[idx].Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
                idx++;
                if (parts.Length > 0) return parts[0];
            }
            return "";
        }
    }
}
