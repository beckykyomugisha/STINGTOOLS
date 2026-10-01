// StingTools — Electrical calc commands inside a workflow preset
//
// The Calc_* and Cable_Calculate commands read their inputs from the Electrical
// dock panel. Clicked from that panel, StingElectricalCommandHandler snapshots the
// panel into its Current* statics just before the command runs. Run as a workflow
// step (WorkflowEngine) nothing takes that snapshot, so the statics hold whatever
// the panel showed the last time one of its own buttons was clicked — or nothing.
//
// Inside a preset these helpers therefore:
//   1. re-read the panel when it is open (RefreshFromPanel — the same reads the
//      panel's dispatcher makes), so the step uses what the panel shows NOW;
//   2. fall back to the documented defaults when no panel was ever opened;
//   3. overlay the step's params (WorkflowEngine.StepParam) on a COPY — a step
//      never changes what the panel's own buttons will use next.
// Engineering inputs that have no defensible default (a cable's load and length,
// the upstream fault level) are never invented: without the panel or a param the
// step fails and names the param.
//
// Outside a preset nothing here is called; the commands behave as they always have.

using System;
using System.Collections.Generic;
using StingTools.Commands.Electrical.FeederSizing;
using StingTools.Core;
using StingTools.Standards;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    internal static class ElectricalStepInputs
    {
        /// <summary>params.breakerStandard / Calc_SizeBreakers params.standard.</summary>
        internal static readonly IReadOnlyList<PresetStepInputs.Choice> BreakerStandards = new[]
        {
            new PresetStepInputs.Choice("BS_MCB", "mcb", "bsen60898", "60898", "bs"),
            new PresetStepInputs.Choice("BS_MCCB", "mccb", "bsen609472", "609472"),
            new PresetStepInputs.Choice("NEC", "nec2023", "necocpd", "ocpd"),
        };

        internal static readonly IReadOnlyList<PresetStepInputs.Choice> Materials = new[]
        {
            new PresetStepInputs.Choice("Cu", "copper"),
            new PresetStepInputs.Choice("Al", "aluminium", "aluminum"),
        };

        internal static bool PanelOpen => StingElectricalCommandHandler.ActivePanel != null;

        /// <summary>
        /// Re-read the Electrical panel into the handler's Current* statics — what the
        /// panel's own dispatcher does before every button. No-op when the panel was never
        /// opened. Only ever called inside a preset.
        /// </summary>
        internal static void RefreshFromPanel()
        {
            var p = StingElectricalCommandHandler.ActivePanel;
            if (p == null) return;
            try
            {
                void Read()
                {
                    StingElectricalCommandHandler.CurrentCableSizeInput = p.ReadCableSizerInputs();
                    StingElectricalCommandHandler.CurrentVDOptions = p.ReadVDOptions();
                    StingElectricalCommandHandler.CurrentBreakerOptions = p.ReadBreakerOptions();
                    StingElectricalCommandHandler.CurrentFeederSettings = p.ReadFeederSettings();
                }
                if (p.Dispatcher.CheckAccess()) Read(); else p.Dispatcher.Invoke(Read);
            }
            catch (Exception ex) { StingLog.Warn($"ElectricalStepInputs: panel re-read failed, using the last snapshot: {ex.Message}"); }
        }

        /// <summary>params.standard over <paramref name="current"/>; an unrecognised spelling is an error.</summary>
        internal static bool Standard(string current, out string standard, out string error)
        {
            error = null;
            string raw = WorkflowEngine.StepParam("standard");
            if (string.IsNullOrWhiteSpace(raw)) { standard = current; return true; }
            if (!ElectricalStandardId.IsRecognised(raw))
            {
                standard = null;
                error = $"params.standard = '{raw.Trim()}' is not a recognised electrical standard (BS7671 | NEC2023 | IEC60364 | ASNZS3000).";
                return false;
            }
            standard = ElectricalStandardId.Normalise(raw);
            return true;
        }

        private static bool Number(string name, double min, double max, ref double target, ref string error)
        {
            if (error != null) return false;
            if (!PresetStepInputs.TryNumber(name, WorkflowEngine.StepParam(name), min, max, out var v, out var err))
            { error = err; return false; }
            if (v.HasValue) target = v.Value;
            return true;
        }

        private static bool Text(string name, ref string target, ref string error)
        {
            if (error != null) return false;
            string raw = WorkflowEngine.StepParam(name);
            if (!string.IsNullOrWhiteSpace(raw)) target = raw.Trim();
            return true;
        }

        private static bool Choice(string name, IReadOnlyList<PresetStepInputs.Choice> choices, ref string target, ref string error)
        {
            if (error != null) return false;
            if (!PresetStepInputs.TryChoice(name, WorkflowEngine.StepParam(name), target, choices, out var v, out var err))
            { error = err; return false; }
            target = v;
            return true;
        }

        /// <summary>
        /// Calc_VoltageDrop / Calc_FlagVD. Params: lightingLimitPct, otherLimitPct, material
        /// (Cu | Al), operatingTempC, standard. Defaults: the panel's VOLTAGE DROP expander,
        /// else 3 % lighting / 5 % other (BS 7671 App 12), Cu, 70 °C, the panel's standard.
        /// </summary>
        internal static bool VdOptions(VDOptionsSnapshot fallback, out VDOptionsSnapshot opts, out string error)
        {
            RefreshFromPanel();
            var b = StingElectricalCommandHandler.CurrentVDOptions ?? fallback;
            opts = new VDOptionsSnapshot
            {
                LightingLimitPct = b.LightingLimitPct, OtherLimitPct = b.OtherLimitPct,
                Material = b.Material, OperatingTempC = b.OperatingTempC, Standard = b.Standard,
            };
            error = null;
            double light = opts.LightingLimitPct, other = opts.OtherLimitPct, temp = opts.OperatingTempC;
            string mat = string.IsNullOrEmpty(opts.Material) ? "Cu" : opts.Material;
            Number("lightingLimitPct", 0.1, 25, ref light, ref error);
            Number("otherLimitPct", 0.1, 25, ref other, ref error);
            Number("operatingTempC", 20, 250, ref temp, ref error);
            Choice("material", Materials, ref mat, ref error);
            if (error != null) { opts = null; return false; }
            if (!Standard(opts.Standard, out var std, out error)) { opts = null; return false; }
            opts.LightingLimitPct = light; opts.OtherLimitPct = other; opts.OperatingTempC = temp;
            opts.Material = mat; opts.Standard = std;
            return true;
        }

        /// <summary>
        /// Calc_SizeBreakers. Params: standard (BS_MCB | BS_MCCB | NEC), continuous (true | false).
        /// Defaults: the panel's BREAKER SIZING expander, else BS_MCB with the continuous factor on
        /// (it only applies under NEC). The In ≤ Iz cable assumptions come from the CABLE tab.
        /// </summary>
        internal static bool BreakerOptions(BreakerOptionsSnapshot fallback, out BreakerOptionsSnapshot opts, out string error)
        {
            RefreshFromPanel();
            var b = StingElectricalCommandHandler.CurrentBreakerOptions ?? fallback;
            string std = string.IsNullOrEmpty(b.Standard) ? "BS_MCB" : b.Standard;
            error = null;
            opts = null;
            if (!Choice("standard", BreakerStandards, ref std, ref error)) return false;
            if (!PresetStepInputs.TryBool("continuous", WorkflowEngine.StepParam("continuous"), b.ContinuousFactor,
                    out bool cont, out error)) return false;
            opts = new BreakerOptionsSnapshot { Standard = std, ContinuousFactor = cont };
            return true;
        }

        /// <summary>
        /// Calc_FeederSize. Params: derateFactor, diversityPct, installMethod, insulation,
        /// cableType, vdLimitPct, standard. Defaults: the panel's FEEDER SIZING expander, else
        /// derate 0.8, diversity 100 %, method C, VD 5 % (BS 7671 App 12 'other'); standard from
        /// the panel (BS 7671 when none).
        /// </summary>
        internal static bool FeederSettings(FeederSettingsSnapshot fallback, out FeederSettingsSnapshot s,
            out string standard, out string error)
        {
            RefreshFromPanel();
            var b = StingElectricalCommandHandler.CurrentFeederSettings ?? fallback;
            s = new FeederSettingsSnapshot
            {
                DerateFactor = b.DerateFactor, DiversityPct = b.DiversityPct, InstallMethod = b.InstallMethod,
                Insulation = b.Insulation, CableType = b.CableType, VDLimitPct = b.VDLimitPct,
                VDLimitUserSet = b.VDLimitUserSet,
            };
            error = null;
            standard = null;
            double derate = s.DerateFactor, div = s.DiversityPct, vd = s.VDLimitPct;
            string method = s.InstallMethod, ins = s.Insulation, ctype = s.CableType;
            Number("derateFactor", 0.05, 1, ref derate, ref error);
            Number("diversityPct", 1, 100, ref div, ref error);
            Text("installMethod", ref method, ref error);
            Text("insulation", ref ins, ref error);
            Text("cableType", ref ctype, ref error);
            if (error == null && !string.IsNullOrWhiteSpace(WorkflowEngine.StepParam("vdLimitPct")))
            {
                Number("vdLimitPct", 0.1, 25, ref vd, ref error);
                s.VDLimitUserSet = true;
            }
            if (error != null) { s = null; return false; }
            s.DerateFactor = derate; s.DiversityPct = div; s.VDLimitPct = vd;
            s.InstallMethod = method; s.Insulation = ins; s.CableType = ctype;
            string panelStd = null;
            try { panelStd = StingElectricalCommandHandler.ActivePanel?.SelectedStandard; }
            catch (Exception ex) { StingLog.Warn($"ElectricalStepInputs standard: {ex.Message}"); }
            if (!Standard(ElectricalStandardId.Normalise(panelStd), out standard, out error)) { s = null; return false; }
            return true;
        }

        /// <summary>
        /// Calc_FaultCurrent. Param: utilityFaultKa (upstream fault level at the origin, kA).
        /// Default: the Electrical panel's field when the panel is open. With neither, the step
        /// fails — a fault level is a site fact from the DNO / utility, not a default.
        /// </summary>
        internal static bool UtilityFaultKa(out double ka, out string error)
        {
            ka = 0;
            if (!PresetStepInputs.TryNumber("utilityFaultKa", WorkflowEngine.StepParam("utilityFaultKa"),
                    0.1, 300, out var v, out error)) return false;
            if (v.HasValue) { ka = v.Value; return true; }
            if (PanelOpen) { ka = StingElectricalCommandHandler.CurrentUtilityFaultKa; return true; }
            error = "needs params.utilityFaultKa — the upstream fault level at the origin in kA, from the " +
                    "utility / DNO (or open the Electrical panel and enter it there).";
            return false;
        }

        /// <summary>
        /// Cable_Calculate. Params: loadKW, voltageV, phases (1 | 3), powerFactor, lengthM,
        /// vdLimitPct, installMethod, material, insulation, cableType, standard. Base: the panel's
        /// CABLE tab when the panel is open. Without the panel, loadKW, voltageV and lengthM are
        /// REQUIRED; the rest default to the command's own defaults (PF 0.85, method C, Cu,
        /// PVC70, Multicore, VD 3 %, BS 7671, single-phase).
        /// </summary>
        internal static bool CableInputs(out CableSizerInputSnapshot c, out string error)
        {
            RefreshFromPanel();
            var b = PanelOpen ? StingElectricalCommandHandler.CurrentCableSizeInput : null;
            c = new CableSizerInputSnapshot
            {
                LoadKW = b?.LoadKW ?? 0, VoltageV = b?.VoltageV ?? 0, Phases = b?.Phases ?? 1,
                PowerFactor = b?.PowerFactor ?? 0.85, LengthM = b?.LengthM ?? 0, VDLimitPct = b?.VDLimitPct ?? 3.0,
                InstallMethod = b?.InstallMethod ?? "C", Material = b?.Material ?? "Cu",
                Insulation = b?.Insulation ?? "PVC70", CableType = b?.CableType ?? "Multicore",
                Standard = b?.Standard ?? ElectricalStandardId.Bs7671,
            };
            error = null;
            double load = c.LoadKW, volt = c.VoltageV, pf = c.PowerFactor, len = c.LengthM, vd = c.VDLimitPct, ph = c.Phases;
            string method = c.InstallMethod, mat = c.Material, ins = c.Insulation, ctype = c.CableType;
            Number("loadKW", 0.001, 100000, ref load, ref error);
            Number("voltageV", 1, 1000, ref volt, ref error);
            Number("phases", 1, 3, ref ph, ref error);
            Number("powerFactor", 0.1, 1, ref pf, ref error);
            Number("lengthM", 0.1, 10000, ref len, ref error);
            Number("vdLimitPct", 0.1, 25, ref vd, ref error);
            Text("installMethod", ref method, ref error);
            Choice("material", Materials, ref mat, ref error);
            Text("insulation", ref ins, ref error);
            Text("cableType", ref ctype, ref error);
            if (error == null && ph != 1 && ph != 3) error = $"params.phases = {ph} must be 1 or 3.";
            if (error != null) { c = null; return false; }
            if (!Standard(c.Standard, out var std, out error)) { c = null; return false; }

            var missing = new List<string>();
            if (load <= 0) missing.Add("loadKW");
            if (volt <= 0) missing.Add("voltageV");
            if (len <= 0) missing.Add("lengthM");
            if (missing.Count > 0)
            {
                error = $"needs params.{string.Join(", params.", missing)} (the Electrical panel is not open to supply them).";
                c = null;
                return false;
            }
            c.LoadKW = load; c.VoltageV = volt; c.Phases = (int)ph; c.PowerFactor = pf; c.LengthM = len;
            c.VDLimitPct = vd; c.InstallMethod = method; c.Material = mat; c.Insulation = ins; c.CableType = ctype;
            c.Standard = std;
            return true;
        }
    }
}
