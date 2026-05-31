using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace StingTools.Boq.Tests
{
    // Z-22 Stage 2 — regression lock for FORMULAS_WITH_DEPENDENCIES.csv:
    //   (1) no formula lists its own Parameter_Name in Input_Parameters
    //       (the 19 spurious self-references Stage 1 mapped), and
    //   (2) the declared-dependency graph is acyclic — Kahn orders ALL nodes.
    // Mirrors the engine's edge definition (input -> output, formula-name
    // inputs only) on the Input_Parameters column.
    public class FormulaSelfRefTests
    {
        private static string CsvPath()
            => Path.Combine(AppContext.BaseDirectory, "Data", "FORMULAS_WITH_DEPENDENCIES.csv");

        private sealed class Row { public string Name; public List<string> Ins; }

        private static List<Row> Load()
        {
            var lines = File.ReadAllLines(CsvPath()).Where(l => !l.StartsWith("#")).ToList();
            var rdr = ParseCsv(lines);
            var hdr = rdr[0];
            int p = Array.IndexOf(hdr, "Parameter_Name");
            int inp = Array.IndexOf(hdr, "Input_Parameters");
            var rows = new List<Row>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in rdr.Skip(1))
            {
                if (f.Length <= p) continue;
                string nm = f[p].Trim();
                if (nm.Length == 0 || !seen.Add(nm)) continue;   // dedup by name, first-wins (engine GroupBy)
                var ins = (f.Length > inp ? f[inp] : "")
                    .Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                rows.Add(new Row { Name = nm, Ins = ins });
            }
            return rows;
        }

        [Fact]
        public void NoFormulaListsItselfAsInput()
        {
            var offenders = Load()
                .Where(r => r.Ins.Any(i => string.Equals(i, r.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(r => r.Name).ToList();
            Assert.True(offenders.Count == 0,
                "Input_Parameters self-references remain: " + string.Join(", ", offenders));
        }

        [Fact]
        public void DependencyGraphIsAcyclic_KahnOrdersEveryNode()
        {
            var rows = Load();
            var names = new HashSet<string>(rows.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
            var adj = names.ToDictionary(n => n, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
            var indeg = names.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
                foreach (var dep in r.Ins.Where(i => names.Contains(i)).Distinct(StringComparer.OrdinalIgnoreCase))
                    if (!string.Equals(dep, r.Name, StringComparison.OrdinalIgnoreCase)) // self-skip, like the engine
                    { adj[dep].Add(r.Name); indeg[r.Name]++; }

            var q = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            int sorted = 0;
            while (q.Count > 0)
            {
                var x = q.Dequeue(); sorted++;
                foreach (var y in adj[x]) if (--indeg[y] == 0) q.Enqueue(y);
            }
            Assert.Equal(names.Count, sorted);   // all 278 — no cycle
        }

        // Minimal RFC-4180 CSV parser (handles quoted fields + "" escapes).
        private static List<string[]> ParseCsv(IEnumerable<string> lines)
        {
            var outp = new List<string[]>();
            foreach (var line in lines)
            {
                var fields = new List<string>();
                var sb = new System.Text.StringBuilder();
                bool q = false;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (q)
                    {
                        if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                        else sb.Append(c);
                    }
                    else if (c == '"') q = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
                fields.Add(sb.ToString());
                outp.Add(fields.ToArray());
            }
            return outp;
        }
    }
}
