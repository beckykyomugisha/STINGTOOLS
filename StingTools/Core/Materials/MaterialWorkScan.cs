// ══════════════════════════════════════════════════════════════════════════
//  MaterialWorkScan.cs — is there anything for Materials_SetClass or
//  Materials_StampCodes to DO in this model?
//
//  WHY IT IS A SEPARATE QUESTION FROM "is anything blank". The obvious
//  condition — any material whose Class is empty — is wrong, and wrong in the
//  direction that never settles. `Materials_SetClass` deliberately refuses to
//  guess a class for a name that says nothing, and this model carries hundreds
//  of such names, so "a blank exists" would be TRUE on every run forever and the
//  step would never skip. Same for MAT_CODE: 536 of the Herring model's 1,815
//  materials are not in the governed register at all, and no amount of running
//  the command will change that.
//
//  So the condition asks exactly what the COMMAND would do, by calling the same
//  planners the commands call — MaterialClassPlanner and
//  MaterialCodeStampPlanner. A skip then means "nothing to do", not "nothing
//  detectable", and a re-run of ProjectKickoff records SKIPPED with a reason
//  instead of scanning 1,815 materials to conclude nothing.
//
//  Revit-free on purpose: the Revit half reads Name / MaterialClass / MAT_CODE
//  off each Material and hands them here, so the DECISION is provable without a
//  Document. Both planners it delegates to are already Revit-free and tested.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core.Materials
{
    /// <summary>One material, flattened to the three fields the decision needs.</summary>
    public sealed class MaterialWorkState
    {
        public string Name = "";
        /// <summary>Revit's own Material Class. Empty when unset.</summary>
        public string MaterialClass = "";
        /// <summary>The MAT_CODE shared parameter. Empty when unset or unbound.</summary>
        public string MatCode = "";
    }

    /// <summary>What the two commands would actually change.</summary>
    public sealed class MaterialWorkTally
    {
        public int Total;
        /// <summary>Materials Materials_SetClass would write a class onto.</summary>
        public int NeedClass;
        /// <summary>Materials Materials_StampCodes would write a code onto.</summary>
        public int NeedCode;
        /// <summary>Materials a planner threw on. Counted rather than swallowed: a
        /// condition that answered "nothing to do" because the decision crashed is the
        /// silent no-op this codebase produces, and the caller logs this number.</summary>
        public int Unreadable;

        public bool HasUnclassed => NeedClass > 0;
        public bool HasUncoded => NeedCode > 0;

        /// <summary>The sentence a SKIPPED step records. Says the number, because
        /// "nothing to do" and "nothing detectable" read identically otherwise.</summary>
        public string ClassReason => NeedClass > 0
            ? NeedClass + " of " + Total + " material(s) can be classified"
            : "no material can be classified from its name (" + Total + " scanned)";

        public string CodeReason => NeedCode > 0
            ? NeedCode + " of " + Total + " material(s) have a register code and no MAT_CODE"
            : "every material the register names already carries its code (" + Total + " scanned)";
    }

    public static class MaterialWorkScan
    {
        /// <summary>
        /// One pass, both answers — the same shape `has_untagged` / `has_placeholders`
        /// already share, because scanning a model's materials twice to ask two questions
        /// about the same row is the cost this condition exists to avoid.
        /// </summary>
        /// <param name="registry">The governed register. Pass
        /// <see cref="MaterialRegistry.Empty"/> when it is not deployed: every material
        /// then resolves no code, which is the same answer a missing name gives and
        /// leaves NeedCode at 0 rather than inventing work.</param>
        public static MaterialWorkTally Scan(
            IEnumerable<MaterialWorkState> materials, MaterialRegistry registry)
        {
            var t = new MaterialWorkTally();
            if (materials == null) return t;

            foreach (var m in materials)
            {
                if (m == null || string.IsNullOrWhiteSpace(m.Name)) continue;
                t.Total++;

                // Exactly what Materials_SetClass would do: never overwrites a class
                // somebody chose, never invents one for a name that says nothing.
                try
                {
                    if (MaterialClassPlanner.Plan(m.Name, m.MaterialClass ?? "").WillWrite)
                        t.NeedClass++;
                }
                catch (Exception) { t.Unreadable++; }

                // Exactly what Materials_StampCodes would do: exact MAT_NAME match,
                // never overwrites an existing code.
                try
                {
                    if (MaterialCodeStampPlanner.Plan(m.Name, m.MatCode ?? "", registry).WillWrite)
                        t.NeedCode++;
                }
                catch (Exception) { t.Unreadable++; }
            }
            return t;
        }
    }
}
