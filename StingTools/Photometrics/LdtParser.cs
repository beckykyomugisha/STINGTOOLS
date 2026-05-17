using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace StingTools.Photometrics
{
    /// <summary>
    /// EULUMDAT (.ldt) file parser. The format is a strictly line-oriented
    /// plain-text layout — each line is a single field at a fixed slot.
    /// Reference: http://paulbourke.net/dataformats/ldt/ and the original
    /// Stockmar 1990 specification. Pure I/O — no Revit references.
    /// </summary>
    public static class LdtParser
    {
        public static PhotometricFile ParseFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("LDT file not found", path);
            return Parse(File.ReadAllText(path), path);
        }

        public static PhotometricFile Parse(string text, string sourcePath = "")
        {
            var p = new PhotometricFile { FileFormat = "LDT", FilePath = sourcePath };
            if (string.IsNullOrEmpty(text)) { p.Warnings.Add("Empty file"); return p; }

            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count < 30) { p.Warnings.Add($"File too short ({lines.Count} lines) — invalid LDT"); return p; }

            try
            {
                // Lines 1-26 are header fields (1-indexed in the spec).
                p.Manufacturer  = SafeLine(lines, 1).Trim();
                int Ityp        = SafeInt(lines, 2);  // 1=point, 2=linear, 3=point with another sym
                int Isym        = SafeInt(lines, 3);  // 0=none, 1=rotational, 2=C0/180, 3=C90/270, 4=C0/180+C90/270
                int Mc          = SafeInt(lines, 4);  // No. of C-planes between 0..360°
                double Dc       = SafeDouble(lines, 5);
                int Ng          = SafeInt(lines, 6);  // No. of luminous-intensity values per C-plane
                double Dg       = SafeDouble(lines, 7);
                p.Keywords["MeasurementReportNumber"] = SafeLine(lines, 8);
                p.LuminaireName = SafeLine(lines, 9).Trim();
                p.CatalogNumber = SafeLine(lines, 10).Trim();
                // 11 — file name
                p.Keywords["DateUser"] = SafeLine(lines, 12);
                double lengthMm = SafeDouble(lines, 13);
                double widthMm  = SafeDouble(lines, 14);
                double heightMm = SafeDouble(lines, 15);
                // 16-19 — luminous-area dimensions
                double dffStart = SafeDouble(lines, 20);  // light output ratio %
                // 21 — DFF
                double cosCorr  = SafeDouble(lines, 22);  // conversion factor
                // 23 — tilt of luminaire
                int nLamps      = SafeInt(lines, 24);     // standard sets of lamps
                // 25-26 reserved
                p.LampCount     = nLamps > 0 ? nLamps : 1;
                p.LengthM = lengthMm / 1000.0;
                p.WidthM  = widthMm  / 1000.0;
                p.HeightM = heightMm / 1000.0;
                p.Symmetry = MapEulumdatSymmetry(Isym);

                // Lines 27 onwards: lamp blocks (5 lines per lamp set), then
                // direct-ratio table (10 values), then C-plane angles (Mc),
                // gamma angles (Ng), candela values (Mc * Ng).
                int idx = 26;  // 0-indexed; we already used lines 1..26
                double totalLumens = 0;
                double totalWatts  = 0;
                for (int i = 0; i < nLamps && idx + 4 < lines.Count; i++)
                {
                    int qty       = SafeInt(lines, idx + 1);
                    string lampType = SafeLine(lines, idx + 2);
                    double lumens = SafeDouble(lines, idx + 3);
                    string colour = SafeLine(lines, idx + 4);
                    string criStr = SafeLine(lines, idx + 5);
                    double watts  = SafeDouble(lines, idx + 6);
                    // Some EULUMDAT files have 5 lines per lamp; some have 6
                    // (depends on EULUMDAT v1 vs v2). Walk by 6 — extras are tolerated.
                    totalLumens += qty * lumens;
                    totalWatts  += qty * watts;
                    if (string.IsNullOrEmpty(p.Keywords.GetValueOrDefault("LampType")))
                        p.Keywords["LampType"] = lampType?.Trim() ?? "";
                    if (TryParseDouble(criStr, out double criV) && p.CRI == 0) p.CRI = criV;
                    if (TryParseDoubleFromColour(colour, out double cct) && p.CCT == 0) p.CCT = cct;
                    idx += 6;
                }
                p.TotalLumens = totalLumens;
                p.TotalWatts  = totalWatts;

                // Direct-ratios table — 10 values.
                idx += 10;

                // C-plane angles.
                for (int i = 0; i < Mc && idx < lines.Count; i++, idx++)
                    p.HorizontalAngles.Add(SafeDouble(lines, idx + 1));
                // Gamma angles.
                for (int i = 0; i < Ng && idx < lines.Count; i++, idx++)
                    p.VerticalAngles.Add(SafeDouble(lines, idx + 1));

                // Candela block — Ng values per C-plane.
                double peak = 0;
                for (int c = 0; c < Mc; c++)
                {
                    var row = new List<double>();
                    for (int g = 0; g < Ng && idx < lines.Count; g++, idx++)
                    {
                        double cd = SafeDouble(lines, idx + 1);
                        row.Add(cd);
                        if (cd > peak) peak = cd;
                    }
                    p.Candela.Add(row);
                }
                p.PeakCandela = peak;
                ResolveBeamAndFieldAngles(p);
            }
            catch (Exception ex) { p.Warnings.Add($"Parse error: {ex.Message}"); }
            return p;
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static string MapEulumdatSymmetry(int isym) => isym switch
        {
            1 => "rotational",
            2 => "axial",
            3 => "axial",
            4 => "quadrant",
            _ => "none"
        };

        private static string SafeLine(List<string> lines, int oneIndexed)
        {
            int i = oneIndexed - 1;
            if (i < 0 || i >= lines.Count) return "";
            return lines[i] ?? "";
        }
        private static int SafeInt(List<string> lines, int oneIndexed)
            => int.TryParse(SafeLine(lines, oneIndexed).Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        private static double SafeDouble(List<string> lines, int oneIndexed)
            => TryParseDouble(SafeLine(lines, oneIndexed).Trim(), out double v) ? v : 0;
        private static bool TryParseDouble(string s, out double v) =>
            double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        /// <summary>
        /// EULUMDAT lamp colour appearance is sometimes stored as plain
        /// Kelvin ("3000K"), sometimes as a CIE-coordinate, sometimes as a
        /// commercial label ("warm white"). Best-effort numeric extraction.
        /// </summary>
        private static bool TryParseDoubleFromColour(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string digits = "";
            foreach (char ch in s)
            {
                if (char.IsDigit(ch) || ch == '.') digits += ch;
                else if (digits.Length > 0) break;
            }
            return TryParseDouble(digits, out value);
        }

        private static void ResolveBeamAndFieldAngles(PhotometricFile p)
        {
            try
            {
                if (p.Candela.Count == 0 || p.Candela[0].Count == 0) return;
                var col = p.Candela[0];
                int peakI = 0; double peak = 0;
                for (int i = 0; i < col.Count; i++) if (col[i] > peak) { peak = col[i]; peakI = i; }
                if (peak <= 0) return;
                double half = peak * 0.5;
                double tenth = peak * 0.10;
                p.BeamAngleDeg  = SpanWhereCandelaExceeds(col, p.VerticalAngles, peakI, half);
                p.FieldAngleDeg = SpanWhereCandelaExceeds(col, p.VerticalAngles, peakI, tenth);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }
        private static double SpanWhereCandelaExceeds(List<double> col,
            List<double> angles, int peakI, double threshold)
        {
            int low = peakI, high = peakI;
            while (low > 0 && col[low - 1] >= threshold) low--;
            while (high < col.Count - 1 && col[high + 1] >= threshold) high++;
            if (low >= angles.Count || high >= angles.Count) return 0;
            return Math.Abs(angles[high] - angles[low]);
        }
    }
}
