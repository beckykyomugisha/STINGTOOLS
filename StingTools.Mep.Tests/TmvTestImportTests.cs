using System;
using System.Linq;
using StingTools.Core.Plumbing;
using Xunit;

namespace StingTools.Mep.Tests
{
    /// <summary>DSCH-46 — TMV test results come back into the model from the register CSV
    /// Plumb_TMVEngine writes. Nothing is guessed: no reading = skipped, a bad cell = the
    /// row is refused with its line, and the rest still imports.</summary>
    public class TmvTestImportTests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 2);
        private const string Header = "ElementId,FamilyName,Room,Scheme,Outlet,InletHot_C,InletCold_C,Set_C,Outlet_C,TestDate,AnnualDueDate,Status,Reason";

        private static TmvTestImportResult Parse(params string[] rows) =>
            TmvTestImport.Parse(Header + "\r\n" + string.Join("\r\n", rows) + "\r\n", Today);

        [Fact]
        public void AFilledRegisterRowIsRead()
        {
            var r = Parse("1234,\"TMV3, basin\",Ward 1,TMV3,BASIN,62.0,11.5,41.0,41.3,2026-09-30,,PASS,");
            Assert.Empty(r.Errors);
            var row = Assert.Single(r.Rows);
            Assert.Equal(1234, row.ElementId);
            Assert.Equal(41.3, row.OutletC);
            Assert.Equal(62.0, row.InletHotC);
            Assert.Equal(11.5, row.InletColdC);
            Assert.Equal("2026-09-30", row.TestDate);
            Assert.Equal(2, row.Line);
        }

        [Theory]
        [InlineData("")]
        [InlineData("0.0")]   // registers before DSCH-46 printed 0.0 for "not measured"
        public void ARowWithNoOutletReadingIsSkippedNotZeroed(string outlet)
        {
            var r = Parse($"1234,F,R,TMV3,BATH,,,44.0,{outlet},,,NOT CHECKED,");
            Assert.Empty(r.Rows);
            Assert.Empty(r.Errors);
            Assert.Equal(1, r.NotMeasured);
        }

        [Fact]
        public void BlankInletsAreNotRecordedRatherThanZero()
        {
            var row = Assert.Single(Parse("7,F,R,TMV3,SHOWER,,,41.0,40.8,2026-01-15,,,").Rows);
            Assert.Null(row.InletHotC);
            Assert.Null(row.InletColdC);
        }

        [Theory]
        [InlineData("7,F,R,TMV3,BATH,,,44,44.5,,,,", "TestDate")]               // reading with no date
        [InlineData("7,F,R,TMV3,BATH,,,44,44.5,30/09/2026,,,", "TestDate")]     // not ISO: 30/09 vs 09/30 is a guess
        [InlineData("7,F,R,TMV3,BATH,,,44,44.5,2026-10-03,,,", "future")]
        [InlineData("7,F,R,TMV3,BATH,,,44,hot,2026-09-30,,,", "Outlet_C")]
        [InlineData("7,F,R,TMV3,BATH,,,44,111,2026-09-30,,,", "plausible")]     // Fahrenheit or a slip
        [InlineData("7,F,R,TMV3,BATH,140,,44,44,2026-09-30,,,", "InletHot_C")]
        [InlineData("x,F,R,TMV3,BATH,,,44,44,2026-09-30,,,", "ElementId")]
        public void ABadCellRefusesTheRowWithItsLine(string row, string expect)
        {
            var r = Parse(row, "8,F,R,TMV3,SHOWER,,,41,41,2026-09-30,,,");
            var e = Assert.Single(r.Errors);
            Assert.Contains("line 2", e);
            Assert.Contains(expect, e);
            Assert.Equal(8, Assert.Single(r.Rows).ElementId);   // the good row still imports
        }

        [Fact]
        public void AnElementListedTwiceImportsNeitherReading()
        {
            var r = Parse("9,F,R,TMV3,BATH,,,44,44,2026-09-30,,,",
                          "9,F,R,TMV3,BATH,,,44,46.5,2026-09-30,,,",
                          "10,F,R,TMV3,BATH,,,44,44,2026-09-30,,,");
            Assert.Equal(new long[] { 10 }, r.Rows.Select(x => x.ElementId).ToArray());
            Assert.Contains(r.Errors, e => e.Contains("element 9 appears more than once"));
        }

        [Fact]
        public void AFileWithoutTheRegisterColumnsIsRefusedWhole()
        {
            var r = TmvTestImport.Parse("Id,Temp\r\n1,41\r\n", Today);
            Assert.Empty(r.Rows);
            Assert.Contains("ElementId", Assert.Single(r.Errors));
            Assert.Contains("the file is empty", Assert.Single(TmvTestImport.Parse("  ", Today).Errors));
        }

        [Fact]
        public void HeaderIsMatchedByNameNotPosition()
        {
            var r = TmvTestImport.Parse("﻿TestDate,Outlet_C,ElementId\n2026-09-30,38.0,55\n", Today);
            Assert.Equal(38.0, Assert.Single(r.Rows).OutletC);
        }

        [Fact]
        public void ImportedReadingIsJudgedByTheOneTmvCheck()
        {
            // The command re-runs the TMV check after writing; the reading it imports is what
            // the never-exceed limit (set + 2, D 08) is applied to.
            var limits = WaterSafetyLimits.Parse(RepoData.Read("Plumbing/STING_TMV_STANDARDS.json"), out var errs);
            Assert.Empty(errs);
            var row = Assert.Single(Parse("11,F,R,TMV3,BATH,,,44,46.5,2026-09-30,,,").Rows);
            var c = WaterSafetyLimits.CheckTmv(limits, "BATH", "TMV3", false, null, true, 44, row.OutletC,
                                               StingTools.Standards.HTM.HtmRegion.England);
            Assert.Equal(WaterCheckStatus.Fail, c.Status);
            Assert.Contains("never-exceed", c.Reason);
        }
    }
}
