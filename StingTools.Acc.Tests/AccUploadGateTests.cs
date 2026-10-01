// AccUploadGate - the ONE pre-upload discipline ACC_UploadModel / ACC_UploadLastBundle and
// the Export Centre auto-upload share (WORKLOG A12). What these guard:
//   * a revision that contradicts its suitability (recorded, or carried in the file name) is
//     refused before the ledger is even consulted;
//   * an identical file already sent is skipped (not a failure), with or without a revision;
//   * a changed file under a revision already sent is refused unless allowed;
//   * an unreadable file / missing ledger is refused, never sent blind;
//   * Record after a confirmed upload makes the next identical send a skip.
// Each "refused"/"skipped" case is paired with an "uploads" case so a gate that refused
// everything could not pass.

using System;
using System.IO;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccUploadGateTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-gate-" + Guid.NewGuid().ToString("N"));
        public AccUploadGateTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }

        private string File(string name, string content)
        {
            string p = Path.Combine(_dir, name);
            System.IO.File.WriteAllText(p, content);
            return p;
        }

        [Fact]
        public void FirstSend_WithAConsistentPair_Uploads()
        {
            var g = AccUploadGate.Check(new AccUploadLedger(), File("KUT-PCE-ZZ-01-DR-A-0101.pdf", "v1"), "KUT-PCE-ZZ-01-DR-A-0101", "P02", "S2", false);
            Assert.Equal(AccUploadGateDecision.Upload, g.Decision);
            Assert.False(string.IsNullOrEmpty(g.Sha256));
            Assert.Equal("PDF", g.Format);
        }

        [Theory]
        [InlineData("P03", "A1")]   // preliminary revision filed as authorised
        [InlineData("C01", "S2")]   // contractual revision shared for coordination
        public void RecordedRevision_ContradictingSuitability_IsRefused(string rev, string suit)
        {
            var g = AccUploadGate.Check(new AccUploadLedger(), File("D-0101.pdf", "x"), "D-0101", rev, suit, allowReissue: true);
            Assert.Equal(AccUploadGateDecision.Refuse, g.Decision);
            Assert.Contains(rev, g.Reason);
        }

        [Fact]
        public void RevisionInTheName_ContradictingSuitability_IsRefused_EvenWithNoRecordedRevision()
        {
            var ok = AccUploadGate.Check(new AccUploadLedger(), File("KUT-PCE-ZZ-01-DR-A-0101-P03.pdf", "x"), "", "", "S2", false);
            Assert.Equal(AccUploadGateDecision.Upload, ok.Decision);
            var bad = AccUploadGate.Check(new AccUploadLedger(), File("KUT-PCE-ZZ-01-DR-A-0102-P03.pdf", "x"), "", "", "A1", false);
            Assert.Equal(AccUploadGateDecision.Refuse, bad.Decision);
        }

        [Fact]
        public void AfterRecord_TheIdenticalFileIsSkipped_AndAChangedOneUnderTheSameRevisionIsRefused()
        {
            var ledger = new AccUploadLedger();
            string path = File("D-0101.pdf", "v1");
            var first = AccUploadGate.Check(ledger, path, "D-0101", "P02", "S2", false);
            Assert.True(first.ShouldUpload);
            AccUploadGate.Record(ledger, first, path, "D-0101", "P02", "S2", "urn:item", "urn:v?version=1", new DateTime(2026, 10, 1));

            var again = AccUploadGate.Check(ledger, path, "D-0101", "P02", "S2", false);
            Assert.Equal(AccUploadGateDecision.SkipIdentical, again.Decision);

            System.IO.File.WriteAllText(path, "v2 - changed content");
            var changed = AccUploadGate.Check(ledger, path, "D-0101", "P02", "S2", false);
            Assert.Equal(AccUploadGateDecision.Refuse, changed.Decision);
            Assert.Contains("without a revision change", changed.Reason);

            var allowed = AccUploadGate.Check(ledger, path, "D-0101", "P02", "S2", true);
            Assert.Equal(AccUploadGateDecision.Upload, allowed.Decision);
            Assert.True(allowed.ReissueAllowed);

            var revised = AccUploadGate.Check(ledger, path, "D-0101", "P03", "S2", false);
            Assert.Equal(AccUploadGateDecision.Upload, revised.Decision);
        }

        // C5: a suitability change under an unchanged revision is a status change, not a re-issue.
        [Fact]
        public void ASuitabilityChange_UnderTheSameRevision_Uploads_AsAStatusChange()
        {
            var ledger = new AccUploadLedger();
            string path = File("D-0101.pdf", "v1");
            var first = AccUploadGate.Check(ledger, path, "D-0101", "P02", "S2", false);
            AccUploadGate.Record(ledger, first, path, "D-0101", "P02", "S2", "i", "v1", DateTime.UtcNow);

            // Identical bytes, new code: before C5 this was skipped and ACC stayed at S2.
            var promoted = AccUploadGate.Check(ledger, path, "D-0101", "P02", "S3", false);
            Assert.Equal(AccUploadGateDecision.Upload, promoted.Decision);
            Assert.True(promoted.StatusChange);
            Assert.Contains("S2", promoted.Reason);

            // Different bytes (the title block prints the code), new code: still a status change, not refused.
            System.IO.File.WriteAllText(path, "v1 with S4 printed");
            var reprinted = AccUploadGate.Check(ledger, path, "D-0101", "P02", "S4", false);
            Assert.True(reprinted.StatusChange);
            Assert.False(reprinted.HeldAsReissue);

            // Back at the code already sent with the same bytes: identical skip, as before.
            System.IO.File.WriteAllText(path, "v1");
            Assert.Equal(AccUploadGateDecision.SkipIdentical,
                AccUploadGate.Check(ledger, path, "D-0101", "P02", "S2", false).Decision);
        }

        [Fact]
        public void ALegacyEntry_WithNoRecordedSuitability_KeepsTheOldRule()
        {
            var ledger = new AccUploadLedger();
            string path = File("D-0102.pdf", "v1");
            ledger.Record(new AccLedgerEntry
            {
                DocumentNumber = "D-0102", Revision = "P01", Format = "PDF", Suitability = "",
                Sha256 = AccUploadLedger.Sha256OfFile(path), UploadedUtc = DateTime.UtcNow, VersionUrn = "v",
            });
            Assert.Equal(AccUploadGateDecision.SkipIdentical,
                AccUploadGate.Check(ledger, path, "D-0102", "P01", "S3", false).Decision);
        }

        [Fact]
        public void NoRevision_IdenticalResendSkipped_ChangedContentStillUploads()
        {
            var ledger = new AccUploadLedger();
            string bundle = File("ACC_PUBLISH_20261001.zip", "zip-1");
            var first = AccUploadGate.Check(ledger, bundle, "", "", "", false);
            Assert.Equal(AccUploadGateDecision.Upload, first.Decision);
            AccUploadGate.Record(ledger, first, bundle, "", "", "", "i", "v", DateTime.UtcNow);

            Assert.Equal(AccUploadGateDecision.SkipIdentical, AccUploadGate.Check(ledger, bundle, "", "", "", false).Decision);
            System.IO.File.WriteAllText(bundle, "zip-2");
            Assert.Equal(AccUploadGateDecision.Upload, AccUploadGate.Check(ledger, bundle, "", "", "", false).Decision);
        }

        [Fact]
        public void NoLedger_OrUnreadableFile_IsRefused()
        {
            Assert.Equal(AccUploadGateDecision.Refuse,
                AccUploadGate.Check(null, File("a.pdf", "x"), "a", "P01", "S2", false).Decision);
            Assert.Equal(AccUploadGateDecision.Refuse,
                AccUploadGate.Check(new AccUploadLedger(), Path.Combine(_dir, "missing.pdf"), "a", "P01", "S2", false).Decision);
        }

        // R11: Supersede / Replace find the deliverable's ACC documents in the ledger.
        [Fact]
        public void LiveRenditions_AreTheNewestPerFormat_WithTheirFolder_UntilRetired()
        {
            var ledger = new AccUploadLedger();
            string doc = "KUT-PCE-ZZ-01-DR-A-0101";
            var t0 = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
            var pdf1 = AccUploadGate.Check(ledger, File("a1.pdf", "p1"), doc, "P01", "S2", false);
            AccUploadGate.Record(ledger, pdf1, File("a1.pdf", "p1"), doc, "P01", "S2", "item:1", "ver:1", t0, "fold:A");
            var pdf2 = AccUploadGate.Check(ledger, File("a2.pdf", "p2"), doc, "P02", "S2", false);
            AccUploadGate.Record(ledger, pdf2, File("a2.pdf", "p2"), doc, "P02", "S2", "item:1", "ver:2", t0.AddDays(14), "fold:A");
            var dwg = AccUploadGate.Check(ledger, File("a2.dwg", "d2"), doc, "P02", "S2", false);
            AccUploadGate.Record(ledger, dwg, File("a2.dwg", "d2"), doc, "P02", "S2", "item:2", "ver:3", t0.AddDays(14), "fold:A");
            // Another document and an upload with no version are never offered.
            ledger.Record(new AccLedgerEntry { DocumentNumber = "OTHER", Format = "PDF", VersionUrn = "ver:x", UploadedUtc = t0 });
            ledger.Record(new AccLedgerEntry { DocumentNumber = doc, Format = "IFC", VersionUrn = "", UploadedUtc = t0 });

            var live = ledger.LiveRenditions(doc.ToLowerInvariant());
            Assert.Equal(new[] { "DWG", "PDF" }, live.ConvertAll(e => e.Format));
            Assert.Equal("ver:2", live.Find(e => e.Format == "PDF").VersionUrn);
            Assert.All(live, e => Assert.Equal("fold:A", e.FolderUrn));

            // Retired renditions are not retired twice.
            foreach (var e in live) e.RetiredUtc = t0.AddDays(20);
            Assert.Empty(ledger.LiveRenditions(doc));
            Assert.Empty(ledger.LiveRenditions(""));
        }

        [Fact]
        public void TheFolderUrn_SurvivesASaveAndLoad()
        {
            var ledger = new AccUploadLedger();
            ledger.Record(new AccLedgerEntry { DocumentNumber = "D", Format = "pdf", VersionUrn = "v", FolderUrn = "f", UploadedUtc = DateTime.UtcNow });
            string path = Path.Combine(_dir, AccUploadLedger.FileName);
            Assert.True(ledger.TrySave(path, out _));
            var back = AccUploadLedger.Load(path, out string err);
            Assert.Null(err);
            Assert.Equal("f", Assert.Single(back.LiveRenditions("D")).FolderUrn);
        }
    }
}
