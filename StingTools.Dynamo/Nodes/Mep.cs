// STING Tools — MEP category nodes (Phase 109).

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>
    /// MEP intelligence + auto-size + QA nodes surfaced from the
    /// CIBSE-aligned MEPIntelligenceEngine.
    /// </summary>
    public static class Mep
    {
        // ── Intelligence ──
        /// <summary>Darcy-Weisbach + Swamee-Jain pressure drop analysis.</summary>
        [NodeCategory("STING Tools.MEP.Intelligence")]
        public static bool PressureDropAnalyse() => StingDispatcher.Dispatch("Mep_PressureDrop");

        /// <summary>Per-fitting Kv + equivalent length report.</summary>
        [NodeCategory("STING Tools.MEP.Intelligence")]
        public static bool FittingLossReport() => StingDispatcher.Dispatch("Mep_FittingLoss");

        /// <summary>Hardy-Cross iterative flow balancer.</summary>
        [NodeCategory("STING Tools.MEP.Intelligence")]
        public static bool Balance() => StingDispatcher.Dispatch("Mep_Balance");

        /// <summary>CIBSE TG6 vibration + NC criteria check.</summary>
        [NodeCategory("STING Tools.MEP.Intelligence")]
        public static bool VibroAcoustic() => StingDispatcher.Dispatch("Mep_VibroAcoustic");

        /// <summary>Whole-model system analyser.</summary>
        [NodeCategory("STING Tools.MEP.Intelligence")]
        public static bool SystemAnalyse() => StingDispatcher.Dispatch("Mep_SystemAnalyse");

        /// <summary>BFS trace from selected element back to source.</summary>
        [NodeCategory("STING Tools.MEP.Intelligence")]
        public static bool SystemTrace() => StingDispatcher.Dispatch("Mep_SystemTracer");

        // ── Auto-size ──
        /// <summary>Size pipes for ≤ 2.5 m/s velocity, BS EN 10255 bores.</summary>
        [NodeCategory("STING Tools.MEP.AutoSize")]
        public static bool AutoSizePipe() => StingDispatcher.Dispatch("Mep_AutoSizePipe");

        /// <summary>Size ducts for ≤ 6 m/s velocity, SMACNA sizes.</summary>
        [NodeCategory("STING Tools.MEP.AutoSize")]
        public static bool AutoSizeDuct() => StingDispatcher.Dispatch("Mep_AutoSizeDuct");

        /// <summary>Size conduits for ≤ 45% fill (BS 7671 522.8).</summary>
        [NodeCategory("STING Tools.MEP.AutoSize")]
        public static bool AutoSizeConduit() => StingDispatcher.Dispatch("Mep_AutoSizeConduit");

        /// <summary>Run Pipe + Duct + Conduit auto-size together.</summary>
        [NodeCategory("STING Tools.MEP.AutoSize")]
        public static bool AutoSizeAll() => StingDispatcher.Dispatch("Mep_AutoSizeAll");

        // ── Penetrations + QA ──
        /// <summary>Insert sleeve families at every wall / floor / roof crossing.</summary>
        [NodeCategory("STING Tools.MEP.Penetrations")]
        public static bool AutoSleeve() => StingDispatcher.Dispatch("Mep_AutoSleeve");

        /// <summary>Recompute conduit + tray fill% from cable cross section.</summary>
        [NodeCategory("STING Tools.MEP.QA")]
        public static bool FillLiveCalc() => StingDispatcher.Dispatch("Mep_FillLiveCalc");

        /// <summary>Audit MEP system names vs CIBSE / BSRIA / Uniclass 2015.</summary>
        [NodeCategory("STING Tools.MEP.QA")]
        public static bool NamingAudit() => StingDispatcher.Dispatch("Mep_NamingAudit");
    }
}
