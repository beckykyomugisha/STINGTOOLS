// Export -> register -> ACC: the suitability / revision a file carries, and what may be
// uploaded. Each test pins a defect the 2026-09-30 review found:
//   * the Export Centre named files with an invented "S2" / "P01" when a sheet carried none,
//     and recorded the same file as S0 in the register;
//   * the ACC upload sent Revision = "" for every file;
//   * nothing stopped an automatic upload from stacking a changed file as a new ACC version
//     under an unchanged revision.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.Core.Drawing;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class ExportIsoFieldsTests
    {
        [Fact]
        public void TheSheetsOwnValues_AreUsed()
        {
            var v = ExportIsoFields.Resolve("S4 - FOR APPROVAL", " P03 ");
            Assert.True(v.Complete);
            Assert.Equal("S4", v.Suitability);
            Assert.Equal("P03", v.Revision);
            Assert.Equal("SHARED", v.CdeState);
            Assert.Empty(v.Unset);
        }

        [Fact]
        public void MissingValues_AreTheNotSetMarkers_NeverAPlausibleCode()
        {
            var v = ExportIsoFields.Resolve(null, "");
            Assert.False(v.Complete);
            Assert.Equal("XX", v.Suitability);
            Assert.Equal("NOREV", v.Revision);
            Assert.Null(v.CdeState);                                     // nothing can route by it
            Assert.Equal(new[] { "suitability", "revision" }, v.Unset);
            Assert.NotEqual("S2", v.Suitability);
            Assert.NotEqual("P01", v.Revision);
        }

        [Theory]
        [InlineData("Z9")]          // not an ISO 19650 code
        [InlineData("XX")]          // the marker read back from an earlier export
        [InlineData("   ")]
        public void AnUnrecognisedSuitability_IsNotSet(string raw)
        {
            var v = ExportIsoFields.Resolve(raw, "P01");
            Assert.False(v.SuitabilitySet);
            Assert.Equal(new[] { "suitability" }, v.Unset);
        }

        [Fact]
        public void TheRevisionMarker_ReadBack_IsStillNotSet()
            => Assert.False(ExportIsoFields.Resolve("S3", "NOREV").RevisionSet);

        [Theory]
        [InlineData("KUT-PLN-ZZ-01-DR-A-0101-XX-P02.pdf", true)]     // suitability position
        [InlineData("KUT-PLN-ZZ-01-DR-A-0101-S3-NOREV.pdf", true)]   // revision marker anywhere
        [InlineData(@"C:\out\A-101_NOREV.dwg", true)]
        [InlineData("KUT-PLN-ZZ-XX-DR-A-0101-S3-P02.pdf", false)]    // XX = ISO unknown LEVEL: fine
        [InlineData("KUT-PLN-ZZ-01-DR-A-0101-S3-P02.pdf", false)]
        [InlineData("model.rvt", false)]
        [InlineData("", false)]
        public void NameMarkerDetection(string name, bool expected)
            => Assert.Equal(expected, ExportIsoFields.NameCarriesNotSetMarker(name));
    }

    public class AccFileIsoTests
    {
        private const string Pdf = @"C:\proj\01_SHARED\A\KUT-PLN-ZZ-01-DR-A-0101-S3-P02.pdf";

        private static JObject Row(string path, string suit = "S3", string rev = "P02", string docNumber = "KUT-PLN-ZZ-01-DR-A-0101",
                                   params string[] unset)
        {
            var r = new JObject
            {
                ["doc_id"] = "DOC-0001", ["file_name"] = Path.GetFileName(path), ["file_path"] = path,
                ["suitability"] = suit, ["revision"] = rev, ["cde_status"] = "SHARED",
            };
            if (docNumber != null) r["doc_number"] = docNumber;
            if (unset.Length > 0) r["iso_unset"] = new JArray(unset);
            return r;
        }

        [Fact]
        public void ADefaultedRegisterSuitability_IsNotSent()
        {
            // R14: the register's S0 for a row nobody gave a code is a convention, not a choice.
            var row = Row(Pdf, "S0");
            row["suitability_defaulted"] = true;
            var f = AccFileIso.Decide(Pdf, null, row);
            Assert.False(f.Refused, f.Refusal);
            Assert.Equal("", f.Suitability);
            Assert.Contains(f.Notes, n => n.Contains("S0 default"));
            // The same S0 recorded for real is sent.
            Assert.Equal("S0", AccFileIso.Decide(Pdf, null, Row(Pdf, "S0")).Suitability);
        }

        [Fact]
        public void TheRegisterRow_SuppliesDocumentNumber_Suitability_AndRevision()
        {
            var f = AccFileIso.Decide(Pdf, null, Row(Pdf));
            Assert.False(f.Refused, f.Refusal);
            Assert.Equal("KUT-PLN-ZZ-01-DR-A-0101", f.DocumentNumber);
            Assert.Equal("S3", f.Suitability);
            Assert.Equal("P02", f.Revision);
        }

        [Fact]
        public void ANameCarryingTheMarker_IsRefused_BeforeTheRegisterIsEvenRead()
        {
            string bad = @"C:\x\KUT-PLN-ZZ-01-DR-A-0101-S3-NOREV.pdf";
            var f = AccFileIso.Decide(bad, null, null);
            Assert.True(f.Refused);
            Assert.Contains("NOREV", f.Refusal);
        }

        [Fact]
        public void ARegisterRowFlaggedIsoUnset_IsRefused_EvenWithACustomName()
        {
            // A custom naming template may print neither field, so the name alone cannot tell.
            string custom = @"C:\x\A-101 Ground Floor Plan.pdf";
            var f = AccFileIso.Decide(custom, null, Row(custom, "XX", "P02", null, "suitability"));
            Assert.True(f.Refused);
            Assert.Contains("no suitability", f.Refusal);
        }

        [Fact]
        public void NotInTheRegister_SendsNoRevision_AndSaysSo()
        {
            var f = AccFileIso.Decide(@"C:\x\model.ifc", null, null);
            Assert.False(f.Refused);
            Assert.Equal("", f.Revision);
            Assert.Equal("model", f.DocumentNumber);
            Assert.Contains(f.Notes, n => n.Contains("not in the document register"));
        }

        [Fact]
        public void ARegisterMarkerRevision_IsNotSentAsARevision()
        {
            var f = AccFileIso.Decide(@"C:\x\a.pdf", null, Row(@"C:\x\a.pdf", "S3", "NOREV"));
            Assert.Equal("", f.Revision);
        }

        [Fact]
        public void TheBundleRecord_WinsForTheBundle_AndItsMissingRevisionIsExplained()
        {
            var rec = new AccBundleRecord { Path = @"C:\x\b.zip", Suitability = "S3", Revision = "", RevisionNote = "the bundle spans 2 revisions (P01, P02), so it carries none" };
            var f = AccFileIso.Decide(rec.Path, rec, null);
            Assert.Equal("S3", f.Suitability);
            Assert.Equal("", f.Revision);
            Assert.Contains(f.Notes, n => n.Contains("spans 2 revisions"));

            rec.Revision = "C01";
            Assert.Equal("C01", AccFileIso.Decide(rec.Path, rec, null).Revision);
        }

        [Fact]
        public void FindRegisterRow_MatchesByPath_AndRefusesAnAmbiguousName()
        {
            var reg = new JArray(Row(@"C:\a\x.pdf", rev: "P01"), Row(@"C:\b\x.pdf", rev: "P02"));
            Assert.Equal("P02", (string)AccFileIso.FindRegisterRow(reg, @"C:\b\x.pdf")["revision"]);
            Assert.Null(AccFileIso.FindRegisterRow(reg, @"C:\c\x.pdf"));             // two rows named x.pdf
            var one = new JArray(Row(@"C:\a\y.pdf"));
            Assert.NotNull(AccFileIso.FindRegisterRow(one, @"D:\moved\y.pdf"));     // unique name
        }
    }

    public class AccBundleRevisionTests
    {
        [Fact] public void OneSharedRevision_IsTheBundles()
            => Assert.Equal(("P03", ""), AccBundleRecord.CommonRevision(new[] { "P03", "p03", "P03" }));

        [Fact]
        public void SeveralRevisions_RecordNone_AndSaySo()
        {
            var (rev, note) = AccBundleRecord.CommonRevision(new[] { "P02", "P03" });
            Assert.Equal("", rev);
            Assert.Contains("spans 2 revisions", note);
        }

        [Fact]
        public void ADeliverableWithNoRevision_MakesTheBundlesUnknown()
        {
            var (rev, note) = AccBundleRecord.CommonRevision(new[] { "P02", "" });
            Assert.Equal("", rev);
            Assert.Contains("1 of 2", note);
        }

        [Fact]
        public void NoRevisionsAnywhere_IsNotP01()
        {
            var (rev, note) = AccBundleRecord.CommonRevision(new[] { "", null });
            Assert.Equal("", rev);
            Assert.False(string.IsNullOrEmpty(note));
        }
    }

    public class AccUploadLedgerTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-ledger-" + Guid.NewGuid().ToString("N"));
        public AccUploadLedgerTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }

        private static AccUploadLedger WithOne() {
            var l = new AccUploadLedger();
            l.Record(new AccLedgerEntry { DocumentNumber = "D-0101", Revision = "P02", Format = "pdf", Sha256 = "AAA", UploadedUtc = new DateTime(2026, 9, 1) });
            return l;
        }

        [Fact] public void NeverSent_Uploads()
            => Assert.Equal(AccLedgerDecision.Upload, new AccUploadLedger().Check("D-0101", "P02", "PDF", "aaa", false).Decision);

        [Fact]
        public void TheIdenticalFile_IsSkipped()
        {
            var v = WithOne().Check("d-0101", "P02", "PDF", "aaa", false);
            Assert.Equal(AccLedgerDecision.SkipIdentical, v.Decision);
            Assert.False(v.ShouldUpload);
            Assert.Contains("already in ACC", v.Reason);
        }

        [Fact]
        public void ChangedContent_UnderTheSameRevision_IsRefusedAsAReissue()
        {
            var v = WithOne().Check("D-0101", "P02", "PDF", "bbb", allowReissue: false);
            Assert.Equal(AccLedgerDecision.RefuseReissueWithoutRevisionChange, v.Decision);
            Assert.False(v.ShouldUpload);
            Assert.Contains("re-issue without a revision change", v.Reason);
        }

        [Fact]
        public void ChangedContent_IsSent_OnlyWhenTheProfileAllowsIt()
        {
            var v = WithOne().Check("D-0101", "P02", "PDF", "bbb", allowReissue: true);
            Assert.Equal(AccLedgerDecision.UploadReissueAllowed, v.Decision);
            Assert.True(v.ShouldUpload);
        }

        [Fact]
        public void ANewRevision_OrAnotherRendition_IsNotAReissue()
        {
            var l = WithOne();
            Assert.Equal(AccLedgerDecision.Upload, l.Check("D-0101", "P03", "PDF", "bbb", false).Decision);
            Assert.Equal(AccLedgerDecision.Upload, l.Check("D-0101", "P02", "DWG", "ccc", false).Decision);
        }

        [Theory]
        [InlineData("", "P02", "aaa")]
        [InlineData("D-0101", "", "aaa")]
        [InlineData("D-0101", "P02", null)]
        public void NoIdentity_IsRefused(string doc, string rev, string sha)
            => Assert.Equal(AccLedgerDecision.RefuseNoIdentity, new AccUploadLedger().Check(doc, rev, "PDF", sha, true).Decision);

        [Fact]
        public void SaveAndLoad_RoundTrip_AndAMissingFileIsEmpty()
        {
            string p = Path.Combine(_dir, "acc", AccUploadLedger.FileName);
            var empty = AccUploadLedger.Load(p, out string err);
            Assert.NotNull(empty); Assert.Null(err); Assert.Empty(empty.Entries);

            Assert.True(WithOne().TrySave(p, out err), err);
            Assert.True(WithOne().TrySave(p, out err), err);          // replace an existing file
            var back = AccUploadLedger.Load(p, out err);
            Assert.Single(back.Entries);
            Assert.Equal("PDF", back.Entries[0].Format);
            Assert.Equal("aaa", back.Entries[0].Sha256);
        }

        [Fact]
        public void AnUnreadableLedger_IsNotAnEmptyOne()
        {
            // Treating it as empty would re-send every file as "never sent".
            string p = Path.Combine(_dir, AccUploadLedger.FileName);
            File.WriteAllText(p, "{ not json");
            Assert.Null(AccUploadLedger.Load(p, out string err));
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void Sha256_IsOfTheBytes()
        {
            string p = Path.Combine(_dir, "f.bin");
            File.WriteAllBytes(p, Encoding.ASCII.GetBytes("abc"));
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", AccUploadLedger.Sha256OfFile(p));
            Assert.Null(AccUploadLedger.Sha256OfFile(Path.Combine(_dir, "missing")));
        }
    }

    /// <summary>Loopback: the revision a file carries reaches ACC as the ISO Revision custom
    /// attribute on the uploaded version — the whole upload, not the attribute client alone.</summary>
    public class AccUploadRevisionLoopbackTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-uprev-" + Guid.NewGuid().ToString("N"));

        public AccUploadRevisionLoopbackTests()
        {
            Directory.CreateDirectory(_dir);
            AccHttp.DelayHook = _ => Task.CompletedTask;
        }

        public void Dispose()
        {
            AccModelUpload.OverrideHostForTests(null);
            AccDocsMetadata.OverrideHostForTests(null);
            AccHttp.DelayHook = t => Task.Delay(t);
            try { Directory.Delete(_dir, true); } catch (Exception) { }
        }

        private static readonly (int id, string name)[] Defs =
        {
            (2001, AccDocsAttributeSet.DocumentNumber), (2002, AccDocsAttributeSet.Suitability),
            (2003, AccDocsAttributeSet.Revision),       (2004, AccDocsAttributeSet.CdeState),
            (2005, AccDocsAttributeSet.Originator),     (2006, AccDocsAttributeSet.TransmittalId),
        };

        private (LoopbackServer server, Func<JArray> batch) Serve()
        {
            JArray posted = null;
            LoopbackServer server = null;
            server = new LoopbackServer((i, req) =>
            {
                string p = req.Url.PathAndQuery;
                if (p.Contains("custom-attribute-definitions"))
                    return new CannedResponse(200, new JObject
                    {
                        ["results"] = new JArray(Defs.Select(d => new JObject { ["id"] = d.id, ["name"] = d.name, ["type"] = "string" })),
                        ["pagination"] = new JObject { ["limit"] = 200, ["offset"] = 0, ["totalResults"] = Defs.Length },
                    }.ToString());
                if (p.Contains("custom-attributes:batch-update"))
                {
                    using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
                    posted = JArray.Parse(sr.ReadToEnd());
                    var echo = new JArray(posted.Select(t => new JObject
                    {
                        ["id"] = t["id"], ["name"] = Defs.First(d => d.id == (int)t["id"]).name,
                        ["type"] = "string", ["value"] = t["value"],
                    }));
                    return new CannedResponse(200, new JObject { ["results"] = echo }.ToString());
                }
                if (p.Contains("/storage")) return new CannedResponse(201, "{\"data\":{\"id\":\"urn:adsk.objects:os.object:bucket/obj\"}}");
                if (req.HttpMethod == "GET" && p.Contains("signeds3upload"))
                    return new CannedResponse(200, "{\"uploadKey\":\"UK\",\"urls\":[\"" + server.BaseUrl + "/s3/1\"]}");
                if (req.HttpMethod == "PUT") return new CannedResponse(200, "");
                if (req.HttpMethod == "POST" && p.Contains("signeds3upload")) return new CannedResponse(200, "{}");
                if (p.Contains("/items"))
                    return new CannedResponse(201, "{\"data\":{\"id\":\"urn:item\"},\"included\":[{\"type\":\"versions\",\"id\":\"urn:adsk.wipprod:fs.file:vf.X?version=1\"}]}");
                return new CannedResponse(404, "{}");
            });
            AccModelUpload.OverrideHostForTests(server.BaseUrl);
            AccDocsMetadata.OverrideHostForTests(server.BaseUrl);
            return (server, () => posted);
        }

        private string NewFile()
        {
            string f = Path.Combine(_dir, "KUT-PLN-ZZ-01-DR-A-0101-S3-C02.pdf");
            File.WriteAllBytes(f, new byte[64]);
            return f;
        }

        [Fact]
        public async Task TheFilesRevision_IsStampedAsIsoRevision_OnTheUploadedVersion()
        {
            var (server, batch) = Serve();
            using var _ = server;
            var c = H.Creds();
            c.FolderUrn = "urn:adsk.wipprod:fs.folder:co.X";
            string file = NewFile();

            // The facts the upload command builds for a registered file.
            var reg = new JArray(new JObject
            {
                ["file_path"] = file, ["file_name"] = Path.GetFileName(file),
                ["doc_number"] = "KUT-PLN-ZZ-01-DR-A-0101", ["suitability"] = "S3", ["revision"] = "C02",
            });
            var facts = AccFileIso.Decide(file, null, AccFileIso.FindRegisterRow(reg, file));
            Assert.False(facts.Refused, facts.Refusal);

            var r = await AccModelUpload.UploadAsync(c, file, new AccUploadOptions
            {
                Suitability = facts.Suitability,
                Metadata = new AccDocMetadataInput
                {
                    DocumentNumber = facts.DocumentNumber, Suitability = facts.Suitability, Revision = facts.Revision,
                },
            });

            Assert.True(r.Ok, r.Message);
            Assert.True(r.MetadataComplete, r.MetadataNote);
            Assert.False(r.MetadataIncomplete);
            var body = batch();
            Assert.NotNull(body);
            var rev = body.OfType<JObject>().SingleOrDefault(t => (int)t["id"] == 2003);
            Assert.NotNull(rev);
            Assert.Equal("C02", (string)rev["value"]);
            Assert.Equal("KUT-PLN-ZZ-01-DR-A-0101", (string)body.OfType<JObject>().Single(t => (int)t["id"] == 2001)["value"]);
        }

        [Fact]
        public async Task NoRecordedRevision_LeavesIsoRevisionUnset_NotBlankNotP01()
        {
            var (server, batch) = Serve();
            using var _ = server;
            var c = H.Creds();
            c.FolderUrn = "urn:adsk.wipprod:fs.folder:co.X";
            string file = NewFile();
            var facts = AccFileIso.Decide(file, null, null);

            var r = await AccModelUpload.UploadAsync(c, file, new AccUploadOptions
            {
                Suitability = "S3",
                Metadata = new AccDocMetadataInput { DocumentNumber = facts.DocumentNumber, Suitability = "S3", Revision = facts.Revision },
            });

            Assert.True(r.Ok, r.Message);
            Assert.DoesNotContain(batch().OfType<JObject>(), t => (int)t["id"] == 2003);
        }
    }
}
