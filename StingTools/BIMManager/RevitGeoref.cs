using System;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.BIMManager
{
    /// <summary>
    /// B2 — a Revit document's georeferencing, in the units the Planscape server
    /// expects (METRES for survey position, degrees for true north).
    /// </summary>
    internal sealed class RevitGeorefData
    {
        /// <summary>Survey easting of the project internal origin, metres.</summary>
        public double EastingM { get; set; }

        /// <summary>Survey northing of the project internal origin, metres.</summary>
        public double NorthingM { get; set; }

        /// <summary>Survey elevation of the project internal origin, metres.</summary>
        public double ElevationM { get; set; }

        /// <summary>
        /// Angle from the CRS grid north to project north, degrees clockwise
        /// positive — Revit's <c>ProjectPosition.Angle</c>, converted from radians.
        /// </summary>
        public double TrueNorthDeg { get; set; }

        /// <summary>Latitude in degrees, if the document carries site data.</summary>
        public double? LatitudeDeg { get; set; }

        /// <summary>Longitude in degrees, if the document carries site data.</summary>
        public double? LongitudeDeg { get; set; }

        /// <summary>
        /// The coordinate reference system the survey point is expressed in,
        /// e.g. "EPSG:27700". Revit has no native concept of this, so it is read
        /// from the optional <c>PRJ_CRS_EPSG_TXT</c> project parameter.
        /// </summary>
        public string CrsEpsg { get; set; }

        /// <summary>"ProjectInternal" — see <see cref="RevitGeoref"/> remarks.</summary>
        public string ExportMode { get; set; } = "ProjectInternal";

        /// <summary>
        /// True when the survey position is far enough from zero to be a real
        /// georeference rather than an untouched default. A project whose survey
        /// point was never moved reports (0,0,0), and writing a zero transform
        /// for it is noise — worse, it looks like a deliberate placement.
        /// </summary>
        public bool HasSurveyOrigin =>
            Math.Abs(EastingM) > 0.001 || Math.Abs(NorthingM) > 0.001;
    }

    /// <summary>
    /// Reads a Revit document's survey position so the server can place the
    /// published model in the federation without the coordinator typing a
    /// transform.
    ///
    /// <para><b>Why this is metadata and not baked into the mesh.</b> The GLB
    /// exporter writes geometry about the PROJECT INTERNAL origin
    /// (<c>ClashExportContext</c> uses <c>Transform.Identity</c>), and that is
    /// the right choice: a site at easting 432,000 m would put every vertex
    /// ~432 km from the origin, where 32-bit float mesh coordinates lose
    /// millimetre precision and surfaces visibly z-fight. So the position
    /// travels beside the geometry as numbers, and the viewer applies it as a
    /// transform — exactly what the IFC path does with IfcMapConversion.</para>
    ///
    /// <para><b>Export mode.</b> Always "ProjectInternal" here, because that is
    /// what the exporter actually does. It is reported explicitly rather than
    /// assumed so that if a shared-coordinates export is ever added, the server
    /// will not double-count the survey origin — it already refuses to write a
    /// transform for a "SharedCoordinates" upload.</para>
    /// </summary>
    internal static class RevitGeoref
    {
        private const double FeetToMetres = 0.3048;

        /// <summary>
        /// Optional project parameter naming the CRS the survey point sits in.
        /// Revit does not model this, and it is the difference between a
        /// transform the platform will apply on its own and one it merely
        /// suggests: without a CRS anchor the server grades the georeference LOW
        /// and leaves the model at the origin until a coordinator confirms it.
        /// Set it ONCE per project, to match the project's declared coordinate
        /// system on the server.
        /// </summary>
        public const string CrsParamName = "PRJ_CRS_EPSG_TXT";

        /// <summary>
        /// Read the document's georeferencing, or null when it carries none
        /// (no project location, or a survey point still at the origin).
        /// Never throws — a document without site data is normal.
        /// </summary>
        public static RevitGeorefData Read(Document doc)
        {
            if (doc == null) return null;

            try
            {
                ProjectLocation loc = doc.ActiveProjectLocation;
                if (loc == null) return null;

                ProjectPosition pos = loc.GetProjectPosition(XYZ.Zero);
                if (pos == null) return null;

                var data = new RevitGeorefData
                {
                    // ProjectPosition is in Revit internal units (feet) and gives
                    // the SURVEY coordinates of the project internal origin —
                    // precisely the number the server needs to negate.
                    EastingM     = pos.EastWest   * FeetToMetres,
                    NorthingM    = pos.NorthSouth * FeetToMetres,
                    ElevationM   = pos.Elevation  * FeetToMetres,
                    TrueNorthDeg = pos.Angle * (180.0 / Math.PI),
                    ExportMode   = "ProjectInternal",
                };

                try
                {
                    SiteLocation site = doc.SiteLocation;
                    if (site != null)
                    {
                        data.LatitudeDeg  = site.Latitude  * (180.0 / Math.PI);
                        data.LongitudeDeg = site.Longitude * (180.0 / Math.PI);
                    }
                }
                catch { /* not every document carries site data */ }

                try
                {
                    var pi = doc.ProjectInformation;
                    if (pi != null)
                    {
                        var crs = ParameterHelpers.GetString(pi, CrsParamName);
                        if (!string.IsNullOrWhiteSpace(crs)) data.CrsEpsg = crs.Trim();
                    }
                }
                catch { /* the parameter is optional */ }

                return data;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"[RevitGeoref] Could not read project position: {ex.Message}");
                return null;
            }
        }
    }
}
