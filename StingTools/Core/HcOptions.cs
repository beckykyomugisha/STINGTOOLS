using System;
using System.Globalization;
using System.Collections.Generic;

namespace StingTools.Core
{
    // ── HcOptions ──────────────────────────────────────────────────────
    //
    // Typed reader for the Hc.* ExtraParams flushed by the Healthcare
    // dock-panel tab (StingDockPanel.SetHealthcareOptions). One central
    // place so command code, validators, and orchestrators don't each
    // re-implement the parse + fallback logic.
    //
    // Flow:
    //   WPF control → SetExtraParam("Hc.<Key>", value)        (panel side)
    //   Command/validator → HcOptions.<Key>                   (read side)
    //
    // Every getter has a sensible fallback so legacy callers that never
    // opened the Healthcare tab still get reasonable defaults.
    public static class HcOptions
    {
        // ── Sticky context bar ─────────────────────────────────────────
        public static string FacilityType
            => Get("Hc.FacilityType", "ACUTE");

        /// <summary>"Project" / "ActiveView" / "Selection" — defaults Project.</summary>
        public static string Scope
            => Get("Hc.Scope", "Project");

        public static bool SkipUnclassified => GetBool("Hc.SkipUnclassified", true);
        public static bool IncludeLinks     => GetBool("Hc.IncludeLinks",     false);

        /// <summary>Comma-separated validator keys ticked on the panel.
        /// Empty string ⇒ "no filter, run them all".</summary>
        public static string SelectedValidatorsCsv
            => Get("Hc.SelectedValidators", "");

        public static HashSet<string> SelectedValidators()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string csv = SelectedValidatorsCsv;
            if (string.IsNullOrWhiteSpace(csv)) return set;
            foreach (string raw in csv.Split(','))
            {
                string s = raw?.Trim();
                if (!string.IsNullOrEmpty(s)) set.Add(s);
            }
            return set;
        }

        // ── Validator thresholds ───────────────────────────────────────
        public static double DpMinPa        => GetDouble("Hc.DpMinPa",        2.5);
        public static double AchMin         => GetDouble("Hc.AchMin",         12.0);
        public static bool   AnteroomStrict => GetBool  ("Hc.AnteroomStrict", true);
        public static double DeadLegMaxM    => GetDouble("Hc.DeadLegMaxM",    1.0);
        public static int    AdjacencyDepth => (int)GetDouble("Hc.AdjacencyDepth", 3);
        public static int    EndoMinReaders => (int)GetDouble("Hc.EndoMinReaders", 4);
        public static int    UpsMaxAgeYrs   => (int)GetDouble("Hc.UpsMaxAgeYrs",   5);
        public static int    IotStaleMins   => (int)GetDouble("Hc.IotStaleMins",   30);
        public static bool   RadRequireQe   => GetBool  ("Hc.RadRequireQe", true);

        // ── MGPS ───────────────────────────────────────────────────────
        public static string MgasGas        => Get   ("Hc.Mgas.Gas", "O2");
        public static string MgasZone       => Get   ("Hc.Mgas.Zone", "");
        public static string MgasVerifier   => Get   ("Hc.Mgas.Verifier", "");
        public static int    MgasStep       => (int)GetDouble("Hc.Mgas.Step", 1);
        public static bool   MgasSignAtEnd  => GetBool("Hc.Mgas.SignAtEnd", true);

        // ── Radiation ──────────────────────────────────────────────────
        public static string RadCalcType    => Get   ("Hc.Rad.CalcType", "CT");
        public static double RadKvp         => GetDouble("Hc.Rad.Kvp", 150);
        public static double RadW           => GetDouble("Hc.Rad.W",   600);
        public static double RadU           => GetDouble("Hc.Rad.U",   0.25);
        public static double RadT           => GetDouble("Hc.Rad.T",   0.5);
        public static double RadD           => GetDouble("Hc.Rad.D",   3.0);
        public static string RadArea        => Get   ("Hc.Rad.Area", "Uncontrolled");
        public static bool   RadAutoApply   => GetBool("Hc.Rad.AutoApply", false);
        public static string RadQeName      => Get   ("Hc.Rad.QeName", "");

        // ── MRI ────────────────────────────────────────────────────────
        public static string MriZone        => Get   ("Hc.Mri.Zone", "Z1");
        public static bool   MriFaradayFlag => GetBool("Hc.Mri.FaradayFlag", false);

        // ── RDS ────────────────────────────────────────────────────────
        public static string RdsClassFilter => Get   ("Hc.Rds.ClassFilter", "All");
        public static string RdsSearch      => Get   ("Hc.Rds.Search", "");
        public static bool   RdsMissingOnly => GetBool("Hc.Rds.MissingOnly", false);
        public static string RdsPickedRoomsCsv => Get("Hc.Rds.PickedRooms", "");

        public static HashSet<string> RdsPickedRooms()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string csv = RdsPickedRoomsCsv;
            if (string.IsNullOrWhiteSpace(csv)) return set;
            foreach (string raw in csv.Split(','))
            {
                string s = raw?.Trim();
                if (!string.IsNullOrEmpty(s)) set.Add(s);
            }
            return set;
        }

        // ── Specialist ─────────────────────────────────────────────────
        public static string SpecialistKind => Get("Hc.Specialist.Kind", "HybridOr");

        // HybridOr / CathLab / IR
        public static string HorRoom        => Get   ("Hc.Specialist.Hor.Room", "");
        public static double HorMinAreaM2   => GetDouble("Hc.Specialist.Hor.MinAreaM2", 70.0);
        public static bool   HorIncludeIr   => GetBool("Hc.Specialist.Hor.IncludeIr", false);

        // Pharmacy USP <797> / <800>
        public static string UspStandard    => Get("Hc.Specialist.Usp.Standard", "USP-797");
        public static double UspAchMin      => GetDouble("Hc.Specialist.Usp.AchMin", 30.0);
        public static double UspDpPa        => GetDouble("Hc.Specialist.Usp.DpPa", 2.5);
        public static bool   UspHasBuffer   => GetBool("Hc.Specialist.Usp.HasBuffer", true);
        public static bool   UspHasAnteroom => GetBool("Hc.Specialist.Usp.HasAnteroom", true);

        // Behavioural health
        public static bool   BhUseFgi       => GetBool("Hc.Specialist.Bh.UseFgi", true);
        public static bool   BhUseHbn       => GetBool("Hc.Specialist.Bh.UseHbn", true);
        public static int    BhRiskLevel    => (int)GetDouble("Hc.Specialist.Bh.RiskLevel", 3);

        // Mortuary
        public static int    MortBeds       => (int)GetDouble("Hc.Specialist.Mort.BedCount", 0);
        public static double MortPctBays    => GetDouble("Hc.Specialist.Mort.PctBaysOfBeds", 0.5);

        // Maternity / NICU
        public static bool   MatMaternity   => GetBool("Hc.Specialist.Mat.Maternity", true);
        public static bool   MatNicu        => GetBool("Hc.Specialist.Mat.Nicu", true);
        public static int    MatNrLimit     => (int)GetDouble("Hc.Specialist.Mat.NicuNrLimit", 35);

        // HSDU
        public static string HsduRoom        => Get("Hc.Specialist.Hsdu.Room", "");
        public static bool   HsduWashCheck   => GetBool("Hc.Specialist.Hsdu.Wash", true);
        public static bool   HsduPackCheck   => GetBool("Hc.Specialist.Hsdu.Pack", true);
        public static bool   HsduSterileCheck => GetBool("Hc.Specialist.Hsdu.Sterile", true);

        // Dialysis
        public static int    DialStations         => (int)GetDouble("Hc.Specialist.Dial.Stations", 0);
        public static bool   DialRoLoopRequired   => GetBool("Hc.Specialist.Dial.RequireRoLoop", true);

        // Hyperbaric / Cytotoxic / IVF
        /// <summary>"HBO" / "Cytotoxic" / "IVF".</summary>
        public static string HboMode        => Get("Hc.Specialist.Hbo.Mode", "HBO");
        public static bool   HboRequireNfpa14 => GetBool("Hc.Specialist.Hbo.RequireNfpa14", true);

        // ── Workflow ───────────────────────────────────────────────────
        public static string WorkflowPreset => Get   ("Hc.Wf.Preset", "");
        public static bool   WorkflowDryRun => GetBool("Hc.Wf.DryRun", false);
        public static bool   WorkflowStopOnFail => GetBool("Hc.Wf.StopOnFail", true);

        // ── Cancel signal ──────────────────────────────────────────────
        public static bool CancelRequested => GetBool("Hc.CancelRequested", false);
        public static void ClearCancel()
            => StingTools.UI.StingCommandHandler.SetExtraParam("Hc.CancelRequested", "0");

        // ── Underlying readers ─────────────────────────────────────────
        private static string Get(string key, string fallback)
        {
            string v = StingTools.UI.StingCommandHandler.GetExtraParam(key);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        private static bool GetBool(string key, bool fallback)
        {
            string v = StingTools.UI.StingCommandHandler.GetExtraParam(key);
            if (string.IsNullOrEmpty(v)) return fallback;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static double GetDouble(string key, double fallback)
        {
            string v = StingTools.UI.StingCommandHandler.GetExtraParam(key);
            if (string.IsNullOrEmpty(v)) return fallback;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
        }
    }
}
