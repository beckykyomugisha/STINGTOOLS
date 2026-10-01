// StingTools — Water fixture aggregator (WS A4 / D3).
//
// Turns per-fixture readings off the model (OST_PlumbingFixtures) into a single
// design FixtureFlows set: the count-weighted mean per kind. A fixture contributes
// a flush volume (WC/urinal) or a flow rate (taps/showers/kitchen). Kinds with no
// real reading inherit the resolved BASELINE flow so they claim zero efficiency
// saving - never a fabricated one. When nothing is read, BuildOrNull returns null
// and the caller keeps the honest "indicative default" flag.
//
// Classification is FixtureFlowReader.ClassifyKind - the one classifier. This
// class used to carry a second, disagreeing keyword classifier; it went in
// DSCH-46b, when SustainabilityEngine.ReadDesignFixtureFlows was wired to this
// aggregator (it had been building its own flows and giving an unrated kind the
// class low-flow default).
//
// Pure POCO - no Revit dependency. Unit-tested. The Revit-facing reader
// (collector + parameter / MEP-connector extraction) lives in SustainabilityEngine
// and only feeds raw numbers in here.

using System;
using System.Collections.Generic;

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

        /// <summary>Add one fixture's rating: litres-per-flush for a WC / urinal,
        /// litres-per-minute for a basin tap / shower / kitchen tap. The kind comes
        /// from FixtureFlowReader.ClassifyKind; an Unknown kind or a non-positive
        /// value is not a reading.</summary>
        public void Add(FixtureKind kind, double value)
        {
            switch (kind)
            {
                case FixtureKind.Wc:         AddWcFlushLpf(value); break;
                case FixtureKind.Urinal:     AddUrinalFlushLpf(value); break;
                case FixtureKind.Basin:      AddBasinTapLpm(value); break;
                case FixtureKind.Shower:     AddShowerLpm(value); break;
                case FixtureKind.KitchenTap: AddKitchenTapLpm(value); break;
            }
        }

        /// <summary>The note the water estimate shows: which kinds were read from
        /// the model (value and fixture count) and which kept the baseline flow.
        /// <paramref name="built"/> is the BuildOrNull result.</summary>
        public string Summary(FixtureFlows built)
        {
            if (built == null || ReadingCount == 0)
                return "No fixture flow rating was read from the model.";
            var got = new List<string>();
            var kept = new List<string>();
            void One(string name, int n, double v, string unit)
            {
                if (n > 0) got.Add($"{name} {v:0.#} {unit} ({n} fixture{(n == 1 ? "" : "s")})");
                else kept.Add(name);
            }
            One("WC", _wcN, built.WcLpf, "L/flush");
            One("urinal", _urN, built.UrinalLpf, "L/flush");
            One("basin tap", _basinN, built.BasinTapLpm, "L/min");
            One("shower", _showerN, built.ShowerLpm, "L/min");
            One("kitchen tap", _kitchenN, built.KitchenTapLpm, "L/min");
            string s = "Design fixture flows read from the model: " + string.Join(", ", got) + ".";
            if (kept.Count > 0)
                s += " No rating read for " + string.Join(", ", kept)
                   + " - those keep the baseline flow, so they claim no saving.";
            return s;
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
