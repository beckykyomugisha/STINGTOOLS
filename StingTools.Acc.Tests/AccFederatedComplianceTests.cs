// ACC_FederatedCompliance: ISO 19650 tag compliance of every model in the ACC federation,
// read through the ACC Model Properties (Index) API. Driven over a real loopback listener
// against the real client (AccModelProperties), then through the Revit-free tally
// (AccFederatedCompliance).
//
// The load-bearing assertions are the failure ones: an index that FAILED, never finished, or
// was refused must come back as a failed read - never as "0 elements", which the tally would
// otherwise render as a model with nothing wrong in it.
//
// NOT covered here (Revit-bound, verified by build only): AccFederatedComplianceCommand -
// resolving ParamRegistry names, category labels, the ISO19650Validator code lists, and
// writing the report files.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccFederatedComplianceTests : IDisposable
    {
        private const string Project = "11111111-2222-3333-4444-555555555555";
        private const string UrnArch = "urn:adsk.wipprod:fs.file:vf.ARCH?version=3";
        private const string UrnMep = "urn:adsk.wipprod:fs.file:vf.MEP?version=5";

        private static readonly string[] Tokens =
            { "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT", "ASS_LVL_COD_TXT",
              "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT" };
        private const string Tag = "ASS_TAG_1_TXT";

        public AccFederatedComplianceTests()
        {
            AccModelCoordSync.DelayHook = _ => Task.CompletedTask;
        }

        public void Dispose()
        {
            AccModelCoordSync.OverrideHostForTests(null);
            AccModelCoordSync.DelayHook = t => Task.Delay(t);
        }

        private static AccCredentials Creds() => new AccCredentials
        {
            ClientId = "c", ClientSecret = "s", RefreshToken = "r",
            ProjectId = "b." + Project, HubId = "b.hub-1",
            AccessToken = "tok", AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
        };

        private static AccModelPropertiesOptions Options() => new AccModelPropertiesOptions
        {
            ParameterNames = Tokens.Concat(new[] { Tag }).ToList(),
            CategoryNames = new List<string> { "Mechanical Equipment", "Doors", "O'Brien's Category" },
            AlwaysIncludeWhenPresent = Tag,
            Timeout = TimeSpan.FromMinutes(2),
            MinPoll = TimeSpan.FromSeconds(5),
            MaxPoll = TimeSpan.FromSeconds(30),
        };

        private static byte[] Gzip(string text)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                var b = Encoding.UTF8.GetBytes(text);
                gz.Write(b, 0, b.Length);
            }
            return ms.ToArray();
        }

        private static CannedResponse Gz(string text) => new CannedResponse(200, "", "application/gzip") { BodyBytes = Gzip(text) };

        private static string ReadBody(HttpListenerRequest req)
        {
            using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
            return sr.ReadToEnd();
        }

        /// <summary>Fields: _RC, name, DISC under TWO groups (two keys), the other tokens and the
        /// tag - except ASS_SEQ_NUM_TXT in the MEP model, which is absent.</summary>
        private static string Fields(bool withSeq, bool revit = true)
        {
            var lines = new List<string>();
            if (revit) lines.Add("{\"key\":\"pRC\",\"category\":\"__category__\",\"type\":\"String\",\"name\":\"_RC\",\"uom\":null}");
            lines.Add("{\"key\":\"pNM\",\"category\":\"__name__\",\"type\":\"String\",\"name\":\"name\",\"uom\":null}");
            lines.Add("{\"key\":\"pD1\",\"category\":\"Identity Data\",\"type\":\"String\",\"name\":\"ASS_DISCIPLINE_COD_TXT\",\"uom\":null}");
            lines.Add("{\"key\":\"pD2\",\"category\":\"Other\",\"type\":\"String\",\"name\":\"ASS_DISCIPLINE_COD_TXT\",\"uom\":null}");
            // A Viewer-internal field sharing a STING name must never be taken for the parameter.
            lines.Add("{\"key\":\"pXX\",\"category\":\"__internal__\",\"type\":\"String\",\"name\":\"ASS_LOC_TXT\",\"uom\":null}");
            int i = 0;
            foreach (var t in Tokens.Skip(1).Concat(new[] { Tag }))
            {
                if (!withSeq && t == "ASS_SEQ_NUM_TXT") continue;
                lines.Add($"{{\"key\":\"pT{i++}\",\"category\":\"Other\",\"type\":\"String\",\"name\":\"{t}\",\"uom\":null}}");
            }
            return string.Join("\n", lines) + "\n";
        }

        /// <summary>A full federation fake. <paramref name="rowsByUrn"/> are the aliased result rows.
        /// <paramref name="processingPolls"/> status GETs answer PROCESSING before FINISHED.</summary>
        private static LoopbackServer Fake(Dictionary<string, string> rowsByUrn, List<string> queryBodies = null,
            int processingPolls = 1, Func<string, CannedResponse> overrideIndex = null, bool mepIsRevit = true)
        {
            int polls = 0;
            return new LoopbackServer((_, req) =>
            {
                string path = req.Url.AbsolutePath;
                string b = "/construction/index/v2/projects/" + Project;
                if (path == b + "/indexes:batch-status" && req.HttpMethod == "POST")
                {
                    var urn = (string)JObject.Parse(ReadBody(req))["versions"][0]["versionUrn"];
                    var o = overrideIndex?.Invoke(urn);
                    if (o != null) return o;
                    string id = urn == UrnArch ? "IDXA" : "IDXM";
                    return new CannedResponse(200, new JObject
                    {
                        ["indexes"] = new JArray(new JObject
                        {
                            ["indexId"] = id, ["state"] = "PROCESSING", ["versionUrns"] = new JArray(urn),
                            ["retryAt"] = "2020-01-01T00:00:00Z",
                        })
                    }.ToString());
                }
                if (path == b + "/indexes/IDXA" || path == b + "/indexes/IDXM")
                {
                    bool done = ++polls > processingPolls;
                    return new CannedResponse(200, new JObject
                    {
                        ["indexId"] = path.EndsWith("IDXA") ? "IDXA" : "IDXM",
                        ["state"] = done ? "FINISHED" : "PROCESSING",
                        ["stats"] = new JObject { ["objects"] = 1234 },
                    }.ToString());
                }
                if (path == b + "/indexes/IDXA/fields") return Gz(Fields(withSeq: true));
                if (path == b + "/indexes/IDXM/fields") return Gz(Fields(withSeq: false, revit: mepIsRevit));
                if (path.EndsWith("/queries") && req.HttpMethod == "POST")
                {
                    queryBodies?.Add(ReadBody(req));
                    return new CannedResponse(200, "{\"queryId\":\"Q1\",\"state\":\"RUNNING\"}");
                }
                if (path.EndsWith("/queries/Q1")) return new CannedResponse(200, "{\"queryId\":\"Q1\",\"state\":\"FINISHED\"}");
                if (path == b + "/indexes/IDXA/queries/Q1/properties") return Gz(rowsByUrn[UrnArch]);
                if (path == b + "/indexes/IDXM/queries/Q1/properties") return Gz(rowsByUrn[UrnMep]);
                return new CannedResponse(404, "{\"unexpected\":\"" + path + "\"}");
            });
        }

        private static string Row(string uid, string cat, params string[] tokenValuesThenTag)
        {
            var o = new JObject { ["uid"] = uid, ["cat"] = cat, ["nm"] = cat + " " + uid };
            for (int i = 0; i < tokenValuesThenTag.Length; i++)
                if (tokenValuesThenTag[i] != null) o["v" + i] = tokenValuesThenTag[i];
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string[] Full(string disc, string seq, string tag)
            => new[] { disc, "BLD1", "Z01", "L01", "HVAC", "SUP", "AHU", seq, tag };

        private static readonly AccModelSetDocument[] Docs =
        {
            new AccModelSetDocument { VersionUrn = UrnArch, DisplayName = "KUT_ARCH.rvt" },
            new AccModelSetDocument { VersionUrn = UrnArch, DisplayName = "KUT_ARCH.rvt" },   // second viewable, same file
            new AccModelSetDocument { VersionUrn = UrnMep, DisplayName = "KUT_MEP.rvt" },
        };

        // ── The read ─────────────────────────────────────────────────────────

        [Fact]
        public async Task ReadFederation_IndexesPollsQueries_AndAttributesRowsToTheirModel()
        {
            var bodies = new List<string>();
            var rows = new Dictionary<string, string>
            {
                [UrnArch] = Row("a-1", "Doors", Full("A", "0001", "A-BLD1-Z01-L01-HVAC-SUP-AHU-0001")) + "\n" +
                            Row("a-2", "Doors") + "\n",
                // MEP has no SEQ field, so the query's v7 column is the TAG (aliases are per model).
                [UrnMep] = Row("m-1", "Mechanical Equipment", "M", "BLD1", "Z01", "L01", "HVAC", "SUP", "AHU", "A-BLD1-Z01-L01-HVAC-SUP-AHU-0001") + "\n",
            };
            using var server = Fake(rows, bodies);
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);

            var reads = await AccModelProperties.ReadFederationAsync(Creds(), Docs, Options());

            Assert.Equal(2, reads.Count);                                        // de-duplicated by version URN
            var arch = reads.Single(r => r.VersionUrn == UrnArch);
            Assert.Equal(AccDocumentReadStatus.Read, arch.Status);
            Assert.Equal("IDXA", arch.IndexId);
            Assert.Equal(1234, arch.IndexObjects);
            Assert.Equal(2, arch.Elements.Count);
            Assert.Equal("A", arch.Elements[0].Get("ASS_DISCIPLINE_COD_TXT"));
            Assert.Equal("0001", arch.Elements[0].Get("ASS_SEQ_NUM_TXT"));
            Assert.Equal("KUT_ARCH.rvt", arch.Elements[0].DocumentName);
            Assert.Empty(arch.AbsentParameters);

            var mep = reads.Single(r => r.VersionUrn == UrnMep);
            Assert.Equal(AccDocumentReadStatus.Read, mep.Status);
            Assert.Equal(new[] { "ASS_SEQ_NUM_TXT" }, mep.AbsentParameters);  // a finding, not a failure
            Assert.Equal("A-BLD1-Z01-L01-HVAC-SUP-AHU-0001", mep.Elements[0].Get(Tag));
            Assert.Equal("", mep.Elements[0].Get("ASS_SEQ_NUM_TXT"));

            // The query: viewable rows, in-scope category OR tagged, aliased columns, DISC coalesced
            // over both keys, the internal field ignored, and the quote in a category name doubled.
            var q = JObject.Parse(bodies[0]);
            string text = q.ToString();
            Assert.Contains("s.views", text);
            Assert.Contains("'O''Brien''s Category'", text);
            Assert.Contains("'Mechanical Equipment'", text);
            Assert.Equal("s.externalId", (string)q["columns"]["uid"]);
            Assert.Equal(new JArray("s.props.pD1", "s.props.pD2").ToString(), q["columns"]["v0"]["$coalesce"].ToString());
            Assert.DoesNotContain("pXX", text);
            Assert.Contains("$notnull", text);                                   // a tagged element is in scope anywhere
        }

        [Fact]
        public async Task IndexFailed_IsAFailedRead_NeverZeroElements()
        {
            using var server = Fake(new Dictionary<string, string>(), overrideIndex: urn => new CannedResponse(200, new JObject
            {
                ["indexes"] = new JArray(new JObject
                {
                    ["indexId"] = "IDXF", ["state"] = "FAILED", ["versionUrns"] = new JArray(urn),
                    ["errors"] = new JArray(new JObject { ["type"] = "SVF2", ["title"] = "Translation failed" }),
                })
            }.ToString()));
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);

            var r = await AccModelProperties.ReadDocumentAsync(Creds(), UrnArch, "KUT_ARCH.rvt", Options());
            Assert.Equal(AccDocumentReadStatus.Failed, r.Status);
            Assert.Contains("FAILED", r.Detail);
            Assert.Contains("Translation failed", r.Detail);
            Assert.Empty(r.Elements);

            var report = AccFederatedCompliance.Build(new[] { r }, Tokens, Tag);
            Assert.False(report.Complete);
            Assert.Equal(0, report.Federation.Scanned);
            Assert.StartsWith("INCOMPLETE", report.Headline());
        }

        [Fact]
        public async Task IndexNeverFinishes_GivesUpWithinTheBudget_AsAFailure()
        {
            using var server = Fake(new Dictionary<string, string>(), processingPolls: int.MaxValue);
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            var opts = Options();
            opts.Timeout = TimeSpan.FromMinutes(1);   // 5 s min poll → a bounded number of polls

            var r = await AccModelProperties.ReadDocumentAsync(Creds(), UrnArch, "KUT_ARCH.rvt", opts);
            Assert.Equal(AccDocumentReadStatus.Failed, r.Status);
            Assert.Contains("gave up", r.Detail);
            Assert.InRange(server.RequestCount, 2, 20);
        }

        [Fact]
        public async Task Forbidden_IsAnAuthFailure()
        {
            using var server = LoopbackServer.Always(403, "{\"detail\":\"no View+Download on this folder\"}");
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);

            var r = await AccModelProperties.ReadDocumentAsync(Creds(), UrnArch, "KUT_ARCH.rvt", Options());
            Assert.Equal(AccDocumentReadStatus.Failed, r.Status);
            Assert.Equal(AccFetchStatus.AuthFailed, r.FetchStatus);
            Assert.Equal(403, r.HttpStatus);
        }

        [Fact]
        public async Task BatchStatusWithoutIndexes_IsASchemaFailure()
        {
            using var server = LoopbackServer.Always(200, "{\"somethingElse\":[]}");
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            var r = await AccModelProperties.ReadDocumentAsync(Creds(), UrnArch, "KUT_ARCH.rvt", Options());
            Assert.Equal(AccDocumentReadStatus.Failed, r.Status);
            Assert.Equal(AccFetchStatus.TransportFailed, r.FetchStatus);
        }

        [Fact]
        public async Task ANonRevitModel_IsListedAsNotRevit_NotCountedAndNotAFailure()
        {
            var rows = new Dictionary<string, string>
            {
                [UrnArch] = Row("a-1", "Doors", Full("A", "0001", "T1")) + "\n",
                [UrnMep] = "",
            };
            using var server = Fake(rows, mepIsRevit: false);
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);

            var reads = await AccModelProperties.ReadFederationAsync(Creds(), Docs, Options());
            Assert.Equal(AccDocumentReadStatus.NotRevit, reads.Single(r => r.VersionUrn == UrnMep).Status);
            var report = AccFederatedCompliance.Build(reads, Tokens, Tag);
            Assert.True(report.Complete);
            Assert.Equal(1, report.Federation.Scanned);
            Assert.Single(report.NotRevit);
        }

        [Fact]
        public async Task MalformedResultLine_FailsTheRead_RatherThanShrinkingTheModel()
        {
            var rows = new Dictionary<string, string>
            {
                [UrnArch] = Row("a-1", "Doors") + "\n{not json\n",
                [UrnMep] = "",
            };
            using var server = Fake(rows);
            AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
            var r = await AccModelProperties.ReadDocumentAsync(Creds(), UrnArch, "KUT_ARCH.rvt", Options());
            Assert.Equal(AccDocumentReadStatus.Failed, r.Status);
            Assert.Empty(r.Elements);
        }

        // ── Pure halves ──────────────────────────────────────────────────────

        [Fact]
        public void FieldMap_CollectsEveryKeyByName_AndNamesTheAbsent()
        {
            var fields = AccModelProperties.ParseFields(Fields(withSeq: false));
            var map = AccPropertyFieldMap.Build(fields, Tokens.Concat(new[] { Tag }));
            Assert.Equal(new[] { "pRC" }, map.CategoryKeys);
            Assert.Equal(new[] { "pNM" }, map.NameKeys);
            Assert.Equal(new[] { "pD1", "pD2" }, map.KeysByParameter["ASS_DISCIPLINE_COD_TXT"]);
            Assert.DoesNotContain("pXX", map.KeysByParameter["ASS_LOC_TXT"]);
            Assert.Equal(new[] { "ASS_SEQ_NUM_TXT" }, map.AbsentParameters);
            Assert.False(map.ColumnByParameter.ContainsKey("ASS_SEQ_NUM_TXT"));
        }

        [Fact]
        public void ParseRows_AcceptsTheRawIndexRowShape_Too()
        {
            var map = AccPropertyFieldMap.Build(AccModelProperties.ParseFields(Fields(withSeq: true)), Tokens.Concat(new[] { Tag }));
            var read = new AccDocumentRead { DocumentName = "KUT_ARCH.rvt", VersionUrn = UrnArch };
            string raw = "{\"svf2Id\":68,\"externalId\":\"uid-9\",\"props\":{\"pRC\":\"Doors\",\"pNM\":\"Door [1]\",\"pD2\":\"A\",\"pT0\":\"BLD1\"},\"views\":[\"v\"]}\n" +
                         "{\"svf2Id\":68,\"externalId\":\"uid-9\",\"props\":{\"pRC\":\"Doors\"},\"views\":[\"v\"]}\n";
            AccModelProperties.ParseRows(raw, map, read);
            var el = Assert.Single(read.Elements);                   // the repeated row is one element
            Assert.Equal("uid-9", el.ExternalId);
            Assert.Equal("Doors", el.Category);
            Assert.Equal("A", el.Get("ASS_DISCIPLINE_COD_TXT"));      // found under the SECOND key
            Assert.Equal("BLD1", el.Get("ASS_LOC_TXT"));
            Assert.False(el.Values.ContainsKey("ASS_ZONE_TXT"));
        }

        private static AccElementRecord El(string doc, string uid, string[] tokensThenTag)
        {
            var e = new AccElementRecord { DocumentName = doc, ExternalId = uid, Category = "Doors" };
            for (int i = 0; i < Tokens.Length && i < tokensThenTag.Length; i++)
                if (tokensThenTag[i] != null) e.Values[Tokens[i]] = tokensThenTag[i];
            if (tokensThenTag.Length > Tokens.Length && tokensThenTag[Tokens.Length] != null) e.Values[Tag] = tokensThenTag[Tokens.Length];
            return e;
        }

        private static AccDocumentRead Doc(string name, params AccElementRecord[] els)
        {
            var d = new AccDocumentRead { DocumentName = name, VersionUrn = name, Status = AccDocumentReadStatus.Read };
            d.Elements.AddRange(els);
            return d;
        }

        [Fact]
        public void Tally_CountsStates_MissingTokens_InvalidCodes_AndCrossModelDuplicates()
        {
            var arch = Doc("ARCH.rvt",
                El("ARCH.rvt", "a1", Full("A", "0001", "DUP-1")),
                El("ARCH.rvt", "a2", new[] { "A", "BLD1", null, null, null, null, null, null, null }),   // partial
                El("ARCH.rvt", "a3", new string[9]),                                                    // untagged
                El("ARCH.rvt", "a4", Full("QQ", "0002", "UNIQUE-2")));                                   // invalid DISC
            var mep = Doc("MEP.rvt",
                El("MEP.rvt", "m1", Full("M", "0001", "dup-1 ")),                                        // same tag, other model
                El("MEP.rvt", "m2", Full("M", "0003", "")));                                             // tokens but no tag → partial
            var vocab = new AccComplianceVocabulary();
            vocab.Add("ASS_DISCIPLINE_COD_TXT", new[] { "A", "M", "E" });

            var r = AccFederatedCompliance.Build(new[] { arch, mep }, Tokens, Tag, vocab);

            Assert.True(r.Complete);
            Assert.Equal(6, r.Federation.Scanned);
            Assert.Equal(3, r.Federation.Full);        // a1, a4, m1
            Assert.Equal(2, r.Federation.Partial);     // a2, m2
            Assert.Equal(1, r.Federation.Untagged);    // a3
            Assert.Equal(50.0, r.Federation.FullPct.Value, 3);
            Assert.Equal(1, r.Federation.WithInvalidCode);
            Assert.Equal(2, r.Federation.MissingByToken["ASS_ZONE_TXT"]);   // a2 + a3

            var dup = Assert.Single(r.Duplicates);                          // case/whitespace-insensitive
            Assert.True(dup.CrossModel);
            Assert.Equal(1, r.CrossModelDuplicates);
            Assert.Equal(2, r.Federation.DuplicateTagged);
            Assert.Equal(new[] { "ARCH.rvt", "MEP.rvt" }, dup.Occurrences.Select(o => o.DocumentName).ToArray());

            Assert.Equal(4, r.Documents[0].Counts.Scanned);
            Assert.Equal(2, r.ByDiscipline["M"].Scanned);
            Assert.Equal(1, r.ByDiscipline[AccFederatedCompliance.NoDiscipline].Scanned);
            Assert.Contains("ACROSS models", r.Headline());

            string summary = AccFederatedCompliance.SummaryCsv(r, "hdr");
            Assert.Contains("Federation,ALL,Complete,6,3,2,1,50.0", summary);
            Assert.Contains("DUP-1,YES,2,ARCH.rvt", summary);
            string elements = AccFederatedCompliance.ElementsCsv(r);
            var listed = elements.Split('\n').Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')[1]).ToList();
            // Worklist = not fully tagged, invalid code, or duplicate. m2 is partial; a1/m1 duplicate; a4 invalid.
            Assert.Equal(new[] { "a1", "a2", "a3", "a4", "m1", "m2" }, listed.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void EmptyScope_HasNoPercentage_AndIsNotCalledCompliant()
        {
            var r = AccFederatedCompliance.Build(new[] { Doc("ARCH.rvt") }, Tokens, Tag);
            Assert.True(r.Complete);
            Assert.Null(r.Federation.FullPct);
            Assert.StartsWith("NO ELEMENTS IN SCOPE", r.Headline());
            Assert.Contains("n/a (none in scope)", AccFederatedCompliance.SummaryCsv(r));
        }

        [Fact]
        public void Csv_NeutralisesFormulaInjection_FromModelData()
        {
            Assert.Equal("'=HYPERLINK(1)", AccFederatedCompliance.Csv("=HYPERLINK(1)"));
            Assert.Equal("\"a,b\"", AccFederatedCompliance.Csv("a,b"));
        }

        // ── Latest model set version ─────────────────────────────────────────

        [Fact]
        public async Task LatestModelSetVersion_ReadsTheDocuments_AndA200WithoutThemIsAFailure()
        {
            using (var server = new LoopbackServer((_, req) =>
                req.Url.AbsolutePath.EndsWith("/modelsets/ms-1/versions/latest")
                    ? new CannedResponse(200, "{\"version\":42,\"status\":\"Successful\",\"documentVersions\":[{\"versionUrn\":\"" + UrnArch + "\",\"displayName\":\"KUT_ARCH.rvt\"}]}")
                    : new CannedResponse(404, "{}")))
            {
                AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
                var r = await AccModelCoordSync.GetLatestModelSetVersionAsync(Creds(), Project, "ms-1");
                Assert.True(r.Succeeded, r.Detail);
                Assert.Equal(42, r.Value.Version);
                Assert.Equal(UrnArch, Assert.Single(r.Value.Documents).VersionUrn);
            }
            using (var server = LoopbackServer.Always(200, "{\"version\":42}"))
            {
                AccModelCoordSync.OverrideHostForTests(server.BaseUrl);
                var r = await AccModelCoordSync.GetLatestModelSetVersionAsync(Creds(), Project, "ms-1");
                Assert.False(r.Succeeded);
                Assert.Equal(AccFetchStatus.TransportFailed, r.Status);
            }
        }
    }
}
