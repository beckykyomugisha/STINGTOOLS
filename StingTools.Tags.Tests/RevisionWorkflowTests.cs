// RevisionWorkflowTests.cs — the revision workflow's Revit-free rules.
//
//   * SheetRevisionResolver: a sheet is at its latest ISSUED revision, numbered on the
//     sheet; a draft above it never becomes "current".
//   * Iso19650RevisionRules: P ↔ S0-S7, C ↔ A/B/CR, AB/AR retire anything.
//   * RevisionScheme: promotion starts contractual at C01, then C02 …
//   * DeliverableRevisionRule: linked deliverables follow the Revit issue.
//   * RevisionIssueCompletion: deliverables, register and issues after an issue — and an
//     issue is never CLOSED by it.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Planscape.Docs.Templates;
using StingTools.BIMManager;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class SheetRevisionResolverTests
    {
        private static SheetRevisionFact F(long id, int seq, bool issued, string num) =>
            new SheetRevisionFact { Id = id, Sequence = seq, Issued = issued, NumberOnSheet = num };

        [Fact]
        public void CurrentIsLatestIssued_NotTheDraftAboveIt()
        {
            // Issuing P03 auto-opens a draft P04; the first P04 cloud puts it on the sheet.
            var s = SheetRevisionResolver.Resolve(new[] { F(1, 1, true, "P01"), F(3, 3, true, "P03"), F(4, 4, false, "P04") });
            Assert.Equal("P03", s.Number);
            Assert.True(s.HasDraftAhead);
            Assert.Equal("P04", s.Newest.NumberOnSheet);
            Assert.Single(s.Drafts);
        }

        [Fact]
        public void OnlyDrafts_MeansNotIssued_NumberIsBlank()
        {
            var s = SheetRevisionResolver.Resolve(new[] { F(7, 7, false, "P01") });
            Assert.Null(s.Issued);
            Assert.Equal("", s.Number);
        }

        [Fact]
        public void NoRevisions_IsBlankNotNull()
        {
            var s = SheetRevisionResolver.Resolve(null);
            Assert.Equal("", s.Number);
            Assert.False(s.HasDraftAhead);
        }

        [Fact]
        public void PerSheetNumber_IsUsed_NotTheProjectNumber()
        {
            // A sheet that joined in cycle 5: project revision 5, first on this sheet.
            var s = SheetRevisionResolver.Resolve(new[] { F(50, 5, true, "P01") });
            Assert.Equal("P01", s.Number);
        }

        [Fact]
        public void OrderingIsBySequence_NotByInputOrder()
        {
            var s = SheetRevisionResolver.Resolve(new[] { F(9, 9, true, "P02"), F(2, 2, true, "P01") });
            Assert.Equal("P02", s.Number);
        }

        [Fact]
        public void LeakCheck_DraftStampIsALeak_NativeDraftIsInformational()
        {
            var s = SheetRevisionResolver.Resolve(new[] { F(3, 3, true, "P03"), F(4, 4, false, "P04") });
            var found = SheetRevisionResolver.Check("A-101", s, new[]
            {
                new KeyValuePair<string, string>("SHT_REV_TXT", "P04"),          // draft leaked
                new KeyValuePair<string, string>("PRJ_TB_REVISION_NR_TXT", "P03"),// correct
                new KeyValuePair<string, string>("ABSENT", null),               // no parameter
            });
            var leak = Assert.Single(found, f => f.IsStamp);
            Assert.Equal("SHT_REV_TXT", leak.Source);
            Assert.Equal("P04", leak.Found);
            Assert.Equal("P03", leak.Issued);
            Assert.Single(found, f => !f.IsStamp);
        }

        [Fact]
        public void LeakCheck_ALockedTitleBlock_IsReportedAsLocked_NotALeak()
        {
            // R10: Revision Sync deliberately leaves a locked title block alone, so failing the
            // run on it would fail every cycle with a fix that cannot clear it.
            var s = SheetRevisionResolver.Resolve(new[] { F(3, 3, true, "P03") });
            var found = SheetRevisionResolver.Check("A-101", s, new[]
            {
                new KeyValuePair<string, string>("TB (locked)", "P02"),
                new KeyValuePair<string, string>("TB", "P02"),
            }, new HashSet<string> { "TB (locked)" });
            Assert.Single(found, f => f.IsStamp && f.IsLocked && f.Source == "TB (locked)");
            Assert.Single(found, f => f.IsStamp && !f.IsLocked && f.Source == "TB");
            // Without a lock set every disagreeing stamp is a leak, as before.
            Assert.All(SheetRevisionResolver.Check("A-101", s,
                new[] { new KeyValuePair<string, string>("TB (locked)", "P02") }), f => Assert.False(f.IsLocked));
        }

        [Fact]
        public void LeakCheck_StampOnNeverIssuedSheet_IsALeak()
        {
            var s = SheetRevisionResolver.Resolve(new[] { F(1, 1, false, "P01") });
            var found = SheetRevisionResolver.Check("A-1", s,
                new[] { new KeyValuePair<string, string>("SHT_REV_TXT", "P01") });
            Assert.Contains(found, f => f.IsStamp && f.Issued == "");
        }

        [Fact]
        public void NumberingPlan_ListsOnlyIssuedRevisionsThatChange()
        {
            var changes = RevisionNumberingPlan.IssuedChanges(new[]
            {
                ("A-1", 5L, true, "P05", "P01"),   // issued, changes → listed
                ("A-1", 6L, false, "P06", "P02"),  // draft → ignored
                ("A-2", 1L, true, "P01", "P01"),   // unchanged → ignored
            });
            var c = Assert.Single(changes);
            Assert.Equal("P05", c.Before);
            Assert.Equal("P01", c.After);
        }
    }

    public class Iso19650RevisionRulesTests
    {
        [Theory]
        [InlineData("P01", "S0", RevisionRuleVerdict.Consistent)]
        [InlineData("P03", "S2", RevisionRuleVerdict.Consistent)]
        [InlineData("P07", "S7", RevisionRuleVerdict.Consistent)]
        [InlineData("P03", "A1", RevisionRuleVerdict.Inconsistent)]
        [InlineData("P03", "B2", RevisionRuleVerdict.Inconsistent)]
        [InlineData("P03", "CR", RevisionRuleVerdict.Inconsistent)]
        [InlineData("C01", "A1", RevisionRuleVerdict.Consistent)]
        [InlineData("C02", "B1", RevisionRuleVerdict.Consistent)]
        [InlineData("C05", "CR", RevisionRuleVerdict.Consistent)]
        [InlineData("C01", "S2", RevisionRuleVerdict.Inconsistent)]
        [InlineData("C01", "S0", RevisionRuleVerdict.Inconsistent)]
        [InlineData("P02", "AB", RevisionRuleVerdict.Consistent)]   // retired keeps its revision
        [InlineData("C03", "AR", RevisionRuleVerdict.Consistent)]
        [InlineData("P01", "S9", RevisionRuleVerdict.Inconsistent)] // not a suitability code
        [InlineData("T01", "S2", RevisionRuleVerdict.NotApplicable)]
        [InlineData("Co01", "A1", RevisionRuleVerdict.NotApplicable)] // Co is not C
        [InlineData("", "S2", RevisionRuleVerdict.NotApplicable)]
        [InlineData("P01", "", RevisionRuleVerdict.NotApplicable)]
        [InlineData("P01", "s2 - for coordination", RevisionRuleVerdict.Consistent)]
        public void Matrix(string rev, string suit, RevisionRuleVerdict expected)
        {
            var r = Iso19650RevisionRules.Check(rev, suit);
            Assert.Equal(expected, r.Verdict);
            Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        }

        [Theory]
        [InlineData("KUT-PLN-ZZ-01-DR-A-0001-P03.pdf", "A1", RevisionRuleVerdict.Inconsistent)]
        [InlineData("KUT-PLN-ZZ-01-DR-A-0001-C01.pdf", "A1", RevisionRuleVerdict.Consistent)]
        [InlineData("bundle_2026-10-01.zip", "S2", RevisionRuleVerdict.NotApplicable)]
        public void FileName(string file, string suit, RevisionRuleVerdict expected)
            => Assert.Equal(expected, Iso19650RevisionRules.CheckFileName(file, suit).Verdict);

        [Fact]
        public void VocabularyRevisionPrefixes_AreTheCanonicalSeries()
        {
            // One vocabulary: "A" is Approved (A1/A2), As-built is "AB".
            Assert.Contains("AB", StingTools.Core.RevisionSeries.Prefixes);
            Assert.Equal("Approved", StingTools.Core.RevisionSeries.InferSeriesName("A1"));
        }
    }

    public class ContractualNumberingTests
    {
        [Theory]
        [InlineData("P01")]
        [InlineData("P03")]
        [InlineData("P07")]
        public void FirstPromotion_IsC01_WhateverThePreliminaryNumber(string p)
            => Assert.Equal("C01", RevisionScheme.Parse(null).PromoteToContractual(p));

        [Fact]
        public void LaterPromotion_TakesNextContractual()
        {
            var s = RevisionScheme.Parse(null);
            Assert.Equal("C02", s.PromoteToContractual("P05", new[] { "P01", "P02", "C01", "P05" }));
            Assert.Equal("C04", s.PromoteToContractual("P09", new[] { "C03", "Co07", "CR" }));
        }

        [Fact]
        public void HighestInSeries_IsExactPrefix()
        {
            Assert.Equal("C03", RevisionScheme.HighestInSeries(new[] { "C01", "C03", "Co09", "CR" }, "C"));
            Assert.Null(RevisionScheme.HighestInSeries(new[] { "P01" }, "C"));
        }
    }

    public class DeliverableRevisionRuleTests
    {
        private static IReadOnlyDictionary<string, string> Map(params (string s, string r)[] x) =>
            x.ToDictionary(t => t.s, t => t.r, StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void Unlinked_KeepsOwnCounter_AndSaysSo()
        {
            var d = DeliverableRevisionRule.Derive("P04", null, Map(("A-1", "P01")));
            Assert.Equal("P04", d.Revision);
            Assert.Equal(RevisionSources.Own, d.Source);
            Assert.Contains("not linked", d.Note);
        }

        [Fact]
        public void Linked_FollowsTheRevitIssue()
        {
            var d = DeliverableRevisionRule.Derive("C03", new[] { "A-1" }, Map(("A-1", "C01")));
            Assert.Equal("C01", d.Revision);
            Assert.True(d.FromSheets);
        }

        [Fact]
        public void LinkedButNotIssued_KeepsOwn()
        {
            var d = DeliverableRevisionDecisionFor(new[] { "A-1" }, Map(("A-1", "")));
            Assert.Equal(RevisionSources.Own, d.Source);
            Assert.Contains("no issued revision", d.Note);
        }

        [Fact]
        public void Disagreeing_TakesMostAdvanced_AndFlags()
        {
            var d = DeliverableRevisionRule.Derive("P01", new[] { "A-1", "A-2", "A-3" },
                Map(("A-1", "P03"), ("A-2", "C01"), ("A-3", "P09")));
            Assert.Equal("C01", d.Revision);   // contractual beats any preliminary
            Assert.True(d.SheetsDisagree);
        }

        private static DeliverableRevisionDecision DeliverableRevisionDecisionFor(string[] s, IReadOnlyDictionary<string, string> m)
            => DeliverableRevisionRule.Derive("P02", s, m);
    }

    public class RevisionIssueCompletionTests
    {
        private static RevisionIssueEvent Ev() => new RevisionIssueEvent
        {
            RevisionCode = "P03",
            Suitability = "S2",
            IssuedDate = "2026-10-01",
            User = "sting",
            Sheets =
            {
                new IssuedSheet { SheetNumber = "A-101", SheetName = "Plan", DocNumber = "KUT-PLN-ZZ-01-DR-A-0101", Revision = "P01" },
                new IssuedSheet { SheetNumber = "A-102", SheetName = "RCP", DocNumber = "", Revision = "P03" },
            },
        };

        [Fact]
        public void LinkedDeliverable_TakesSheetRevision_DateAndHistory()
        {
            var arr = JArray.Parse(@"[
              {""DocNumber"":""D-1"",""Revision"":""P07"",""Suitability"":""S0"",""SheetNumbers"":[""A-101""],""RevisionHistory"":[]},
              {""DocNumber"":""D-2"",""Revision"":""P02"",""SheetNumbers"":[]},
              {""DocNumber"":""D-3"",""Revision"":""P02"",""SheetNumbers"":[""Z-999""]}
            ]");
            var changed = RevisionIssueCompletion.ApplyToDeliverables(arr, Ev(), new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
            Assert.Equal(new[] { "D-1" }, changed);
            var d1 = (JObject)arr[0];
            Assert.Equal("P01", (string)d1["Revision"]);   // the sheet's own first issue
            Assert.Equal("S2", (string)d1["Suitability"]);
            Assert.Equal("SHARED", (string)d1["CDE"]);
            Assert.Equal("2026-10-01", (string)d1["IssuedDate"]);
            Assert.Equal(RevisionSources.Sheets, (string)d1["RevisionSource"]);
            Assert.Single((JArray)d1["RevisionHistory"]);
            Assert.Equal("P02", (string)arr[1]["Revision"]);   // unlinked untouched
            Assert.Null(arr[1]["IssuedDate"]);
        }

        [Fact]
        public void Register_AddsThenUpdates_ByDocNumber()
        {
            var reg = JArray.Parse(@"[{""doc_id"":""DOC-0007"",""document_id"":""DOC-0007"",""doc_number"":""KUT-PLN-ZZ-01-DR-A-0101"",""revision"":""P00"",""file_path"":""x.pdf""}]");
            var rep = new IssueCompletionReport();
            RevisionIssueCompletion.ApplyToRegister(reg, Ev(), new DateTime(2026, 10, 1), rep);
            Assert.Equal(1, rep.RegisterUpdated);
            Assert.Equal(1, rep.RegisterAdded);
            Assert.Equal("P01", (string)reg[0]["revision"]);
            Assert.Equal("x.pdf", (string)reg[0]["file_path"]);   // export's file kept
            Assert.Equal("2026-10-01", (string)reg[0]["date_issued"]);
            var added = (JObject)reg[1];
            Assert.Equal("A-102", (string)added["doc_number"]);    // no ISO id → sheet number, as the export keys it
            Assert.Equal("DOC-0008", (string)added["doc_id"]);
            Assert.Equal((string)added["doc_id"], (string)added["document_id"]);
            Assert.Equal("SHARED", (string)added["cde_status"]);
        }

        [Fact]
        public void MatchingIssues_AreProposed_NeverClosed()
        {
            var issues = JArray.Parse(@"[
              {""issue_id"":""I-1"",""status"":""OPEN"",""target_revision"":""P03""},
              {""issue_id"":""I-2"",""status"":""CLOSED"",""target_revision"":""P03""},
              {""issue_id"":""I-3"",""status"":""OPEN"",""target_revision"":""P04""},
              {""issue_id"":""I-4"",""status"":""In Progress"",""revision"":""p03""}
            ]");
            var p = RevisionIssueCompletion.ProposeIssueResolutions(issues, Ev(), new DateTime(2026, 10, 1));
            Assert.Equal(new[] { "I-1", "I-4" }, p);
            Assert.Equal("RESPONDED", (string)issues[0]["status"]);
            Assert.Equal("CLOSED", (string)issues[1]["status"]);
            Assert.Equal("OPEN", (string)issues[2]["status"]);
            Assert.DoesNotContain(issues, i => (string)i["status"] == "CLOSED" && (string)i["issue_id"] != "I-2");
            Assert.NotNull(issues[0]["proposed_resolution"]);
            Assert.Single((JArray)issues[0]["comments"]);
        }

        [Fact]
        public void AStaleSuitability_ThatContradictsTheNewRevision_IsClearedAndReported()
        {
            // R5: published A1 earlier, now issued at P01 with no issue suitability — never "P01 / A1".
            var ev = Ev(); ev.Suitability = "";
            var arr = JArray.Parse(@"[{""DocNumber"":""D-1"",""Revision"":""C01"",""Suitability"":""A1"",""SheetNumbers"":[""A-101""]}]");
            var rep = new IssueCompletionReport();
            RevisionIssueCompletion.ApplyToDeliverables(arr, ev, DateTime.UtcNow, rep);
            Assert.Equal("P01", (string)arr[0]["Revision"]);
            Assert.Equal("", (string)arr[0]["Suitability"]);
            Assert.Contains("A1", (string)arr[0]["IsoConflict"]);
            Assert.Contains(rep.Warnings, w => w.Contains("D-1") && w.Contains("A1"));

            // A consistent kept code stays, and clears an earlier conflict flag.
            var arr2 = JArray.Parse(@"[{""DocNumber"":""D-2"",""Revision"":""P00"",""Suitability"":""S3"",""IsoConflict"":""old"",""SheetNumbers"":[""A-101""]}]");
            RevisionIssueCompletion.ApplyToDeliverables(arr2, ev, DateTime.UtcNow);
            Assert.Equal("S3", (string)arr2[0]["Suitability"]);
            Assert.Null(arr2[0]["IsoConflict"]);
        }

        [Fact]
        public void UnknownSuitability_IsNotWritten()
        {
            var ev = Ev(); ev.Suitability = "whatever";
            var arr = JArray.Parse(@"[{""DocNumber"":""D-1"",""Revision"":""P01"",""Suitability"":""S0"",""SheetNumbers"":[""A-101""]}]");
            RevisionIssueCompletion.ApplyToDeliverables(arr, ev, DateTime.UtcNow);
            Assert.Equal("S0", (string)arr[0]["Suitability"]);
            Assert.Null(arr[0]["CDE"]);
        }
    }
}
