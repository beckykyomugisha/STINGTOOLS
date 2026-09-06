using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Twin;
using Xunit;

namespace StingTools.Boq.Tests
{
    // This parser sits directly under a payment decision: HasPresentValue -> NiagaraPoint
    // .HasValue -> BmsValuation.IsCommissioned -> whether a BOQ line is certified. The
    // arithmetic on the far side has always been tested; the decision feeding it never was.
    //
    // A station feed is third-party data, so the tests below care as much about what the
    // parser does with shapes we did NOT design for as with the happy path.
    public class NiagaraPointParserTests
    {
        // ---- the certification branch -------------------------------------------

        [Fact]
        public void APointReportingAValue_HasValue()
        {
            var r = NiagaraPointParser.Parse(@"[{ ""id"": ""AHU-1"", ""status"": ""ok"", ""present_value"": 21.4 }]");
            Assert.False(r.Failed);
            Assert.True(r.Points["AHU-1"].HasValue);
            Assert.Equal("ok", r.Points["AHU-1"].Status);
        }

        [Theory]
        // A point that EXISTS on the station and answers with a status, but reports no
        // value, is configured-but-dead. Reading it as live certifies money against
        // equipment that is not running — this is the branch worth asserting.
        [InlineData(@"{ ""id"": ""FCU-2"", ""status"": ""ok"", ""present_value"": null }")]
        [InlineData(@"{ ""id"": ""FCU-2"", ""status"": ""ok"", ""present_value"": """" }")]
        [InlineData(@"{ ""id"": ""FCU-2"", ""status"": ""ok"", ""present_value"": ""   "" }")]
        [InlineData(@"{ ""id"": ""FCU-2"", ""status"": ""ok"" }")]
        [InlineData(@"{ ""id"": ""FCU-2"", ""status"": ""ok"", ""present_value"": { ""val"": null } }")]
        [InlineData(@"{ ""id"": ""FCU-2"", ""status"": ""ok"", ""present_value"": { } }")]
        public void APointWithNoUsableValue_DoesNotHaveValue(string point)
        {
            var r = NiagaraPointParser.Parse("[" + point + "]");
            Assert.False(r.Failed);
            var p = Assert.Single(r.Points).Value;
            Assert.False(p.HasValue);
            Assert.Equal("ok", p.Status);   // it answered — it just is not reporting
        }

        [Theory]
        [InlineData("0")]        // a point reading zero IS reporting
        [InlineData("false")]    // so is a binary point reading off
        [InlineData(@"""0""")]
        [InlineData("-3.5")]
        public void ZeroAndFalse_AreStillPresentValues(string literal)
        {
            var r = NiagaraPointParser.Parse(
                @"[{ ""id"": ""P"", ""status"": ""ok"", ""present_value"": " + literal + " }]");
            Assert.True(r.Points["P"].HasValue);
        }

        [Fact]
        public void ObixNestedVal_IsReadOneLevelDown()
        {
            // oBIX <real val="21.4"/> arrives as { "val": 21.4 }.
            var r = NiagaraPointParser.Parse(@"[{ ""id"": ""T-1"", ""out"": { ""val"": 21.4 } }]");
            Assert.True(r.Points["T-1"].HasValue);
        }

        [Fact]
        public void HasPresentValue_IsAssertableDirectly()
        {
            Assert.False(NiagaraPointParser.HasPresentValue(null));
            Assert.False(NiagaraPointParser.HasPresentValue(JValue.CreateNull()));
            Assert.False(NiagaraPointParser.HasPresentValue(new JValue("")));
            Assert.False(NiagaraPointParser.HasPresentValue(JObject.Parse(@"{ ""val"": null }")));
            Assert.True(NiagaraPointParser.HasPresentValue(new JValue(0)));
            Assert.True(NiagaraPointParser.HasPresentValue(new JValue(false)));
            Assert.True(NiagaraPointParser.HasPresentValue(JObject.Parse(@"{ ""val"": 1 }")));
            // A JArray must be stringified, not cast — a cast throws, and a station is free
            // to send a shape we did not plan for.
            Assert.True(NiagaraPointParser.HasPresentValue(JArray.Parse("[1,2]")));
        }

        // ---- malformed and unexpected input --------------------------------------

        [Theory]
        [InlineData("{not valid")]
        [InlineData("[{ \"id\": ")]
        [InlineData("<obix/>")]              // XML, not JSON — a mis-set pointsPath
        public void MalformedJson_ReportsAnError_AndDoesNotThrow(string junk)
        {
            var r = NiagaraPointParser.Parse(junk);
            Assert.True(r.Failed);
            Assert.False(string.IsNullOrWhiteSpace(r.Error));
            Assert.Empty(r.Points);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankPayload_IsEmpty_AndNotAnError(string blank)
        {
            var r = NiagaraPointParser.Parse(blank);
            Assert.False(r.Failed);
            Assert.Empty(r.Points);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData(@"{ ""points"": [] }")]
        [InlineData(@"""a bare string""")]
        [InlineData("42")]
        [InlineData("null")]
        [InlineData(@"[ 1, 2, ""three"" ]")]           // an array of non-objects
        [InlineData(@"{ ""points"": ""not an array"" }")]
        public void ValidJsonInAShapeWeDoNotRead_IsEmpty_AndNotAnError(string json)
        {
            // "the feed parsed but held nothing we understand" is a different fact from
            // "the feed is corrupt", and the caller logs them differently.
            var r = NiagaraPointParser.Parse(json);
            Assert.False(r.Failed);
            Assert.Empty(r.Points);
        }

        [Fact]
        public void EntriesWithNoUsableId_AreCounted_NotSilentlyDropped()
        {
            // A feed naming its id field something we do not read produces an EMPTY result,
            // which downstream is indistinguishable from a station with nothing on it.
            var r = NiagaraPointParser.Parse(
                @"[{ ""pointName"": ""AHU-1"", ""present_value"": 1 },
                   { ""id"": """", ""present_value"": 1 },
                   { ""id"": ""   "", ""present_value"": 1 }]");
            Assert.False(r.Failed);
            Assert.Empty(r.Points);
            Assert.Equal(3, r.SkippedNoId);
        }

        // ---- the three shapes stations actually emit ------------------------------

        [Fact]
        public void BareArray_PointsWrapper_AndObjectKeyedById_AllParse()
        {
            var bare = NiagaraPointParser.Parse(@"[{ ""id"": ""A"", ""value"": 1 }]");
            var wrapped = NiagaraPointParser.Parse(@"{ ""points"": [{ ""id"": ""A"", ""value"": 1 }] }");
            var keyed = NiagaraPointParser.Parse(@"{ ""A"": { ""status"": ""ok"", ""value"": 1 } }");

            foreach (var r in new[] { bare, wrapped, keyed })
            {
                Assert.False(r.Failed);
                Assert.True(r.Points.ContainsKey("A"));
                Assert.True(r.Points["A"].HasValue);
            }
        }

        [Theory]
        [InlineData("deviceId")]
        [InlineData("id")]
        [InlineData("name")]
        public void IdIsReadFromAnyOfTheThreeFieldNames(string field)
        {
            var r = NiagaraPointParser.Parse(
                @"[{ """ + field + @""": ""AHU-1"", ""present_value"": 1 }]");
            Assert.True(r.Points.ContainsKey("AHU-1"));
        }

        [Fact]
        public void DeviceIdWinsOverIdAndName()
        {
            var r = NiagaraPointParser.Parse(
                @"[{ ""deviceId"": ""D"", ""id"": ""I"", ""name"": ""N"", ""value"": 1 }]");
            Assert.Equal("D", Assert.Single(r.Points).Key);
        }

        [Theory]
        [InlineData("present_value")]
        [InlineData("presentValue")]
        [InlineData("out")]
        [InlineData("value")]
        [InlineData("val")]
        public void ValueIsReadFromAnyOfTheFiveFieldNames(string field)
        {
            var r = NiagaraPointParser.Parse(
                @"[{ ""id"": ""P"", """ + field + @""": 7 }]");
            Assert.True(r.Points["P"].HasValue);
        }

        [Fact]
        public void KeysAreCaseInsensitive_SoADeviceIdCasedDifferentlyStillJoins()
        {
            // The BOQ side looks points up by the device id on the element; a station that
            // cases it differently must still match, or the asset reads as uncommissioned.
            var r = NiagaraPointParser.Parse(@"[{ ""id"": ""ahu-1"", ""value"": 1 }]");
            Assert.True(r.Points.ContainsKey("AHU-1"));
        }

        [Fact]
        public void MissingStatus_IsEmpty_WhichIsBajasOkConvention()
        {
            // BmsValuation treats "" as in-service (the Baja empty-status object). The
            // hasValue gate is what stops that becoming a free pass.
            var r = NiagaraPointParser.Parse(@"[{ ""id"": ""P"", ""value"": 1 }]");
            Assert.Equal("", r.Points["P"].Status);
            Assert.True(BmsValuation.IsCommissioned(r.Points["P"].Status, r.Points["P"].HasValue));

            var dead = NiagaraPointParser.Parse(@"[{ ""id"": ""P"" }]");
            Assert.False(BmsValuation.IsCommissioned(dead.Points["P"].Status, dead.Points["P"].HasValue));
        }

        [Fact]
        public void DuplicateId_LastWins_Deterministically()
        {
            // Arbitrary but pinned, so it cannot drift into "whichever the dictionary kept".
            var r = NiagaraPointParser.Parse(
                @"[{ ""id"": ""A"", ""status"": ""ok"",    ""value"": 1 },
                   { ""id"": ""A"", ""status"": ""fault"", ""value"": 2 }]");
            Assert.Equal("fault", Assert.Single(r.Points).Value.Status);
        }

        [Fact]
        public void NullEntriesInAnArray_AreSkipped_NotCountedAsIdless()
        {
            var r = NiagaraPointParser.Parse(@"[null, { ""id"": ""A"", ""value"": 1 }, null]");
            Assert.False(r.Failed);
            Assert.Single(r.Points);
            Assert.Equal(0, r.SkippedNoId);
        }

        // ---- end to end, through the decision the valuation actually makes --------

        [Fact]
        public void AFeedOfMixedPoints_CommissionsOnlyTheLiveOnes()
        {
            var r = NiagaraPointParser.Parse(@"{ ""points"": [
                { ""id"": ""AHU-1"", ""status"": ""ok"",       ""present_value"": 21.4 },
                { ""id"": ""FCU-2"", ""status"": ""ok"",       ""present_value"": null },
                { ""id"": ""VAV-3"", ""status"": ""fault"",    ""present_value"": 12   },
                { ""id"": ""EF-4"",  ""status"": ""{ok}"",     ""present_value"": 0    },
                { ""id"": ""P-5"",   ""status"": ""disabled"", ""present_value"": 3    }
            ] }");

            var commissioned = r.Points
                .Where(kv => BmsValuation.IsCommissioned(kv.Value.Status, kv.Value.HasValue))
                .Select(kv => kv.Key)
                .OrderBy(k => k)
                .ToArray();

            // AHU-1 live; EF-4 live and reading zero, which is still reading. FCU-2 is
            // configured-but-dead, VAV-3 is faulted, P-5 is out of service.
            Assert.Equal(new[] { "AHU-1", "EF-4" }, commissioned);
        }
    }
}
