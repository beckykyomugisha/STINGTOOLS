// ============================================================================
// SetTagTierDefaultsCommand.cs — tier gates, written where they are bound.
//
// WHY THIS EXISTS
//
// TAG_PARA_STATE_1..10_BOOL and TAG_WARN_VISIBLE_BOOL control which label rows a
// STING tag family renders. MR_PARAMETERS.csv declares them "Generic Models,
// Type", and they appear in neither CATEGORY_BINDINGS.csv nor
// RESOLVED_BINDINGS.csv: they are TYPE parameters of the TAG FAMILY, not
// instance parameters of the tagged model element.
//
// D11 set them with SetYesNo(el, …) inside WriteTag7All. That call site sees a
// model element, where the parameters do not exist, so every write landed
// nowhere and reported success — the seventh instance of the declared-vs-actual
// pattern. Those calls are removed.
//
// A loaded FamilySymbol, however, is not a .rfa. Its type parameters are live
// Parameters on an Element in the project document, and a transaction can set
// them. So the defaults can be applied to all 14 type variants of every loaded
// STING tag family in one pass, for this project and every future one, instead
// of an operator opening each type by hand.
//
// WHAT IT DOES NOT DO
//
// It does not touch the .rfa on disk. A family reloaded from a library ships
// whatever its author saved, so this is re-runnable by design rather than a
// one-time migration. It also never invents a parameter: a symbol that does not
// carry a gate is counted and reported, not "fixed".
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.TagStudio
{
    /// <summary>
    /// Sets the shipped tier-gate defaults on every loaded STING tag FamilySymbol:
    /// tiers 1-2 ON, tiers 3-10 OFF, warning row ON.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetTagTierDefaultsCommand : IExternalCommand
    {
        // Tiers 1 and 2 ON: tier 1 alone renders an identity code with no context;
        // tier 2 adds the material/system line a reviewer needs to recognise what
        // the tag is on. Tiers 3+ off so a stock tag stays readable.
        private const int TiersOn = 2;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                var symbols = CollectStingTagSymbols(doc);
                if (symbols.Count == 0)
                {
                    TaskDialog.Show("Tag Tier Defaults",
                        "No loaded STING tag family types found.\n\n"
                      + "This command sets the tier gates on tag family TYPES. Load the STING "
                      + "tag families into the project first.");
                    return Result.Cancelled;
                }

                string[] paraStates = ParamRegistry.AllParaStates;   // index 0 = tier 1
                var families = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                int typesTouched = 0, gatesWritten = 0, symbolsWithoutGates = 0;
                var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

                // Per-gate outcome tally. The first version reported only "types
                // carrying gates" and "values written", which cannot distinguish
                // "the gate is not on this type" from "the gate is there and already
                // holds the target value" — so a run that wrote 0 was unreadable.
                // Three counters per parameter name make the next run self-explaining.
                var tally = new SortedDictionary<string, int[]>(StringComparer.Ordinal); // [written, alreadyCorrect, absent]
                int[] Slot(string n)
                {
                    if (!tally.TryGetValue(n, out int[] c)) { c = new int[3]; tally[n] = c; }
                    return c;
                }

                using (var tx = new Transaction(doc, "STING Set Tag Tier Defaults"))
                {
                    tx.Start();
                    foreach (FamilySymbol sym in symbols)
                    {
                        bool anyGateOnThisSymbol = false;
                        int writtenHere = 0;

                        void Apply(string pname, bool want)
                        {
                            switch (SetBool(sym, pname, want))
                            {
                                case GateWrite.Written:        writtenHere++; anyGateOnThisSymbol = true; Slot(pname)[0]++; break;
                                case GateWrite.AlreadyCorrect:                anyGateOnThisSymbol = true; Slot(pname)[1]++; break;
                                case GateWrite.Absent:         missing.Add(pname);                        Slot(pname)[2]++; break;
                            }
                        }

                        for (int i = 0; i < paraStates.Length; i++)
                            Apply(paraStates[i], i < TiersOn);

                        Apply(ParamRegistry.WARN_VISIBLE, true);

                        if (!anyGateOnThisSymbol) { symbolsWithoutGates++; continue; }

                        typesTouched++;
                        gatesWritten += writtenHere;
                        families.Add(sym.Family?.Name ?? "(unnamed family)");
                    }
                    tx.Commit();
                }

                var report = new StringBuilder();
                report.AppendLine($"  Tag family types scanned: {symbols.Count:N0}");
                report.AppendLine($"  Types carrying gates:     {typesTouched:N0} across {families.Count:N0} families");
                report.AppendLine($"  Gate values written:      {gatesWritten:N0}");
                report.AppendLine($"  Defaults applied:         tiers 1-{TiersOn} ON, tiers {TiersOn + 1}-10 OFF, warning row ON");
                if (symbolsWithoutGates > 0)
                {
                    report.AppendLine();
                    report.AppendLine($"  {symbolsWithoutGates:N0} type(s) carry NO tier-gate parameters at all — not changed.");
                    report.AppendLine("  Those families predate the gated label design; run the family");
                    report.AppendLine("  conformance check (FamilyConformanceCheck) against them.");
                }
                report.AppendLine();
                report.AppendLine("  Per gate — written / already correct / not on the type:");
                report.AppendLine($"    {"parameter",-28} {"want",5} {"wrote",6} {"same",6} {"absent",7}");
                for (int i = 0; i < paraStates.Length; i++)
                {
                    int[] c = tally.TryGetValue(paraStates[i], out int[] v) ? v : new int[3];
                    report.AppendLine($"    {paraStates[i],-28} {(i < TiersOn ? "ON" : "OFF"),5} {c[0],6} {c[1],6} {c[2],7}");
                }
                {
                    int[] c = tally.TryGetValue(ParamRegistry.WARN_VISIBLE, out int[] v) ? v : new int[3];
                    report.AppendLine($"    {ParamRegistry.WARN_VISIBLE,-28} {"ON",5} {c[0],6} {c[1],6} {c[2],7}");
                }
                report.AppendLine();
                report.AppendLine("  'same' means the gate IS on the type and already holds the target");
                report.AppendLine("  value — nothing to do. 'absent' means the type does not carry it,");
                report.AppendLine("  which for tiers 1-3 and the warning row means they are instance-side");
                report.AppendLine("  and must be set per placed tag, not here.");
                if (missing.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("  Absent on at least one type (skipped, never invented):");
                    report.AppendLine("    " + string.Join(", ", missing));
                }
                report.AppendLine();
                report.AppendLine("  This writes the loaded types only. A family reloaded from the");
                report.AppendLine("  library brings its author's saved defaults — re-run then.");

                StingLog.Info($"SetTagTierDefaults: symbols={symbols.Count}, typesTouched={typesTouched}, "
                            + $"gatesWritten={gatesWritten}, noGates={symbolsWithoutGates}");

                var td = new TaskDialog("Tag Tier Defaults")
                {
                    MainInstruction = $"Set tier gates on {typesTouched:N0} tag family type(s)",
                    MainContent = report.ToString(),
                };
                td.Show();
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("SetTagTierDefaultsCommand crashed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private enum GateWrite { Written, AlreadyCorrect, Absent }

        /// <summary>
        /// Set a Yes/No type parameter on a symbol. Distinguishes "not present" from
        /// "already right" so the report can tell the operator which families are
        /// missing gates rather than counting them as done.
        /// </summary>
        private static GateWrite SetBool(FamilySymbol sym, string paramName, bool value)
        {
            if (sym == null || string.IsNullOrEmpty(paramName)) return GateWrite.Absent;
            try
            {
                Parameter p = sym.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return GateWrite.Absent;

                int want = value ? 1 : 0;
                if (p.StorageType == StorageType.Integer)
                {
                    if (p.AsInteger() == want) return GateWrite.AlreadyCorrect;
                    return p.Set(want) ? GateWrite.Written : GateWrite.Absent;
                }
                if (p.StorageType == StorageType.String)
                {
                    // Some vendor families declare the gate as Text rather than Yes/No.
                    string wantStr = value ? "1" : "0";
                    if (string.Equals(p.AsString(), wantStr, StringComparison.Ordinal))
                        return GateWrite.AlreadyCorrect;
                    return p.Set(wantStr) ? GateWrite.Written : GateWrite.Absent;
                }
                return GateWrite.Absent;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SetTagTierDefaults: '{paramName}' on '{sym.Name}': {ex.Message}");
                return GateWrite.Absent;
            }
        }

        /// <summary>
        /// Every loaded annotation-category FamilySymbol whose family name marks it as
        /// a STING tag. Matching on the shipped "STING - " prefix keeps third-party tag
        /// families out of a bulk write against them.
        /// </summary>
        private static List<FamilySymbol> CollectStingTagSymbols(Document doc)
        {
            var result = new List<FamilySymbol>();
            foreach (FamilySymbol sym in new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>())
            {
                try
                {
                    if (sym.Category?.CategoryType != CategoryType.Annotation) continue;
                    string fam = sym.Family?.Name;
                    if (string.IsNullOrEmpty(fam)) continue;
                    if (fam.IndexOf("STING", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    result.Add(sym);
                }
                catch (Exception ex) { StingLog.Warn($"SetTagTierDefaults: symbol scan: {ex.Message}"); }
            }
            return result;
        }
    }
}
