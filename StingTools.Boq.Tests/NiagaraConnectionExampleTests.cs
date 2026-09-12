using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Boq.Tests
{
    // docs/examples/KUT/niagara_connection.json.example is the ONLY written record of the
    // shape NiagaraConnection.Load expects. Before it existed, whoever enables the live
    // BMS read at Stage 3.1 had to read the source to learn the field names — and a
    // mistyped key there is a silent default (empty base URL, "/obix" points path), not
    // an error, so the station would simply never answer and nothing would say why.
    //
    // This gate holds the example and the loader together in BOTH directions:
    //   - every key the loader reads must appear in the example (else a required field is
    //     undocumented and gets left out),
    //   - every key the example declares must be one the loader reads (else the example
    //     teaches a field name that goes nowhere).
    // A one-directional check is an alternative-path escape: it lets the example go stale
    // in whichever direction it does not look.
    //
    // The loader's key list is EXTRACTED FROM THE REAL SOURCE, not restated here. A
    // restated list is a copy that drifts, and a gate that asserts its own copy proves
    // nothing. NiagaraJsonClient.cs cannot be <Compile Include>d into this project (it
    // logs through StingLog, which drags in StingTools.Core.Drawing), so it is copied to
    // the output as text and parsed.
    public class NiagaraConnectionExampleTests
    {
        private static readonly Regex JsonKeyRead = new Regex(@"o\[""([A-Za-z_][A-Za-z0-9_]*)""\]", RegexOptions.Compiled);

        private static string DataFile(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", name);
            Assert.True(File.Exists(path), $"Expected test data file not found: {path}");
            return File.ReadAllText(path);
        }

        /// <summary>The JSON keys NiagaraConnection.Load actually reads, scraped from the
        /// real source between 'class NiagaraConnection' and the next type.</summary>
        private static List<string> LoaderKeys()
        {
            string src = DataFile("NiagaraJsonClient.cs.txt");

            int start = src.IndexOf("class NiagaraConnection", StringComparison.Ordinal);
            Assert.True(start >= 0,
                "could not find 'class NiagaraConnection' in the copied source — the type has been " +
                "renamed or moved, and this gate is no longer reading the loader it claims to.");
            int end = src.IndexOf("class NiagaraJsonClient", StringComparison.Ordinal);
            Assert.True(end > start,
                "could not find the end of the NiagaraConnection class — refusing to scrape a region I cannot bound.");

            var keys = JsonKeyRead.Matches(src.Substring(start, end - start))
                                  .Cast<Match>()
                                  .Select(m => m.Groups[1].Value)
                                  .Distinct()
                                  .ToList();

            // Instrument check. A regex that matched nothing would make every assertion
            // below vacuously true — an empty list standing in for an answer.
            Assert.True(keys.Count >= 5,
                $"extracted only {keys.Count} JSON keys from NiagaraConnection.Load " +
                $"({string.Join(", ", keys)}). The extraction is broken, so any 'pass' here is meaningless.");
            return keys;
        }

        private static JObject Example() => JObject.Parse(DataFile("niagara_connection.json.example"));

        private static List<string> ExampleKeys() =>
            Example().Properties()
                     .Select(p => p.Name)
                     .Where(n => !n.StartsWith("_", StringComparison.Ordinal))   // _comment is prose
                     .ToList();

        [Fact]
        public void EveryKeyTheLoaderReads_IsInTheExample()
        {
            var loader = LoaderKeys();
            var example = ExampleKeys();

            var missing = loader.Where(k => !example.Contains(k, StringComparer.Ordinal)).ToList();
            Assert.True(missing.Count == 0,
                "NiagaraConnection.Load reads these keys, but docs/examples/KUT/niagara_connection.json.example " +
                $"does not declare them: {string.Join(", ", missing)}. Whoever copies the example would leave " +
                "the field out, and the loader would silently use its default.");
        }

        [Fact]
        public void EveryKeyTheExampleDeclares_IsReadByTheLoader()
        {
            var loader = LoaderKeys();
            var example = ExampleKeys();

            var stray = example.Where(k => !loader.Contains(k, StringComparer.Ordinal)).ToList();
            Assert.True(stray.Count == 0,
                "the example declares these keys, but NiagaraConnection.Load never reads them: " +
                $"{string.Join(", ", stray)}. A key that goes nowhere reads as configured and is not.");
        }

        [Fact]
        public void TheExampleAndTheLoaderAgreeExactly()
        {
            // Both directions in one assertion, so the counts are visible in the failure.
            var loader = LoaderKeys().OrderBy(k => k, StringComparer.Ordinal).ToList();
            var example = ExampleKeys().OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.True(loader.Count > 0 && example.Count > 0,
                $"loader keys: {loader.Count}, example keys: {example.Count} — neither may be empty.");
            Assert.Equal(string.Join(",", loader), string.Join(",", example));
        }

        [Fact]
        public void TheExampleCarriesNoRealCredential()
        {
            // A committed station URL or password would be a leak; a plausible-looking one
            // would also be a guess presented as supplied.
            foreach (var p in Example().Properties())
            {
                if (p.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                string v = (string)p.Value ?? "";
                Assert.True(v.Length == 0 || v.StartsWith("REPLACE_WITH_", StringComparison.Ordinal),
                    $"'{p.Name}' carries '{v}'. Every value in a committed example must be empty or a " +
                    "REPLACE_WITH_ placeholder — a real-looking value is either a leaked credential or a guess.");
            }
        }

        [Fact]
        public void TheExampleWarnsThatPointsPathIsStationSpecific()
        {
            // The /obix default is a lobby, not a points feed. Someone accepting it would
            // get an empty read that looks like an empty station.
            string comment = ((string)Example()["_comment"] ?? "");
            Assert.False(string.IsNullOrWhiteSpace(comment), "the example must carry a _comment explaining the file.");
            Assert.Contains("pointsPath", comment, StringComparison.Ordinal);
            Assert.Contains("/obix", comment, StringComparison.Ordinal);
            Assert.Contains("controls contractor", comment, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("gitignored", comment, StringComparison.OrdinalIgnoreCase);
        }
    }
}
