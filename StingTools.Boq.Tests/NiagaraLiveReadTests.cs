// The Niagara live-read path, end to end, over a real loopback listener.
//
// The defect this closes, in four compounding steps:
//   1. NiagaraPointParser.Parse correctly set Error on an unparseable body.
//   2. NiagaraJsonClient.ParsePoints logged that failure and returned r.Points ANYWAY,
//      discarding the flag.
//   3. That empty dictionary is not null, so CommissioningSource.Resolve took the LIVE
//      branch and reported "live (captured ...)".
//   4. Persist then wrote the empty result over last_station_points.json - destroying the
//      last good snapshot, which is the fallback that exists for an unreachable station.
//
// KUT_ValuationFromBms turns those points into a commissioning valuation percentage that
// the cost module carries toward payment certification. A fabricated 0% presented as live,
// with the recovery cache deleted, is the most consequential defect in these integrations.
//
// The worst input is {"error":"unauthorized"}: it PARSES, yields zero points, and sets no
// error at all, so nothing anywhere said anything was wrong.
//
// A test over NiagaraPointParser alone would have passed against every version of this
// bug - the parser was already right. So these drive the real client and the real
// resolver, and the cache assertion is a FILE-CONTENT comparison, not "Resolve returned
// Cached", because only the bytes prove the snapshot survived.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.Core.Twin;
using Xunit;

namespace StingTools.Boq.Tests
{
    public class NiagaraLiveReadTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _snapshot;

        public NiagaraLiveReadTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sting-niagara-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _snapshot = Path.Combine(_dir, "last_station_points.json");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private static NiagaraConnection ConnTo(LoopbackServer server) => new NiagaraConnection
        {
            BaseUrl = server.BaseUrl,
            PointsPath = "/obix/points",
            ApiKey = "test-key",
        };

        /// <summary>A real, non-empty snapshot on disk — the fallback an outage depends on.</summary>
        private void WriteGoodSnapshot()
        {
            using var server = LoopbackServer.Always(200,
                "[{\"id\":\"AHU-1\",\"status\":\"ok\",\"present_value\":21.4}," +
                "{\"id\":\"FCU-2\",\"status\":\"ok\",\"present_value\":19.1}]");
            var snap = CommissioningSource.Resolve(_snapshot, ConnTo(server));

            Assert.Equal(CommissioningSourceKind.Live, snap.Source);
            Assert.Equal(2, snap.Points.Count);                       // assert on non-empty data
            Assert.True(File.Exists(_snapshot), "the good read must have written a snapshot");
        }

        // ── The three failure bodies. None may end as Live. ────────────────────

        [Theory]
        [InlineData("not json at all {{{", "unparseable")]
        [InlineData("{\"error\":\"unauthorized\"}", "JSON error envelope")]
        [InlineData("<html><body>502 Bad Gateway</body></html>", "HTML error page")]
        [InlineData("\"just a string\"", "bare scalar")]
        [InlineData("[{\"deviceRef\":\"AHU-1\",\"present_value\":21.4}]", "entries present, none understood")]
        public void AFailedLiveRead_FallsBackToCache_AndIsNeverLive(string body, string why)
        {
            WriteGoodSnapshot();
            string before = File.ReadAllText(_snapshot);

            using var server = LoopbackServer.Always(200, body);
            var snap = CommissioningSource.Resolve(_snapshot, ConnTo(server));

            Assert.NotEqual(CommissioningSourceKind.Live, snap.Source);
            Assert.Equal(CommissioningSourceKind.Cached, snap.Source);
            Assert.Equal(2, snap.Points.Count);                       // the CACHED points, not zero
            Assert.Contains("CACHED", snap.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("live (captured", snap.Detail, StringComparison.Ordinal);

            // The assertion that catches the data loss: the file itself, byte for byte.
            Assert.Equal(before, File.ReadAllText(_snapshot));
        }

        [Theory]
        [InlineData("not json at all {{{")]
        [InlineData("{\"error\":\"unauthorized\"}")]
        [InlineData("<html><body>502 Bad Gateway</body></html>")]
        [InlineData("\"just a string\"")]
        public void AFailedLiveRead_WithNoCache_IsNone_NotLive(string body)
        {
            Assert.False(File.Exists(_snapshot));

            using var server = LoopbackServer.Always(200, body);
            var snap = CommissioningSource.Resolve(_snapshot, ConnTo(server));

            Assert.Equal(CommissioningSourceKind.None, snap.Source);
            Assert.Empty(snap.Points);
            Assert.Equal("no BMS data", snap.Detail);
            // A failed read must not create a snapshot either — an empty one would look
            // like a fallback and LoadCache would reject it, so it is worse than none.
            Assert.False(File.Exists(_snapshot),
                "a failed read must not write a snapshot: an empty one is a file, not a fallback");
        }

        [Fact]
        public void TheJsonErrorEnvelope_SetsNoParserError_AndIsStillRejected()
        {
            // Named specifically because this is the input that used to slip through with
            // nothing anywhere reporting a problem: it parses, and Error stays null.
            var r = NiagaraPointParser.Parse("{\"error\":\"unauthorized\"}");

            Assert.Null(r.Error);            // still not a parse ERROR - that is correct
            Assert.False(r.Failed);
            Assert.False(r.RecognisedShape); // ...but it is not a points feed
            Assert.True(r.Unusable);
            Assert.Empty(r.Points);

            Assert.Null(NiagaraJsonClient.ParsePoints("{\"error\":\"unauthorized\"}"));
        }

        [Fact]
        public void HttpErrorFromTheStation_FallsBackToCache_AndKeepsTheSnapshot()
        {
            WriteGoodSnapshot();
            string before = File.ReadAllText(_snapshot);

            using var server = LoopbackServer.Always(401, "{\"error\":\"unauthorized\"}");
            var snap = CommissioningSource.Resolve(_snapshot, ConnTo(server));

            Assert.Equal(CommissioningSourceKind.Cached, snap.Source);
            Assert.Equal(before, File.ReadAllText(_snapshot));
        }

        [Fact]
        public void StationUnreachable_FallsBackToCache_AndKeepsTheSnapshot()
        {
            WriteGoodSnapshot();
            string before = File.ReadAllText(_snapshot);

            NiagaraConnection dead;
            using (var probe = LoopbackServer.Always(200, "[]")) { dead = ConnTo(probe); }
            // probe disposed -> nothing is listening on that port

            var snap = CommissioningSource.Resolve(_snapshot, dead);

            Assert.Equal(CommissioningSourceKind.Cached, snap.Source);
            Assert.Equal(before, File.ReadAllText(_snapshot));
        }

        // ── The legitimate cases. Over-correcting these would be the worse bug. ─

        [Fact]
        public void AValidFeedWithZeroPoints_IsStillLive()
        {
            // A station with nothing commissioned yet is a real, expected state in early
            // Stage 3. Turning it into an error would make a clean station unreportable.
            using var server = LoopbackServer.Always(200, "{\"points\":[]}");
            var snap = CommissioningSource.Resolve(_snapshot, ConnTo(server));

            Assert.Equal(CommissioningSourceKind.Live, snap.Source);
            Assert.Empty(snap.Points);
            Assert.Contains("live (captured", snap.Detail, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("{\"points\":[]}")]
        [InlineData("{}")]
        public void EmptyFeedShapes_AreRecognised_NotRejected(string body)
        {
            var r = NiagaraPointParser.Parse(body);
            Assert.True(r.RecognisedShape, $"'{body}' is an empty station, not an unrecognised body");
            Assert.False(r.Unusable);
            Assert.Equal(0, r.CandidateEntries);
            Assert.NotNull(NiagaraJsonClient.ParsePoints(body));
        }

        [Fact]
        public void AZeroPointLiveRead_DoesNotDestroyAGoodSnapshot()
        {
            // Live and empty is legitimate; wiping the fallback on the way past is not.
            // LoadCache rejects a zero-point snapshot, so writing one does not update the
            // cache — it removes it.
            WriteGoodSnapshot();
            string before = File.ReadAllText(_snapshot);

            using (var server = LoopbackServer.Always(200, "{\"points\":[]}"))
            {
                var snap = CommissioningSource.Resolve(_snapshot, ConnTo(server));
                Assert.Equal(CommissioningSourceKind.Live, snap.Source);   // still reported honestly
                Assert.Empty(snap.Points);
            }

            Assert.Equal(before, File.ReadAllText(_snapshot));

            // And the fallback still works on the next outage.
            NiagaraConnection dead;
            using (var probe = LoopbackServer.Always(200, "[]")) { dead = ConnTo(probe); }
            var after = CommissioningSource.Resolve(_snapshot, dead);
            Assert.Equal(CommissioningSourceKind.Cached, after.Source);
            Assert.Equal(2, after.Points.Count);
        }

        [Fact]
        public void AGoodLiveRead_UpdatesTheSnapshot()
        {
            // The mirror of the guard: a real read with real points must still persist,
            // or "never overwrite" would have quietly frozen the cache forever.
            WriteGoodSnapshot();
            string before = File.ReadAllText(_snapshot);

            using var server = LoopbackServer.Always(200,
                "[{\"id\":\"AHU-1\",\"status\":\"ok\",\"present_value\":22.0}," +
                "{\"id\":\"FCU-2\",\"status\":\"ok\",\"present_value\":19.1}," +
                "{\"id\":\"VAV-3\",\"status\":\"ok\",\"present_value\":18.0}]");
            var snap = CommissioningSource.Resolve(_snapshot, ConnTo(server));

            Assert.Equal(CommissioningSourceKind.Live, snap.Source);
            Assert.Equal(3, snap.Points.Count);
            Assert.NotEqual(before, File.ReadAllText(_snapshot));
            Assert.Contains("VAV-3", File.ReadAllText(_snapshot), StringComparison.Ordinal);
        }

        [Fact]
        public void AGoodFeed_StillParsesAndReportsItsPoints()
        {
            // So "reject everything" would not pass this suite either.
            var pts = NiagaraJsonClient.ParsePoints(
                "[{\"id\":\"AHU-1\",\"status\":\"ok\",\"present_value\":21.4}," +
                "{\"id\":\"DEAD-2\",\"status\":\"ok\"}]");

            Assert.NotNull(pts);
            Assert.Equal(2, pts.Count);
            Assert.True(pts["AHU-1"].HasValue);
            Assert.False(pts["DEAD-2"].HasValue);   // configured but not reporting
        }
    }
}
