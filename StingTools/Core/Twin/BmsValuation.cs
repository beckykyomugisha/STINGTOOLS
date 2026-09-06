// ══════════════════════════════════════════════════════════════════════════
//  BmsValuation.cs — Phase H3 (KUT lifecycle, max automation).
//
//  Pure, host-free roll-up of "what fraction of the priced, monitorable BOQ is
//  live on the BMS and therefore certifiable as commissioned" — the 5D valuation
//  signal that feeds the Phase 191 PaymentCert engine. A monitorable asset whose
//  Niagara point is online + reporting a present value is installed and
//  communicating, i.e. it has earned its certificate.
//
//  Zero Autodesk.Revit.* / network dependencies on purpose (unit-tested in
//  StingTools.Boq.Tests). The command (KutValuationFromBmsCommand) walks the model
//  + queries the live station; this class only does the arithmetic + the
//  point-status → commissioned decision.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Twin
{
    public sealed class BmsAsset
    {
        public string Tag = "";
        public string DeviceId = "";
        public double ValueUGX;
        public bool Commissioned;   // resolved from the live point status
        public string Status = "";  // raw Niagara status (ok / fault / down / stale / disabled / …)
    }

    public sealed class BmsValuationResult
    {
        public int MonitorableCount;
        public double MonitorableValueUGX;
        public int CommissionedCount;
        public double CommissionedValueUGX;
        public int NoPointCount;          // priced + monitorable but no BMS device id

        /// <summary>Commissioned share of the monitorable priced value (0..1) — the
        /// valuation percentage offered to the PaymentCert engine for the
        /// monitorable scope.</summary>
        public double CommissionedValueFraction =>
            MonitorableValueUGX > 0 ? CommissionedValueUGX / MonitorableValueUGX : 0.0;
    }

    public static class BmsValuation
    {
        // Niagara / oBIX fault-status strings that mean "the point is in service":
        // an empty status object {} is the Baja convention for OK.
        private static readonly HashSet<string> LiveStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "ok", "", "{ok}", "normal", "online", "active", "inservice", "in service", "good" };

        /// <summary>A point is commissioned when its status is in-service AND it is
        /// reporting a present value (a configured-but-dead point reports a status but
        /// no value). Conservative: anything fault/down/stale/disabled/null ⇒ false.</summary>
        public static bool IsCommissioned(string status, bool hasPresentValue)
        {
            string s = (status ?? "").Trim();
            // Strip Baja decoration like "{ok}" / "{fault}" → "ok"/"fault".
            if (s.StartsWith("{") && s.EndsWith("}") && s.Length >= 2) s = s.Substring(1, s.Length - 2);
            return hasPresentValue && LiveStatuses.Contains(s);
        }

        /// <summary>Roll up a set of priced-monitorable assets. <paramref name="assets"/>
        /// is every priced monitorable BOQ line; assets with no DeviceId count toward
        /// NoPoint (not commissioned).</summary>
        public static BmsValuationResult Compute(IEnumerable<BmsAsset> assets)
        {
            var r = new BmsValuationResult();
            foreach (var a in assets ?? Enumerable.Empty<BmsAsset>())
            {
                if (a == null) continue;
                r.MonitorableCount++;
                r.MonitorableValueUGX += a.ValueUGX;
                if (string.IsNullOrWhiteSpace(a.DeviceId)) { r.NoPointCount++; continue; }
                if (a.Commissioned)
                {
                    r.CommissionedCount++;
                    r.CommissionedValueUGX += a.ValueUGX;
                }
            }
            return r;
        }
    }
}
