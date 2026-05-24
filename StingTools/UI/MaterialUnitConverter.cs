using System;
using System.Collections.Generic;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// D6 — Unit conversion + locale-aware parsing for material values.
    ///
    /// STING stores everything in SI internally (kg/m³ density, W/m·K
    /// thermal conductivity, kgCO₂e/m³ embodied carbon). Display switches
    /// based on the active <see cref="MaterialLocale"/>. The risk closed
    /// by this module: a user typing "150" thinking lb/ft³ but having
    /// the cell store it as 150 kg/m³ (12× error).
    ///
    /// Two surfaces
    ///   - <see cref="ToDisplay"/> / <see cref="FromDisplay"/> — round-trip
    ///     a stored SI value through the locale-presented unit.
    ///   - <see cref="ConvertProjectMaterials"/> — one-shot bulk conversion
    ///     offered when the user flips Region (e.g. UK → US) so existing
    ///     SI values can be re-stored as US-locale values if the project
    ///     wants Imperial canon.
    ///
    /// The default behaviour is "display-only" conversion: storage stays
    /// SI, the user sees lb/ft³ in US locale. ConvertProjectMaterials is
    /// opt-in via the locale change dialog.
    /// </summary>
    public static class MaterialUnitConverter
    {
        // ── Density (SI: kg/m³ ; Imperial: lb/ft³) ─────────────────────────
        public const double KgM3_PerLbFt3 = 16.018463374;

        // ── Thermal conductivity (SI: W/m·K ; Imperial: Btu·in/h·ft²·°F) ───
        public const double WmK_PerBtuInHrFt2F = 0.1441314;

        public enum Quantity { Density, ThermalConductivity, Cost, Carbon }

        /// <summary>Convert an SI value to the locale's display value.</summary>
        public static double ToDisplay(double siValue, Quantity q, MaterialLocale locale)
        {
            if (locale == null) return siValue;
            switch (q)
            {
                case Quantity.Density:
                    return locale.Region == MaterialRegion.US ? siValue / KgM3_PerLbFt3 : siValue;
                case Quantity.ThermalConductivity:
                    return locale.Region == MaterialRegion.US ? siValue / WmK_PerBtuInHrFt2F : siValue;
                case Quantity.Cost:    // Cost is currency, not unit-converted. FX is the BOQ's job.
                case Quantity.Carbon:  // kgCO2e is universal.
                default:
                    return siValue;
            }
        }

        /// <summary>Convert a locale-display value back to SI for storage.</summary>
        public static double FromDisplay(double displayValue, Quantity q, MaterialLocale locale)
        {
            if (locale == null) return displayValue;
            switch (q)
            {
                case Quantity.Density:
                    return locale.Region == MaterialRegion.US ? displayValue * KgM3_PerLbFt3 : displayValue;
                case Quantity.ThermalConductivity:
                    return locale.Region == MaterialRegion.US ? displayValue * WmK_PerBtuInHrFt2F : displayValue;
                default:
                    return displayValue;
            }
        }

        /// <summary>Return the unit label for the current locale + quantity.
        /// Used in column headers + tooltips so the user always knows what
        /// the cell expects.</summary>
        public static string UnitLabel(Quantity q, MaterialLocale locale)
        {
            if (locale == null) return "";
            switch (q)
            {
                case Quantity.Density:
                    return locale.Region == MaterialRegion.US ? "lb/ft³" : "kg/m³";
                case Quantity.ThermalConductivity:
                    return locale.Region == MaterialRegion.US ? "Btu·in/h·ft²·°F" : "W/m·K";
                case Quantity.Cost: return locale.CurrencySymbol ?? "";
                case Quantity.Carbon: return "kgCO₂e";
                default: return "";
            }
        }

        /// <summary>
        /// Sanity-check a free-text value parsed in the active locale
        /// before it's stored as SI. Returns true if the value looks
        /// plausible. Used as a guard against the "typed 150 thinking
        /// lb/ft³ but locale is metric" class of bug.
        ///
        /// Density: 10 ≤ kg/m³ ≤ 25000.
        /// Thermal conductivity: 0.005 ≤ W/m·K ≤ 500.
        /// Outside the range → caller surfaces a warning.
        /// </summary>
        public static bool IsPlausibleSI(double siValue, Quantity q)
        {
            switch (q)
            {
                case Quantity.Density:
                    return siValue >= 10 && siValue <= 25000;
                case Quantity.ThermalConductivity:
                    return siValue >= 0.005 && siValue <= 500;
                default: return true;
            }
        }
    }
}
