// StingTools — Water fixture aggregator (WS A4 / D3).
//
// Turns per-fixture readings off the model (OST_PlumbingFixtures) into a single
// design FixtureFlows set. Classification is by fixture NAME keyword (data, not a
// code branch on building type); a fixture contributes a flush volume (WC/urinal)
// or a flow rate (taps/showers/kitchen). Categories with no real reading inherit
// the resolved baseline flow so they claim zero efficiency saving — never a
// fabricated one. When nothing is read, BuildOrNull returns null and the caller
// keeps the honest "indicative default" flag.
//
// Pure POCO — no Revit dependency. Unit-tested. The Revit-facing reader
// (collector + parameter / MEP-connector extraction) lives in SustainabilityEngine
// and only feeds raw numbers in here.

using System;

namespace StingTools.Core.Sustainability
{
    public class WaterFixtureAggregator
    {
        private double _wcSum, _urSum, _basinSum, _showerSum, _kitchenSum;
        private int    _wcN, _urN, _basinN, _showerN, _kitchenN;

        /// <summary>Total real readings accumulated (0 ⇒ no model fixture data).</summary>
        public int ReadingCount => _wcN + _urN + _basinN + _showerN + _kitchenN;

        public void AddWcFlushLpf(double lpf)        { if (lpf > 0) { _wcSum += lpf; _wcN++; } }
        public void AddUrinalFlushLpf(double lpf)    { if (lpf > 0) { _urSum += lpf; _urN++; } }
        public void AddBasinTapLpm(double lpm)       { if (lpm > 0) { _basinSum += lpm; _basinN++; } }
        public void AddShowerLpm(double lpm)         { if (lpm > 0) { _showerSum += lpm; _showerN++; } }
        public void AddKitchenTapLpm(double lpm)     { if (lpm > 0) { _kitchenSum += lpm; _kitchenN++; } }

        /// <summary>Classify a fixture by name and add either its flush volume
        /// (litres-per-flush, WC/urinal) or its flow rate (L/min, taps/showers).
        /// Pass whichever value the model carries; the other may be 0.</summary>
        public void AddByName(string fixtureName, double flushLitres, double flowLpm)
        {
            switch (Classify(fixtureName))
            {
                case FixtureKind.Wc:       if (flushLitres > 0) AddWcFlushLpf(flushLitres); break;
                case FixtureKind.Urinal:   if (flushLitres > 0) AddUrinalFlushLpf(flushLitres); break;
                case FixtureKind.Shower:   if (flowLpm > 0) AddShowerLpm(flowLpm); break;
                case FixtureKind.Basin:    if (flowLpm > 0) AddBasinTapLpm(flowLpm); break;
                case FixtureKind.Kitchen:  if (flowLpm > 0) AddKitchenTapLpm(flowLpm); break;
            }
        }

        public enum FixtureKind { Unknown, Wc, Urinal, Basin, Shower, Kitchen }

        /// <summary>Keyword classification of a plumbing-fixture name.</summary>
        public static FixtureKind Classify(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Length == 0) return FixtureKind.Unknown;
            if (n.Contains("urinal")) return FixtureKind.Urinal;
            if (n.Contains("wc") || n.Contains("toilet") || n.Contains("water closet")
                || n.Contains("cistern") || n.Contains("closet")) return FixtureKind.Wc;
            if (n.Contains("shower")) return FixtureKind.Shower;
            if (n.Contains("kitchen") || n.Contains("sink")) return FixtureKind.Kitchen;
            if (n.Contains("basin") || n.Contains("lavatory") || n.Contains("lav ")
                || n.Contains("hand wash") || n.Contains("handwash") || n.Contains("wash hand")
                || n.Contains("whb")) return FixtureKind.Basin;
            return FixtureKind.Unknown;
        }

        /// <summary>Build the averaged design flows, falling back per-category to the
        /// resolved baseline for any fixture type the model didn't carry (so an
        /// unread category claims no saving). Returns null when no real reading was
        /// taken — the caller then uses the indicative default and flags it honestly.</summary>
        public FixtureFlows BuildOrNull(FixtureFlows fallback)
        {
            if (ReadingCount == 0) return null;
            fallback = fallback ?? new FixtureFlows();
            return new FixtureFlows
            {
                WcLpf         = _wcN      > 0 ? _wcSum / _wcN           : fallback.WcLpf,
                UrinalLpf     = _urN      > 0 ? _urSum / _urN           : fallback.UrinalLpf,
                BasinTapLpm   = _basinN   > 0 ? _basinSum / _basinN     : fallback.BasinTapLpm,
                ShowerLpm     = _showerN  > 0 ? _showerSum / _showerN   : fallback.ShowerLpm,
                KitchenTapLpm = _kitchenN > 0 ? _kitchenSum / _kitchenN : fallback.KitchenTapLpm
            };
        }
    }
}
