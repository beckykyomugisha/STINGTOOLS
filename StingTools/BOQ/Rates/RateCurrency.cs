// ══════════════════════════════════════════════════════════════════════════
//  RateCurrency.cs — the ONE currency convention for the rate chain (CA-1).
//
//  Document-free so the currency invariants are pinned by headless tests. The
//  project BASE currency is UGX; every rate flows to UGX via the single
//  doc-scoped FX (UGX_PER_USD / UGX_PER_GBP), then out to a presentation
//  currency from the SAME FX. Two safety rules that close the ~3,700× silent
//  errors:
//
//    1. A missing / blank currency defaults to the BASE (UGX) — never GBP, so
//       a source that forgets to declare its currency cannot trigger a ×4,700.
//    2. An UNKNOWN currency is treated as BASE (UGX) rather than guessed, so it
//       passes through 1:1 instead of being silently multiplied.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.BOQ.Rates
{
    internal static class RateCurrency
    {
        /// <summary>Project base currency — everything reconciles to this.</summary>
        public const string Base = "UGX";

        /// <summary>Blank / null → base (UGX). Trimmed + upper-cased otherwise.</summary>
        public static string Normalize(string code)
            => string.IsNullOrWhiteSpace(code) ? Base : code.Trim().ToUpperInvariant();

        /// <summary>Convert a rate in <paramref name="sourceCurrency"/> to UGX
        /// using the one doc-scoped FX. Unknown currency ⇒ treated as UGX (1:1),
        /// never silently scaled.</summary>
        public static double ToUgx(double rate, string sourceCurrency,
            double ugxPerUsd, double ugxPerGbp)
        {
            switch (Normalize(sourceCurrency))
            {
                case "UGX": return rate;
                case "USD": return rate * SafeFx(ugxPerUsd, 3700.0);
                case "GBP": return rate * SafeFx(ugxPerGbp, 4700.0);
                // Unknown currency ⇒ treat as base (UGX), 1:1. Never silently
                // scaled. (The registry's ConvertCurrency logs the occurrence.)
                default: return rate;
            }
        }

        /// <summary>Convert a UGX rate out to <paramref name="targetCurrency"/>
        /// using the SAME doc-scoped FX. Unknown target ⇒ UGX (1:1).</summary>
        public static double FromUgx(double ugx, string targetCurrency,
            double ugxPerUsd, double ugxPerGbp)
        {
            switch (Normalize(targetCurrency))
            {
                case "UGX": return ugx;
                case "USD": { double fx = SafeFx(ugxPerUsd, 3700.0); return fx > 0 ? ugx / fx : 0; }
                case "GBP": { double fx = SafeFx(ugxPerGbp, 4700.0); return fx > 0 ? ugx / fx : 0; }
                default: return ugx;
            }
        }

        /// <summary>Source → target via UGX as the pivot, one FX pair throughout.</summary>
        public static double Convert(double rate, string sourceCurrency, string targetCurrency,
            double ugxPerUsd, double ugxPerGbp)
        {
            if (string.Equals(Normalize(sourceCurrency), Normalize(targetCurrency), StringComparison.Ordinal))
                return rate;
            double ugx = ToUgx(rate, sourceCurrency, ugxPerUsd, ugxPerGbp);
            return FromUgx(ugx, targetCurrency, ugxPerUsd, ugxPerGbp);
        }

        private static double SafeFx(double fx, double fallback) => fx > 0 ? fx : fallback;
    }
}
