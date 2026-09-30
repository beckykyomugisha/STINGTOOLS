// The Day-1 playbook is what an Information Manager copies acc_settings.json from. Two ways it
// has drifted before (WORKLOG A16): an example that the strict parser rejects - so the copied
// file loads as Malformed and every setting silently reverts to prompting - and a key the
// parser reads that §6 never mentions, so nobody knows it exists. These tests hold the doc to
// the code in both directions:
//   * every ```json block in the playbook, loaded through AccOperatingPolicy.Load, is Loaded;
//   * every AccOperatingPolicy.KnownKeys entry has a row in §6, and every key §6 lists is known.
// The playbook must be found: a missing file fails, it does not skip.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccPlaybookSettingsDocTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-playbook-" + Guid.NewGuid().ToString("N"));
        public AccPlaybookSettingsDocTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }

        private static string Playbook()
        {
            string path = AccAttributeNamesTests.FindRepoFile("docs", "KUT_ACC_DAY1_PLAYBOOK.md");
            Assert.True(path != null, "docs/KUT_ACC_DAY1_PLAYBOOK.md was not found above the test output directory");
            return File.ReadAllText(path);
        }

        private static string Section6(string text)
        {
            int start = text.IndexOf("## 6.", StringComparison.Ordinal);
            int end = text.IndexOf("## 7.", start + 1, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, "playbook §6 / §7 headings not found");
            return text.Substring(start, end - start);
        }

        [Fact]
        public void EveryJsonExampleInThePlaybook_LoadsAsSettings_NotMalformed()
        {
            var blocks = Regex.Matches(Playbook(), "```json\\s*\\n(.*?)```", RegexOptions.Singleline)
                              .Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToList();
            Assert.True(blocks.Count >= 3, $"expected the playbook's acc_settings examples, found {blocks.Count} json block(s)");

            int i = 0;
            foreach (string block in blocks)
            {
                // A fragment ("key": value, …) is shown as part of the file: load it as one.
                string json = block.StartsWith("{", StringComparison.Ordinal) ? block : "{" + block + "}";
                string path = Path.Combine(_dir, $"example{i++}.json");
                File.WriteAllText(path, json);
                var policy = AccOperatingPolicy.Load(path);
                Assert.True(policy.Source == AccPolicySource.Loaded,
                    $"playbook json example #{i} loads as {policy.Source}: {policy.LoadError}\n{block}");
                // escalateExcludeStatuses REPLACES the default list: an example that lists fewer
                // would quietly start escalating resolved / approved clashes for whoever copies it.
                var dropped = AccOperatingPolicy.DefaultExcludedClashStatuses
                    .Where(d => !policy.EscalateExcludeStatuses.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
                Assert.True(dropped.Count == 0,
                    $"playbook json example #{i} drops default excluded clash statuses: {string.Join(", ", dropped)}");
            }
        }

        [Fact]
        public void EveryKnownKey_HasARowInSection6()
        {
            string s6 = Section6(Playbook());
            var missing = AccOperatingPolicy.KnownKeys
                .Where(k => !Regex.IsMatch(s6, "^\\|\\s*`" + Regex.Escape(k) + "`", RegexOptions.Multiline))
                .OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.True(missing.Count == 0, "KnownKeys with no row in playbook §6: " + string.Join(", ", missing));
        }

        [Fact]
        public void EveryKeySection6Lists_IsAKnownKey()
        {
            var listed = Regex.Matches(Section6(Playbook()), "^\\|\\s*`([A-Za-z]+)`", RegexOptions.Multiline)
                              .Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.NotEmpty(listed);
            var unknown = listed.Where(k => !AccOperatingPolicy.KnownKeys.Contains(k)).ToList();
            Assert.True(unknown.Count == 0, "playbook §6 lists keys the parser does not read: " + string.Join(", ", unknown));
        }
    }
}
