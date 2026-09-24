// PhaseBalancer — Revit-free planner for applied phase balancing (ROADMAP PNL-5).
//
// Input: a three-phase board's per-phase load, its single-pole circuits (load,
// phase, slot, locked) and its FREE slots with their phase. Output: a list of
// moves "circuit from slot s to free slot t" that reduces the spread between
// the heaviest and lightest phase. Only single-pole, unlocked circuits move,
// and only into slots that are empty — Revit's MoveSlotTo moves a breaker into
// a slot; it is never asked to swap two occupied ones.
//
// Greedy: at each step take the single move that cuts (max − min) the most,
// stop when no move improves it by at least MinGainVa. Deterministic for a
// given input (ties broken by circuit id then slot), so a preview and an apply
// on the same model give the same plan.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Electrical
{
    public sealed class BalanceCircuit
    {
        public long Id;
        public string Label = "";
        public int Phase;       // 0=L1, 1=L2, 2=L3
        public double LoadVa;
        public int Slot;
        public bool Locked;
    }

    public sealed class BalanceFreeSlot
    {
        public int Slot;
        public int Phase;
        public bool Locked;
    }

    public sealed class BalanceMove
    {
        public long CircuitId;
        public string Label = "";
        public int FromSlot, ToSlot, FromPhase, ToPhase;
        public double LoadVa;
    }

    public sealed class BalancePlan
    {
        public double[] Before = new double[3];
        public double[] After = new double[3];
        public List<BalanceMove> Moves { get; } = new List<BalanceMove>();
        public double BeforeImbalancePct => ImbalancePct(Before);
        public double AfterImbalancePct => ImbalancePct(After);

        /// <summary>Largest deviation from the mean phase load, as % of the mean (NEMA-style).</summary>
        public static double ImbalancePct(double[] p)
        {
            double avg = p.Average();
            if (avg <= 0) return 0;
            return p.Max(x => Math.Abs(x - avg)) / avg * 100.0;
        }
    }

    public static class PhaseBalancer
    {
        public const double MinGainVa = 50;   // below this a move is churn, not balance

        public static BalancePlan Plan(double[] phaseLoadsVa, IEnumerable<BalanceCircuit> circuits,
            IEnumerable<BalanceFreeSlot> freeSlots, int maxMoves = 12)
        {
            if (phaseLoadsVa == null || phaseLoadsVa.Length != 3)
                throw new ArgumentException("three phase loads required", nameof(phaseLoadsVa));

            var plan = new BalancePlan();
            Array.Copy(phaseLoadsVa, plan.Before, 3);
            var load = (double[])phaseLoadsVa.Clone();

            var movable = circuits.Where(c => !c.Locked && c.LoadVa > 0 && c.Phase >= 0 && c.Phase <= 2)
                                  .OrderBy(c => c.Id).ToList();
            var free = freeSlots.Where(f => !f.Locked && f.Phase >= 0 && f.Phase <= 2)
                                .OrderBy(f => f.Slot).ToList();
            var moved = new HashSet<long>();

            for (int step = 0; step < maxMoves; step++)
            {
                double spread = load.Max() - load.Min();
                BalanceCircuit bestC = null; BalanceFreeSlot bestF = null; double bestSpread = spread;

                foreach (var c in movable)
                {
                    if (moved.Contains(c.Id)) continue;   // each circuit moves at most once
                    foreach (var f in free)
                    {
                        if (f.Phase == c.Phase) continue;
                        var trial = (double[])load.Clone();
                        trial[c.Phase] -= c.LoadVa;
                        trial[f.Phase] += c.LoadVa;
                        double s = trial.Max() - trial.Min();
                        if (s < bestSpread - 1e-9) { bestSpread = s; bestC = c; bestF = f; }
                    }
                }
                if (bestC == null || spread - bestSpread < MinGainVa) break;

                plan.Moves.Add(new BalanceMove
                {
                    CircuitId = bestC.Id, Label = bestC.Label, LoadVa = bestC.LoadVa,
                    FromSlot = bestC.Slot, ToSlot = bestF.Slot,
                    FromPhase = bestC.Phase, ToPhase = bestF.Phase,
                });
                load[bestC.Phase] -= bestC.LoadVa;
                load[bestF.Phase] += bestC.LoadVa;
                // The vacated slot becomes free on the old phase; the target is taken.
                free.Remove(bestF);
                free.Add(new BalanceFreeSlot { Slot = bestC.Slot, Phase = bestC.Phase });
                free = free.OrderBy(x => x.Slot).ToList();
                moved.Add(bestC.Id);
                bestC.Phase = bestF.Phase;
                bestC.Slot = bestF.Slot;
            }
            Array.Copy(load, plan.After, 3);
            return plan;
        }

        /// <summary>
        /// Phase of each body row, calibrated from circuits whose phase is known:
        /// phase = (row + offset) mod 3. Returns the offset, or null when the known
        /// circuits disagree (the layout is not row-cyclic) or none are known.
        /// </summary>
        public static int? CalibrateRowOffset(IEnumerable<(int row, int phase)> known)
        {
            int? offset = null;
            foreach (var (row, phase) in known)
            {
                int o = ((phase - row) % 3 + 3) % 3;
                if (offset == null) offset = o;
                else if (offset != o) return null;
            }
            return offset;
        }

        public static int PhaseOfRow(int row, int offset) => ((row + offset) % 3 + 3) % 3;
    }
}
