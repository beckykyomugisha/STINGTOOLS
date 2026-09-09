// ══════════════════════════════════════════════════════════════════════════
//  TypeCreationTally.cs — what a type-creation run actually did, counted so the
//  outcomes cannot share a number.
//
//  The four Create*Type functions in CompoundTypeCreator caught a
//  SetCompoundStructure failure, logged a warning, and returned TRUE. The
//  comment said it plainly — "type was created, just no layers" — and the
//  function then told its caller the whole operation had succeeded. The type
//  existed, carried the BASE type's build-up, and was counted as created; the
//  dialog said "Created N types" and meant two different things by it.
//
//  This is the decision, extracted so it is provable without Revit:
//
//      a compound-structure failure means the type was NOT built
//
//  and the counting rule that follows from it: a type built with the declared
//  build-up and a type left with somebody else's must never land in one count.
//  CreateMEPType has always returned false on every failure path. The codebase
//  knew; four functions did not.
//
//  NOT established — and worth repeating wherever this is read: whether that
//  path is what produced the 87 identical 100 mm concrete floors on the
//  2026-09-09 model. Their shape is consistent with it. The logs on the live
//  plugin path start 2026-08-17 and hold no type-creation line at all, so the
//  question is open and the fix stands on its own.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Materials
{
    public enum TypeCreationOutcome
    {
        /// <summary>Created, and carrying the build-up the register row declares.</summary>
        CreatedAsDeclared,
        /// <summary>Refused: no layer could be built from the row.</summary>
        RefusedNoLayers,
        /// <summary>Refused: Revit rejected the compound structure.</summary>
        RefusedStructureFailed,
        /// <summary>Refused, and the half-made type could NOT be removed — so a type is in
        /// the model carrying a build-up nobody asked for. The worst outcome, and the one
        /// that must never be silent.</summary>
        RefusedButLeftBehind,
        /// <summary>A type of that name was already present; nothing was done.</summary>
        AlreadyPresent,
    }

    public sealed class TypeCreationRecord
    {
        public string Kind = "";
        public string TypeName = "";
        public TypeCreationOutcome Outcome;
        public string Why = "";

        public override string ToString()
            => $"{Kind} '{TypeName}' — {Outcome}" + (Why.Length > 0 ? ": " + Why : "");
    }

    public sealed class TypeCreationTally
    {
        private readonly List<TypeCreationRecord> _records = new List<TypeCreationRecord>();

        public IReadOnlyList<TypeCreationRecord> Records => _records;

        public void Add(string kind, string typeName, TypeCreationOutcome outcome, string why = "")
            => _records.Add(new TypeCreationRecord
            { Kind = kind ?? "", TypeName = typeName ?? "", Outcome = outcome, Why = why ?? "" });

        public int Count(TypeCreationOutcome o) => _records.Count(r => r.Outcome == o);

        /// <summary>Types that exist AND carry the build-up they were asked to carry.
        /// The only number that may be called "created".</summary>
        public int CreatedAsDeclared => Count(TypeCreationOutcome.CreatedAsDeclared);

        /// <summary>Everything that was asked for and not built.</summary>
        public int Refused => Count(TypeCreationOutcome.RefusedNoLayers)
                            + Count(TypeCreationOutcome.RefusedStructureFailed)
                            + Count(TypeCreationOutcome.RefusedButLeftBehind);

        /// <summary>Types left in the model without the build-up they were asked for. Not
        /// folded into <see cref="Refused"/> in the report, because a refusal that cleaned
        /// up and one that could not are different things to be told.</summary>
        public int LeftBehind => Count(TypeCreationOutcome.RefusedButLeftBehind);

        /// <summary>
        /// A line a reader can act on. A run that built everything and a run that refused
        /// half of it MUST NOT produce the same text — that identity is the whole defect.
        /// </summary>
        public string Report()
        {
            if (_records.Count == 0) return "No rows to create from.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{CreatedAsDeclared} created with the build-up the register declares.");
            sb.AppendLine($"{Refused} refused because their layers could not be built.");
            sb.AppendLine($"{Count(TypeCreationOutcome.AlreadyPresent)} already present.");

            if (LeftBehind > 0)
                sb.AppendLine($"WARNING: {LeftBehind} refused type(s) could NOT be removed and are "
                            + "in the model carrying a build-up nobody asked for. Their quantities "
                            + "will measure and price as if they were real.");

            foreach (var r in _records.Where(x => x.Outcome != TypeCreationOutcome.CreatedAsDeclared
                                               && x.Outcome != TypeCreationOutcome.AlreadyPresent)
                                      .Take(10))
                sb.AppendLine("  " + r);

            int more = Refused - Math.Min(10, Refused);
            if (more > 0) sb.AppendLine($"  … and {more} more, in the log");
            return sb.ToString();
        }
    }
}
