using System;
using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using StingTools.Commands.Electrical.VoltageDrop;

namespace StingTools.Core.Electrical
{
    /// <summary>
    /// Revit side of <see cref="CircuitVoltageDrop"/> (ELEC-22): read a circuit's inputs,
    /// resolve its drop, and stamp the number with its basis. Every STING writer of
    /// ELC_VLT_DROP_PCT goes through <see cref="Stamp"/> or <see cref="StampForeign"/>,
    /// so a figure always says how it was obtained.
    /// </summary>
    public static class CircuitVoltageDropModel
    {
        public const string BasisParam = "ELC_CKT_VD_BASIS_TXT";

        /// <summary>The circuit's inputs: Ib, route length (else ELC_CKT_LENGTH_M), voltage,
        /// poles, conductor size (native wire size, else ELC_CKT_CSA_MM2) and its cable record.</summary>
        public static CircuitVdInput Read(ElectricalSystem sys, string standard, string material = "Cu")
        {
            var i = new CircuitVdInput { Standard = standard ?? "BS7671", Material = string.IsNullOrWhiteSpace(material) ? "Cu" : material };
            if (sys == null) return i;
            try { i.CurrentA = sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM)?.AsDouble() ?? 0; }
            catch (Exception ex) { StingLog.Warn($"VD current {sys.Id}: {ex.Message}"); }
            try { i.LengthM = sys.Length * 0.3048; }
            catch (Exception ex) { StingLog.Warn($"VD length {sys.Id}: {ex.Message}"); }
            // ELC_CKT_LENGTH_M is TEXT; parsed without thousands separators, so "12,5" is
            // "not known" (NONE: missing length) rather than GetDouble's 125 m.
            if (i.LengthM <= 0
                && double.TryParse((ParameterHelpers.GetValueText(sys, "ELC_CKT_LENGTH_M") ?? "").Trim(), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out double lenM) && lenM > 0)
                i.LengthM = lenM;
            try { i.VoltageV = ElecUnits.Read(sys, BuiltInParameter.RBS_ELEC_VOLTAGE); }
            catch (Exception ex) { StingLog.Warn($"VD voltage {sys.Id}: {ex.Message}"); }
            try { i.Phases = sys.PolesNumber >= 3 ? 3 : 1; }
            catch (Exception ex) { StingLog.Warn($"VD poles {sys.Id}: {ex.Message}"); }
            try { i.CsaMm2 = WireSizeParser.ParseCsaMm2(sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM)?.AsString() ?? ""); }
            catch (Exception ex) { StingLog.Warn($"VD wire size {sys.Id}: {ex.Message}"); }
            // ELC_CBL_SZ_MM (ParamRegistry.ELC_CKT_CSA_MM2) is TEXT. GetDouble parsed it with
            // thousands separators allowed, so "2,5" (a comma-decimal locale) read as 25 mm².
            if (i.CsaMm2 <= 0) i.CsaMm2 = WireSizeParser.ParseCsaMm2(ParameterHelpers.GetValueText(sys, "ELC_CBL_SZ_MM"));
            var rec = CircuitCableRecord.Read(sys);
            i.Insulation = rec.Insulation; i.InstallMethod = rec.InstallMethod; i.CableType = rec.CableType;
            return i;
        }

        /// <summary>BS EN 60228 resistance at the operating temperature — the NEC path only.</summary>
        public static Func<CircuitVdInput, double> Resistance(double operatingTempC = 70.0)
            => i => VoltageDropEngine.CalculateVoltDropPercent(i.CurrentA, i.LengthM, i.CsaMm2,
                                                              i.Material, i.VoltageV, i.Phases, operatingTempC);

        public static CircuitVdResult Compute(ElectricalSystem sys, Bs7671Data data, string standard,
            string material = "Cu", double operatingTempC = 70.0)
            => CircuitVoltageDrop.Resolve(Read(sys, standard, material), data, Resistance(operatingTempC));

        /// <summary>
        /// Write the drop and its basis. With no value the number is cleared where the
        /// parameter allows it, and the basis says NONE and why — never a 0.00 that reads
        /// as a pass. Returns true when the basis was written.
        /// </summary>
        public static bool Stamp(Element el, CircuitVdResult r)
        {
            if (el == null || r == null) return false;
            if (r.HasValue)
                ParameterHelpers.SetString(el, "ELC_VLT_DROP_PCT", r.Pct.ToString("0.00", CultureInfo.InvariantCulture), overwrite: true);
            else
                TryClear(el.LookupParameter("ELC_VLT_DROP_PCT"));
            // ELEC-26: the text mirror is what STING's schedules show. Unlike the number it can
            // always be overwritten, so a NONE never leaves an old figure on view.
            ParameterHelpers.SetString(el, "ELC_VLT_DROP_TXT", CircuitVoltageDrop.DisplayText(r), overwrite: true);
            return ParameterHelpers.SetString(el, "ELC_CKT_VD_BASIS_TXT", r.Stamp, overwrite: true);
        }

        /// <summary>Stamp a figure STING did not resolve itself (a sizer's own result on a
        /// board, or an imported value), with the basis the caller gives.</summary>
        public static bool StampForeign(Element el, double pct, string basis)
        {
            if (el == null) return false;
            ParameterHelpers.SetString(el, "ELC_VLT_DROP_PCT", pct.ToString("0.00", CultureInfo.InvariantCulture), overwrite: true);
            ParameterHelpers.SetString(el, "ELC_VLT_DROP_TXT", pct.ToString("0.00", CultureInfo.InvariantCulture), overwrite: true);
            return ParameterHelpers.SetString(el, "ELC_CKT_VD_BASIS_TXT", basis ?? "", overwrite: true);
        }

        /// <summary>What is stamped: the value (null when absent), its method, and the basis text.</summary>
        public static (double? Pct, VdMethod Method, string Basis) ReadStamp(Element el)
        {
            if (el == null) return (null, VdMethod.None, "");
            string basis = ParameterHelpers.GetString(el, "ELC_CKT_VD_BASIS_TXT") ?? "";
            var method = CircuitVoltageDrop.ParseMethod(basis);
            double? pct = null;
            try
            {
                var p = el.LookupParameter("ELC_VLT_DROP_PCT");
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.Double) pct = p.AsDouble();
                    else if (p.StorageType == StorageType.String
                             && NumberText.TryParse(p.AsString(), out double v)) pct = v;
                }
            }
            catch (Exception ex) { StingLog.Warn($"VD read {el.Id}: {ex.Message}"); }
            // A NONE basis beside a number is a stale figure the parameter could not clear.
            if (method == VdMethod.None) pct = null;
            return (pct, method, basis);
        }

        /// <summary>
        /// A conduit's own inputs (ELEC-23): design current <c>ELC_WIRE_MAX_DEMAND_A</c>
        /// (else the connected circuit's current), this segment's length, conductor size
        /// <c>ELC_WIRE_CSA_MM2_NUM</c>, material, and the cable recorded on the conduit
        /// (<c>ELC_WIRE_INSTALL_METHOD_TXT</c> or <c>ELC_CBL_INSTALL_METHOD_TXT</c>,
        /// <c>ELC_CBL_INS_TYPE_TXT</c>, <c>ELC_CBL_TYPE_TXT</c>). Voltage and phases come
        /// from the connected circuit; with no circuit the nominal 400 V / 230 V is used and
        /// <paramref name="voltageAssumed"/> says so. Nothing else is assumed: an incomplete
        /// cable record gives the A4-MAX upper bound, not a guessed table.
        /// </summary>
        public static CircuitVdInput ReadConduit(Element conduit, ElectricalSystem circuit, string standard,
            out bool voltageAssumed)
        {
            voltageAssumed = false;
            var i = new CircuitVdInput { Standard = standard ?? "BS7671" };
            if (conduit == null) return i;
            string mat = ParameterHelpers.GetString(conduit, "ELC_WIRE_COND_MAT_TXT");
            i.Material = mat != null && mat.Trim().StartsWith("Al", StringComparison.OrdinalIgnoreCase) ? "Al" : "Cu";
            i.CsaMm2 = ParameterHelpers.GetDouble(conduit, "ELC_WIRE_CSA_MM2_NUM");
            i.CurrentA = ParameterHelpers.GetDouble(conduit, "ELC_WIRE_MAX_DEMAND_A");
            try { if (conduit.Location is LocationCurve lc && lc.Curve != null) i.LengthM = lc.Curve.Length * 0.3048; }
            catch (Exception ex) { StingLog.Warn($"VD conduit length {conduit.Id}: {ex.Message}"); }

            int poles = 0;
            if (circuit != null)
            {
                try { if (i.CurrentA <= 0) i.CurrentA = circuit.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM)?.AsDouble() ?? 0; }
                catch (Exception ex) { StingLog.Warn($"VD conduit circuit current {conduit.Id}: {ex.Message}"); }
                try { i.VoltageV = ElecUnits.Read(circuit, BuiltInParameter.RBS_ELEC_VOLTAGE); }
                catch (Exception ex) { StingLog.Warn($"VD conduit circuit voltage {conduit.Id}: {ex.Message}"); }
                try { poles = circuit.PolesNumber; }
                catch (Exception ex) { StingLog.Warn($"VD conduit circuit poles {conduit.Id}: {ex.Message}"); }
            }
            string phase = ParameterHelpers.GetString(conduit, "ELC_WIRE_PHASE_TXT");
            i.Phases = poles >= 3 || (poles == 0 && phase != null && phase.Contains("3")) ? 3 : 1;
            if (i.VoltageV <= 0) { i.VoltageV = i.Phases == 3 ? 400.0 : 230.0; voltageAssumed = true; }

            string method = ParameterHelpers.GetString(conduit, "ELC_WIRE_INSTALL_METHOD_TXT");
            if (string.IsNullOrWhiteSpace(method)) method = ParameterHelpers.GetString(conduit, "ELC_CBL_INSTALL_METHOD_TXT");
            i.InstallMethod = method?.Trim();
            i.Insulation = ParameterHelpers.GetString(conduit, "ELC_CBL_INS_TYPE_TXT")?.Trim();
            i.CableType = ParameterHelpers.GetString(conduit, "ELC_CBL_TYPE_TXT")?.Trim();
            return i;
        }

        private static void TryClear(Parameter p)
        {
            if (p == null || !p.HasValue || p.IsReadOnly) return;
            try { p.ClearValue(); }
            catch (Exception ex) { StingLog.Info($"VD value not cleared (the basis says NONE): {ex.Message}"); }
        }
    }
}
