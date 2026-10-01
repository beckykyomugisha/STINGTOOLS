using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-61 — a production rule pinned to a slot must name a slot the type
    /// declares, and no two rules may share one. mep-coord-A1-1to50 produced a
    /// plan, an ISO and a section (slotIndex 0, 1, 2) but declared one slot, so
    /// the ISO and the section were stacked on top of the plan.
    /// </summary>
    public class ProductionRuleSlotTests
    {
        [Fact]
        public void Every_pinned_production_rule_has_its_own_declared_slot()
        {
            var problems = new List<string>();
            int pinned = 0;
            foreach (var t in DrawingCatalogueFixture.Shipped().DrawingTypes)
            {
                if (t.ProductionRules == null) continue;
                int slots = t.Slots?.Count ?? 0;
                var used = new HashSet<int>();
                foreach (var r in t.ProductionRules.Where(r => r.SlotIndex >= 0))
                {
                    pinned++;
                    if (r.SlotIndex >= slots)
                        problems.Add($"{t.Id}: rule {r.Idx} ({r.ViewType}) -> slotIndex {r.SlotIndex}, but only {slots} slot(s)");
                    else if (!used.Add(r.SlotIndex))
                        problems.Add($"{t.Id}: rule {r.Idx} ({r.ViewType}) shares slotIndex {r.SlotIndex} with another rule");
                }
            }
            Assert.True(pinned >= 5, $"Only {pinned} pinned rules bound — catalogue binding broken?");
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }
}
