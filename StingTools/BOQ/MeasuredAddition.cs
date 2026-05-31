namespace StingTools.BOQ
{
    /// <summary>
    /// Z-23b — discipline-specific MEASURED ADDITIONS for BOQ take-off, kept
    /// strictly SEPARATE from the general cutting/offcut waste
    /// (COST_DEFAULT_WASTE_PCT, applied by <see cref="WasteFactor"/>).
    ///
    /// QS / NRM2 basis (RICS New Rules of Measurement 2 — Detailed Measurement
    /// for Building Works):
    ///   • REBAR LAPS are a MEASURED ADDITION to reinforcement mass, not waste.
    ///     NRM2 measures reinforcement by mass of steel in the finished work;
    ///     laps required by the design are added to that measured quantity.
    ///     Cutting/offcut WASTE is a separate allowance (the project 5%). Laps
    ///     and waste are therefore DISTINCT percentages — adding a lap allowance
    ///     does not re-apply the cutting waste.
    ///   • CONCRETE OVER-ORDER (spillage / over-excavation / pump priming) is a
    ///     procurement BUFFER, not an NRM2-measured quantity (concrete is
    ///     measured net of the finished work; spillage is deemed in the rate /
    ///     the general waste). It is an opt-in buffer, OFF by default.
    ///
    /// Anti-double-count guarantee: both knobs DEFAULT TO 0 (off) — zero
    /// delivered-number change until a project opts in — and when enabled they
    /// are summed with the general waste and applied ONCE to the net base
    /// quantity (never a second waste pass). Pure (no Revit dep) → unit-tested.
    /// </summary>
    public static class MeasuredAddition
    {
        /// <summary>Rebar lap allowance % — only on rebar, only when the project
        /// knob is enabled (> 0). NRM2-typical 8–12% on lapped members.</summary>
        public static double RebarLapPercent(bool isRebar, double knobPercent)
            => isRebar && !double.IsNaN(knobPercent) && knobPercent > 0 ? knobPercent : 0.0;

        /// <summary>Concrete over-order buffer % — only on concrete, only when
        /// the project knob is enabled (> 0). Typical +5%.</summary>
        public static double ConcreteOverOrderPercent(bool isConcrete, double knobPercent)
            => isConcrete && !double.IsNaN(knobPercent) && knobPercent > 0 ? knobPercent : 0.0;

        /// <summary>
        /// Gross up the NET base quantity by the general waste % PLUS one
        /// distinct discipline addition % — summed and applied ONCE:
        ///   qty = base × (1 + (wastePct + additionPct)/100).
        /// The two percentages are different allowances (waste = cutting/offcut;
        /// addition = lap or over-order); the addition is never the waste
        /// re-applied, so there is no double-count. Non-measured units and
        /// negative/NaN inputs are treated as 0% (never reduce a quantity).
        /// </summary>
        public static double GrossUp(double baseQuantity, string unit, double wastePercent, double additionPercent)
        {
            if (!WasteFactor.AppliesTo(unit)) return baseQuantity;
            double w = (double.IsNaN(wastePercent) || wastePercent < 0) ? 0.0 : wastePercent;
            double a = (double.IsNaN(additionPercent) || additionPercent < 0) ? 0.0 : additionPercent;
            return baseQuantity * (1.0 + (w + a) / 100.0);
        }
    }
}
