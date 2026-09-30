// ACC naming lineage and the one set of attribute names (2026-10-01 revision review):
//   A. a name carrying "-{Suitability}-{Revision}" makes every revision a NEW ACC item (ACC
//      matches items by name) and breaks KUT's 7-field BEP name — refused, and a folder's
//      naming standard is checked before any bytes are sent;
//   B. STING wrote "ISO Suitability" etc. while the playbook told the admin to create
//      "Suitability" (a drop-down) — two drifting columns. The names are now the project's.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccAttributeNamesTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-attrnames-" + Guid.NewGuid().ToString("N"));
        public AccAttributeNamesTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }

        private AccOperatingPolicy Load(string json)
        {
            string p = Path.Combine(_dir, AccOperatingPolicy.FileName);
            File.WriteAllText(p, json);
            return AccOperatingPolicy.Load(p);
        }

        [Fact]
        public void TheDefaults_AreThePlaybookNames()
        {
            var n = AccAttributeNames.Default;
            Assert.Equal("Suitability", n.Suitability);
            Assert.Equal("Revision", n.Revision);
            Assert.Equal("Document Number", n.DocumentNumber);
            Assert.Equal("CDE State", n.CdeState);
            // The playbook (docs/KUT_ACC_DAY1_PLAYBOOK.md §3.4) names every one exactly.
            string playbook = FindRepoFile("docs", "KUT_ACC_DAY1_PLAYBOOK.md");
            if (playbook != null)
            {
                string text = File.ReadAllText(playbook);
                foreach (var name in n.All()) Assert.Contains("`" + name + "`", text);
            }
        }

        [Fact]
        public void TheProjectsNames_AreRead_AndUsedToBuildValues()
        {
            var p = Load("{\"docsAttributeNames\":{\"suitability\":\"Status Code\",\"revision\":\"Rev\"}}");
            Assert.Equal(AccPolicySource.Loaded, p.Source);
            Assert.Equal("Status Code", p.DocsAttributeNames.Suitability);
            Assert.Equal("Rev", p.DocsAttributeNames.Revision);
            Assert.Equal("Document Number", p.DocsAttributeNames.DocumentNumber);      // not given: default

            var v = AccDocsAttributeSet.Build(new AccDocMetadataInput { DocumentNumber = "D-1", Suitability = "S3", Revision = "P02" },
                                              p.DocsAttributeNames);
            Assert.Equal("S3", v.Values["Status Code"]);
            Assert.Equal("P02", v.Values["Rev"]);
            Assert.False(v.Values.ContainsKey("Suitability"));
            Assert.Contains(p.DocsAttributeNames.Specs(), s => s.Name == "Status Code" && s.Accepts("array"));
        }

        [Theory]
        [InlineData("{\"docsAttributeNames\":{\"suitabilty\":\"X\"}}", "suitabilty")]                       // typo
        [InlineData("{\"docsAttributeNames\":{\"suitability\":\"Rev\",\"revision\":\"Rev\"}}", "more than one")]
        [InlineData("{\"docsAttributeNames\":{\"revision\":\"\"}}", "empty")]
        public void ABadNameMap_DiscardsTheWholeFile(string json, string why)
        {
            var p = Load(json);
            Assert.Equal(AccPolicySource.Malformed, p.Source);
            Assert.Contains(why, p.LoadError);
            Assert.Equal("Suitability", p.DocsAttributeNames.Suitability);
        }

        [Fact]
        public void SevenFieldNaming_IsTheDefaultOnceAccIsConfigured_AndCanBeSaidExplicitly()
        {
            Assert.False(AccOperatingPolicy.Interactive().SevenFieldNaming);                        // no settings file
            Assert.False(Load("{\"unattended\":false}").SevenFieldNaming);                           // settings, no ACC project
            Assert.True(Load("{\"projectId\":\"b.1\"}").SevenFieldNaming);                           // ACC configured
            Assert.False(Load("{\"projectId\":\"b.1\",\"fileNamingFields\":9}").SevenFieldNaming);   // opted out
            Assert.True(Load("{\"fileNamingFields\":7}").SevenFieldNaming);
            var bad = Load("{\"fileNamingFields\":8}");
            Assert.Equal(AccPolicySource.Malformed, bad.Source);
            Assert.Contains("fileNamingFields", bad.LoadError);
        }

        internal static string FindRepoFile(params string[] parts)
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null)
            {
                string p = Path.Combine(new[] { d.FullName }.Concat(parts).ToArray());
                if (File.Exists(p)) return p;
                d = d.Parent;
            }
            return null;
        }
    }

    public class AccNamingStandardTests
    {
        [Theory]
        [InlineData("KUT-SMB-ZZ-01-DR-A-0101-S3-P02.pdf", "S3", "P02", true)]
        [InlineData("KUT-SMB-ZZ-01-DR-A-0101-S3-P02.pdf", null, null, true)]      // shape, values unknown
        [InlineData("KUT-SMB-ZZ-01-DR-A-0101-XX-NOREV.pdf", null, null, true)]
        [InlineData("KUT-SMB-ZZ-01-DR-A-0101.pdf", "S3", "P02", false)]           // the 7-field name
        [InlineData("KUT-SMB-ZZ-XX-M3-A-0001.rvt", null, null, false)]
        [InlineData("ACC_PUBLISH_KUT_20260930_101500.zip", "S3", "", false)]
        public void NameEmbedsStatus(string name, string suit, string rev, bool expected)
            => Assert.Equal(expected, AccNamingStandard.NameEmbedsStatus(name, suit, rev));

        [Fact]
        public void FolderNamingStandardIds_AreReadFromTheDataManagementRecord()
        {
            string folder = "{\"data\":{\"type\":\"folders\",\"id\":\"urn:f\",\"attributes\":{\"extension\":{\"data\":{\"namingStandardIds\":[\"ns-1\"]}}}}}";
            Assert.Equal(new[] { "ns-1" }, AccNamingStandard.ParseFolderNamingStandardIds(folder));
            Assert.Empty(AccNamingStandard.ParseFolderNamingStandardIds("{\"data\":{\"attributes\":{}}}"));
            Assert.Null(AccNamingStandard.ParseFolderNamingStandardIds("<html>"));
        }

        internal const string SevenFieldStandard = @"{""id"":""ns-1"",""name"":""KUT BEP 4.2"",
            ""definition"":{""delimiter"":""HYPHEN"",""fields"":[
              {""name"":""Project"",""type"":""FIXED"",""value"":""KUT""},
              {""name"":""Originator"",""values"":[""SMB"",""PLN""]},
              {""name"":""Volume""},{""name"":""Level""},{""name"":""Type""},{""name"":""Role""},{""name"":""Number""}]}}";

        [Fact]
        public void ASevenFieldStandard_AcceptsTheSevenFieldName_AndRefusesNine()
        {
            var spec = AccNamingStandard.ParseStandard(SevenFieldStandard, "ns-1");
            Assert.True(spec.Interpretable);
            Assert.Equal("-", spec.Delimiter);
            Assert.Equal(7, spec.Fields.Count);
            Assert.Equal("KUT BEP 4.2", spec.Name);

            var ok = AccNamingStandard.Validate(spec, "KUT-SMB-ZZ-01-DR-A-0101.pdf");
            Assert.True(ok.Conforms);
            Assert.Empty(ok.Warnings);

            var nine = AccNamingStandard.Validate(spec, "KUT-SMB-ZZ-01-DR-A-0101-S3-P02.pdf");
            Assert.False(nine.Conforms);
            Assert.Contains("9 field", nine.Detail);

            var badValue = AccNamingStandard.Validate(spec, "KUT-XYZ-ZZ-01-DR-A-0101.pdf");
            Assert.True(badValue.Conforms);                    // a value-list miss is reported, not refused
            Assert.Contains(badValue.Warnings, w => w.Contains("XYZ"));
        }

        [Theory]
        [InlineData("{\"id\":\"ns\",\"name\":\"x\"}")]                                   // no fields, no delimiter
        [InlineData("{\"fields\":[{\"name\":\"a\"}],\"delimiter\":\"TILDE_WORD\"}")]     // delimiter not understood
        [InlineData("not json")]
        public void AStandardThatCannotBeInterpreted_IsUndecided_NotAPassOrARefusal(string json)
        {
            var c = AccNamingStandard.Validate(AccNamingStandard.ParseStandard(json), "KUT-SMB-ZZ-01-DR-A-0101.pdf");
            Assert.Null(c.Conforms);
            Assert.Contains("NOT validated", c.Detail);
        }
    }

    /// <summary>The uploader runs the naming checks BEFORE any bytes: a refused file never
    /// reaches /storage.</summary>
    public class AccUploadNamingLoopbackTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-upname-" + Guid.NewGuid().ToString("N"));
        public AccUploadNamingLoopbackTests()
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

        private string NewFile(string name)
        {
            string f = Path.Combine(_dir, name);
            File.WriteAllBytes(f, new byte[16]);
            return f;
        }

        private static AccCredentials Creds()
        {
            var c = H.Creds();
            c.FolderUrn = "urn:adsk.wipprod:fs.folder:co.X";
            return c;
        }

        /// <summary>A fake ACC: the folder record (with or without a naming standard), the
        /// standard, the definitions (Suitability as a DROP-DOWN), batch-update and the upload.</summary>
        private LoopbackServer Serve(string namingStandardBody, List<string> suitabilityList, Action<JArray> onBatch = null)
        {
            LoopbackServer server = null;
            server = new LoopbackServer((i, req) =>
            {
                string p = req.Url.PathAndQuery;
                if (req.HttpMethod == "GET" && req.Url.AbsolutePath.StartsWith("/data/v1/projects/") && p.Contains("/folders/") && !p.Contains("/contents"))
                    return new CannedResponse(200, namingStandardBody == null
                        ? "{\"data\":{\"id\":\"urn:f\",\"attributes\":{\"extension\":{\"data\":{}}}}}"
                        : "{\"data\":{\"id\":\"urn:f\",\"attributes\":{\"extension\":{\"data\":{\"namingStandardIds\":[\"ns-1\"]}}}}}");
                if (p.Contains("/naming-standards/")) return new CannedResponse(200, namingStandardBody ?? "{}");
                if (p.Contains("custom-attribute-definitions"))
                    return new CannedResponse(200, new JObject
                    {
                        ["results"] = new JArray(
                            new JObject { ["id"] = 1, ["name"] = "Document Number", ["type"] = "string" },
                            new JObject { ["id"] = 2, ["name"] = "Suitability", ["type"] = "array", ["arrayValues"] = new JArray(suitabilityList) },
                            new JObject { ["id"] = 3, ["name"] = "Revision", ["type"] = "string" }),
                        ["pagination"] = new JObject { ["limit"] = 200, ["offset"] = 0, ["totalResults"] = 3 },
                    }.ToString());
                if (p.Contains("custom-attributes:batch-update"))
                {
                    using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
                    var arr = JArray.Parse(sr.ReadToEnd());
                    onBatch?.Invoke(arr);
                    return new CannedResponse(200, new JObject { ["results"] = new JArray(arr.Select(t => new JObject { ["id"] = t["id"], ["value"] = t["value"] })) }.ToString());
                }
                if (p.Contains("/storage")) return new CannedResponse(201, "{\"data\":{\"id\":\"urn:adsk.objects:os.object:bucket/obj\"}}");
                if (req.HttpMethod == "GET" && p.Contains("signeds3upload"))
                    return new CannedResponse(200, "{\"uploadKey\":\"UK\",\"urls\":[\"" + server.BaseUrl + "/s3/1\"]}");
                if (req.HttpMethod == "PUT") return new CannedResponse(200, "");
                if (req.HttpMethod == "POST" && p.Contains("signeds3upload")) return new CannedResponse(200, "{}");
                if (p.Contains("/contents")) return new CannedResponse(200, "{\"data\":[]}");
                if (p.Contains("/items"))
                    return new CannedResponse(201, "{\"data\":{\"id\":\"urn:item\"},\"included\":[{\"type\":\"versions\",\"id\":\"urn:adsk.wipprod:fs.file:vf.X?version=1\"}]}");
                return new CannedResponse(404, "{}");
            });
            AccModelUpload.OverrideHostForTests(server.BaseUrl);
            AccDocsMetadata.OverrideHostForTests(server.BaseUrl);
            return server;
        }

        private static AccUploadOptions Opts(bool seven, bool check, string rev = "P02") => new AccUploadOptions
        {
            Suitability = "S3",
            SevenFieldNaming = seven,
            CheckNamingStandard = check,
            Metadata = new AccDocMetadataInput { DocumentNumber = "KUT-SMB-ZZ-01-DR-A-0101", Suitability = "S3", Revision = rev },
        };

        [Fact]
        public async Task SevenFieldProject_RefusesARevisionBearingName_BeforeAnyRequest()
        {
            using var server = Serve(null, new List<string> { "S3" });
            var r = await AccModelUpload.UploadAsync(Creds(), NewFile("KUT-SMB-ZZ-01-DR-A-0101-S3-P02.pdf"), Opts(seven: true, check: true));
            Assert.False(r.Ok);
            Assert.Contains("NEW item", r.Message);
            Assert.DoesNotContain(server.Paths, p => p.Contains("/storage"));
        }

        [Fact]
        public async Task AFolderWithANamingStandard_RefusesANineFieldName_AndSendsNoBytes()
        {
            using var server = Serve(AccNamingStandardTests.SevenFieldStandard, new List<string> { "S3" });
            var r = await AccModelUpload.UploadAsync(Creds(), NewFile("KUT-SMB-ZZ-01-DR-A-0101-S3-P02.pdf"), Opts(seven: false, check: true));
            Assert.False(r.Ok);
            Assert.Contains("naming standard", r.Message);
            Assert.DoesNotContain(server.Paths, p => p.Contains("/storage"));
        }

        [Fact]
        public async Task AConformingSevenFieldName_Uploads_AndReportsTheCheck_WithSuitabilityOnTheDropDown()
        {
            JArray sent = null;
            using var server = Serve(AccNamingStandardTests.SevenFieldStandard, new List<string> { "S2", "S3", "S4" }, a => sent = a);
            var r = await AccModelUpload.UploadAsync(Creds(), NewFile("KUT-SMB-ZZ-01-DR-A-0101.pdf"), Opts(seven: true, check: true));
            Assert.True(r.Ok, r.Message);
            Assert.Contains("7 fields", r.NamingNote);
            Assert.NotNull(sent);
            Assert.Equal("S3", (string)sent.OfType<JObject>().Single(t => (int)t["id"] == 2)["value"]);   // the admin's drop-down
            Assert.Equal("P02", (string)sent.OfType<JObject>().Single(t => (int)t["id"] == 3)["value"]);
        }

        [Fact]
        public async Task ASuitabilityNotOnTheDropDown_IsNotWritten_AndSaysWhy()
        {
            JArray sent = null;
            using var server = Serve(null, new List<string> { "S2", "S4" }, a => sent = a);
            var r = await AccModelUpload.UploadAsync(Creds(), NewFile("KUT-SMB-ZZ-01-DR-A-0101.pdf"), Opts(seven: true, check: true));
            Assert.True(r.Ok, r.Message);                           // the upload stands; the stamp does not
            Assert.False(r.MetadataComplete);
            Assert.Contains("not one of its drop-list values", r.MetadataNote);
            Assert.True(r.MetadataIncomplete);                      // A11: visible to the caller as one flag
            Assert.Null(sent);                                       // nothing partial was sent
        }

        [Fact]
        public async Task AStandardThatCannotBeInterpreted_IsReported_NotGuessed()
        {
            using var server = Serve("{\"id\":\"ns-1\",\"name\":\"Mystery\"}", new List<string> { "S3" });
            var r = await AccModelUpload.UploadAsync(Creds(), NewFile("KUT-SMB-ZZ-01-DR-A-0101.pdf"), Opts(seven: true, check: true));
            Assert.True(r.Ok, r.Message);
            Assert.Contains("NOT validated", r.NamingNote);
        }

        [Fact]
        public async Task WithoutTheCheck_NoNamingRequestIsMade()
        {
            using var server = Serve(AccNamingStandardTests.SevenFieldStandard, new List<string> { "S3" });
            var r = await AccModelUpload.UploadAsync(Creds(), NewFile("KUT-SMB-ZZ-01-DR-A-0101.pdf"), Opts(seven: false, check: false));
            Assert.True(r.Ok, r.Message);
            Assert.DoesNotContain(server.Paths, p => p.Contains("naming-standards"));
            Assert.Equal("", r.NamingNote);
        }
    }
}
