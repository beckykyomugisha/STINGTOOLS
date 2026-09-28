// ══════════════════════════════════════════════════════════════════════════
//  SharedParamFileShapeTests.cs — every PARAM row of MR_PARAMETERS.txt must be
//  shaped the way Revit's shared-parameter reader expects.
//
//  Why: on 2026-09-24 an edit to ELC_CKT_NR shifted its columns so the
//  description landed in VISIBLE. Revit parses VISIBLE as an integer, so a row
//  like that can make OpenSharedParameterFile reject the whole file and Load
//  Params bind nothing. Every other gate passed it: the name existed, the GUID
//  was unique, the CSV mirror regenerated. Only the shape was wrong.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SharedParamFileShapeTests
    {
        private static readonly HashSet<string> DataTypes = new HashSet<string>(StringComparer.Ordinal)
        { "TEXT", "INTEGER", "NUMBER", "LENGTH", "AREA", "VOLUME", "ANGLE", "YESNO", "CURRENCY", "URL", "MATERIAL", "MULTILINETEXT" };

        private static string ParamFile()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data", "MR_PARAMETERS.txt");
        }

        private static List<(int line, string[] f)> ParamRows()
            => File.ReadAllLines(ParamFile())
                   .Select((l, i) => (line: i + 1, f: l.Split('\t')))
                   .Where(x => x.f[0] == "PARAM")
                   .ToList();

        [Fact]
        public void File_has_param_rows()
            => Assert.True(ParamRows().Count > 3000, "MR_PARAMETERS.txt has suspiciously few PARAM rows");

        [Fact]
        public void Every_param_row_has_the_columns_Revit_reads()
        {
            // PARAM GUID NAME DATATYPE DATACATEGORY GROUP VISIBLE DESCRIPTION USERMODIFIABLE [HIDEWHENNOVALUE]
            var bad = new List<string>();
            foreach (var (line, f) in ParamRows())
            {
                string name = f.Length > 2 ? f[2] : "?";
                if (f.Length < 9 || f.Length > 10) { bad.Add($"line {line} {name}: {f.Length} fields"); continue; }
                if (!Guid.TryParse(f[1], out _)) bad.Add($"line {line} {name}: GUID '{f[1]}'");
                if (!DataTypes.Contains(f[3])) bad.Add($"line {line} {name}: DATATYPE '{f[3]}'");
                if (!int.TryParse(f[5], out _)) bad.Add($"line {line} {name}: GROUP '{f[5]}'");
                if (f[6] != "0" && f[6] != "1") bad.Add($"line {line} {name}: VISIBLE '{Trunc(f[6])}' (must be 0/1)");
                if (f[8] != "0" && f[8] != "1") bad.Add($"line {line} {name}: USERMODIFIABLE '{Trunc(f[8])}' (must be 0/1)");
                if (f.Length == 10 && f[9] != "0" && f[9] != "1") bad.Add($"line {line} {name}: HIDEWHENNOVALUE '{Trunc(f[9])}'");
            }
            Assert.True(bad.Count == 0, "Malformed PARAM rows:\n" + string.Join("\n", bad.Take(30)));
        }

        [Fact]
        public void Circuit_number_is_text_and_well_formed()
        {
            var row = ParamRows().Single(r => r.f.Length > 2 && r.f[2] == "ELC_CKT_NR").f;
            Assert.Equal("TEXT", row[3]);      // holds multi-pole numbers like "1,3,5"
            Assert.Equal("1", row[6]);
        }

        private static string Trunc(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;
    }
}
