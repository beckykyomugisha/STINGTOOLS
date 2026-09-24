using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using StingTools.Core;

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
                // EULUMDAT fixed-line layout (1-indexed):
                //  1 company   2 Ityp   3 Isym   4 Mc   5 Dc   6 Ng   7 Dg
                //  8 report no.   9 luminaire name   10 luminaire no.   11 file name   12 date/user
                // 13 length/diameter (mm)  14 width (mm)  15 height (mm)
                // 16-21 luminous-area dimensions
                // 22 DFF (%)   23 LORL light output ratio (%)   24 CFLI conversion factor
                // 25 tilt   26 n = number of standard sets of lamps
                // then SIX FIELD GROUPS of n lines each (not n blocks of six):
                //   26a lamp count  26b lamp type  26c TOTAL flux of the set (lm)
                //   26d colour  26e CRI group  26f wattage incl. ballast (W)
                // then 10 direct ratios, Mc C-angles, Ng γ-angles, and the
                // intensities in cd/1000 lm for C-planes Mc1..Mc2 (Isym-dependent).
                p.Manufacturer  = SafeLine(lines, 1).Trim();
                int Ityp        = SafeInt(lines, 2);
                int Isym        = SafeInt(lines, 3);  // 0 none, 1 rotational, 2 C0-C180, 3 C90-C270, 4 both
                int Mc          = SafeInt(lines, 4);  // No. of C-planes between 0..360°
                double Dc       = SafeDouble(lines, 5);
                int Ng          = SafeInt(lines, 6);  // No. of intensities per C-plane
                double Dg       = SafeDouble(lines, 7);
                p.Keywords["MeasurementReportNumber"] = SafeLine(lines, 8);
                p.LuminaireName = SafeLine(lines, 9).Trim();
                p.CatalogNumber = SafeLine(lines, 10).Trim();
                p.Keywords["DateUser"] = SafeLine(lines, 12);
                double lengthMm = SafeDouble(lines, 13);
                double widthMm  = SafeDouble(lines, 14);
                double heightMm = SafeDouble(lines, 15);
                double lorPct   = SafeDouble(lines, 23);
                double cfli     = SafeDouble(lines, 24);
                int nSets       = SafeInt(lines, 26);
                p.LengthM = lengthMm / 1000.0;
                p.WidthM  = widthMm  / 1000.0;
                p.HeightM = heightMm / 1000.0;
                p.Symmetry = MapEulumdatSymmetry(Isym);
                p.Keywords["Ityp"] = Ityp.ToString(CultureInfo.InvariantCulture);
                p.Keywords["LORL"] = lorPct.ToString(CultureInfo.InvariantCulture);
                if (nSets < 1) { p.Warnings.Add($"Number of lamp sets n = {nSets} — invalid LDT"); return p; }
                if (Mc < 1 || Ng < 1) { p.Warnings.Add($"Mc={Mc} / Ng={Ng} — invalid LDT"); return p; }

                int L(int field, int set) => 27 + field * nSets + set; // 1-indexed line of 26a..26f
                int lampCount   = SafeInt(lines, L(0, 0));
                string lampType = SafeLine(lines, L(1, 0)).Trim();
                double setFlux  = SafeDouble(lines, L(2, 0));   // total flux of the whole set
                string colour   = SafeLine(lines, L(3, 0));
                string criStr   = SafeLine(lines, L(4, 0));
                double setWatts = SafeDouble(lines, L(5, 0));   // incl. ballast, whole set

                // 26c is ALREADY the set's total — multiplying by the lamp count
                // (the previous behaviour) over-stated a 2-lamp fitting 2x.
                p.LampCount   = Math.Abs(lampCount) > 0 ? Math.Abs(lampCount) : 1;
                p.TotalLumens = setFlux;
                p.TotalWatts  = setWatts;
                p.Keywords["LampType"] = lampType;
                if (TryParseDouble(criStr, out double criV)) p.CRI = criV;
                if (TryParseDoubleFromColour(colour, out double cct)) p.CCT = cct;
                if (lampCount < 0)
                {
                    // Convention for absolute (LED) photometry: negative lamp count,
                    // 26c = luminaire flux, LORL = 100 %.
                    p.AbsolutePhotometry = true;
                }
                if (nSets > 1)
                    p.Warnings.Add($"{nSets} lamp sets listed — sets 2..{nSets} are treated as alternatives; set 1 used for flux, watts and cd conversion.");

                int idx = 26 + 6 * nSets;   // 0-indexed position of the first direct ratio
                idx += 10;                  // direct ratios

                var allC = new List<double>();
                for (int i = 0; i < Mc; i++, idx++) allC.Add(SafeDouble(lines, idx + 1));
                for (int i = 0; i < Ng; i++, idx++) p.VerticalAngles.Add(SafeDouble(lines, idx + 1));

                // Which C-planes are actually stored (EULUMDAT Mc1..Mc2, 1-indexed).
                int mc1, mc2;
                switch (Isym)
                {
                    case 1:  mc1 = 1;              mc2 = 1;              break;
                    case 2:  mc1 = 1;              mc2 = Mc / 2 + 1;     break;
                    case 3:  mc1 = 3 * Mc / 4 + 1; mc2 = mc1 + Mc / 2;   break;
                    case 4:  mc1 = 1;              mc2 = Mc / 4 + 1;     break;
                    default: mc1 = 1;              mc2 = Mc;             break;
                }

                // cd/1000 lm → cd, using the set's lamp flux (and CFLI when given).
                double toCd = setFlux > 0 ? setFlux / 1000.0 : 0;
                if (cfli > 0) toCd *= cfli;
                if (toCd <= 0)
                    p.Warnings.Add("Lamp flux (26c) is 0 — intensities are left in cd/1000 lm and cannot be converted to cd.");
                double scale = toCd > 0 ? toCd : 1.0;

                double peak = 0;
                for (int c = mc1; c <= mc2; c++)
                {
                    int ci = (c - 1) % Mc;          // Isym 3 wraps past C360 back to C0
                    double angle = allC.Count > ci ? allC[ci] : 0;
                    // Keep the stored planes monotonic: C270..C360..C90 → -90..0..90.
                    if (Isym == 3 && angle > 180) angle -= 360;
                    p.HorizontalAngles.Add(angle);
                    var row = new List<double>();
                    for (int g = 0; g < Ng; g++, idx++)
                    {
                        double cd = SafeDouble(lines, idx + 1) * scale;
                        row.Add(cd);
                        if (cd > peak) peak = cd;
                    }
                    p.Candela.Add(row);
                }
                p.PeakCandela = peak;
                ResolveBeamAndFieldAngles(p);

                if (toCd > 0)
                {
                    p.LuminaireLumens = PhotometricIntegrator.ZonalLumens(
                        p.VerticalAngles, p.HorizontalAngles, p.Candela);
                    // Consistency check: integrated output should be ≈ lamp flux × LOR.
                    if (lorPct > 0 && setFlux > 0 && p.LuminaireLumens > 0)
                    {
                        double expected = setFlux * lorPct / 100.0;
                        double dev = Math.Abs(p.LuminaireLumens - expected) / expected;
                        if (dev > 0.10)
                            p.Warnings.Add($"Integrated flux {p.LuminaireLumens:0} lm differs {dev:P0} from lamp flux × LOR ({expected:0} lm) — check the file.");
                    }
                }
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
