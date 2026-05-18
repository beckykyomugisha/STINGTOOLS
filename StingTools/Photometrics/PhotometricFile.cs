using System;
using System.Collections.Generic;
using System.IO;

namespace StingTools.Photometrics
{
    /// <summary>
    /// Common-denominator photometric data DTO. Populated by the IES, LDT
    /// and (future) GLDF parsers; consumed by the AssignPhotometricCommand
    /// to stamp Revit luminaire types with manufacturer-supplied data.
    /// All angles in degrees, intensities in candela, dimensions in metres.
    /// </summary>
    public class PhotometricFile
    {
        /// <summary>Source file format ("IES" | "LDT" | "GLDF").</summary>
        public string FileFormat   { get; set; } = "";
        public string FilePath     { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string LuminaireName{ get; set; } = "";
        public string CatalogNumber{ get; set; } = "";

        /// <summary>Total luminous flux in lumens (sum across all lamps).</summary>
        public double TotalLumens  { get; set; }
        /// <summary>Total power draw in watts (lamp + ballast where the file separates them).</summary>
        public double TotalWatts   { get; set; }
        /// <summary>Lamp count from the photometric file (≥1).</summary>
        public int    LampCount    { get; set; } = 1;
        /// <summary>Beam angle in degrees (50 % peak intensity envelope) — derived during parse.</summary>
        public double BeamAngleDeg { get; set; }
        /// <summary>Field angle in degrees (10 % peak intensity envelope) — derived during parse.</summary>
        public double FieldAngleDeg{ get; set; }
        /// <summary>Peak candela across the candela grid.</summary>
        public double PeakCandela  { get; set; }

        /// <summary>Correlated colour temperature in Kelvin (K). 0 if not in file.</summary>
        public double CCT          { get; set; }
        /// <summary>Colour Rendering Index Ra. 0 if not in file.</summary>
        public double CRI          { get; set; }

        /// <summary>"rotational" | "axial" | "quadrant" | "none" — derived from H/V angle counts.</summary>
        public string Symmetry     { get; set; } = "none";

        /// <summary>Luminaire physical width in metres (X-extent).</summary>
        public double WidthM       { get; set; }
        /// <summary>Luminaire physical length in metres (Y-extent).</summary>
        public double LengthM      { get; set; }
        /// <summary>Luminaire physical height in metres (Z-extent).</summary>
        public double HeightM      { get; set; }

        /// <summary>Vertical angles (γ) of the candela grid in degrees.</summary>
        public List<double> VerticalAngles  { get; set; } = new List<double>();
        /// <summary>Horizontal angles (C) of the candela grid in degrees.</summary>
        public List<double> HorizontalAngles{ get; set; } = new List<double>();
        /// <summary>Candela values indexed [horizontal][vertical].</summary>
        public List<List<double>> Candela   { get; set; } = new List<List<double>>();

        /// <summary>Free-form descriptive lines (TILT, OWNER, ISSUEDATE, …).</summary>
        public Dictionary<string, string> Keywords { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Warnings emitted by the parser (non-fatal).</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        public double Efficacy => TotalWatts > 0 ? TotalLumens / TotalWatts : 0;

        public override string ToString() =>
            string.IsNullOrEmpty(LuminaireName)
                ? System.IO.Path.GetFileName(FilePath ?? "")
                : LuminaireName;
    }
}
