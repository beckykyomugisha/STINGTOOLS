// A5 — the bundle record that makes a non-interactive upload a file CHOICE, not a guess.
// A6 — an upload failure that names which KIND of failure it was.
//
// PR #927 wired ACC_UploadModel behind a file picker and refused to put it in the
// fortnightly Coordination Cycle, because a workflow step cannot answer "upload which
// file?" and guessing would put an unintended file into an issued CDE container. That
// question is answerable now: ACCPublish records the ZIP it built. The rule that keeps it
// a choice rather than a guess is that a record naming a file which is NO LONGER THERE is
// not a bundle — and that rule is what the first half of this file pins.
//
// The second half pins A6: before it, UploadResult carried only bool Ok and a string, so a
// 403 from Autodesk, a wrong folder and a network drop were one undifferentiated failure.

using System;
using System.IO;
using System.Threading.Tasks;
using StingTools.Acc.Tests.TestHelpers;
using StingTools.V6;
using Xunit;

namespace StingTools.Acc.Tests
{
    public class AccBundleRecordTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _record;
        private readonly string _zip;

        public AccBundleRecordTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sting-accbundle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _record = Path.Combine(_dir, AccBundleRecord.FileName);
            _zip = Path.Combine(_dir, "KUT_ACC_Publish_S3.zip");
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private void WriteZip(int bytes = 2048) => File.WriteAllBytes(_zip, new byte[bytes]);

        [Fact]
        public void RoundTrips_TheBundleItRecorded()
        {
            WriteZip();
            var rec = AccBundleRecord.ForFile(_zip, "S3", deliverableCount: 7);
            Assert.NotNull(rec);
            Assert.True(AccBundleRecord.TryWrite(_record, rec, out string err), err);

            var back = AccBundleRecord.ReadExisting(_record);
            Assert.NotNull(back);
            Assert.Equal(_zip, back.Path);
            Assert.Equal("S3", back.Suitability);
            Assert.Equal(7, back.DeliverableCount);
            Assert.Equal(2048, back.SizeBytes);
            Assert.Contains("KUT_ACC_Publish_S3.zip", back.Describe(), StringComparison.Ordinal);
            Assert.Contains("7 deliverable", back.Describe(), StringComparison.Ordinal);
        }

        [Fact]
        public void ReadExisting_RefusesARecordWhoseFileIsGone()
        {
            // THE rule. A record pointing at a deleted ZIP is not a file to upload:
            // acting on it either fails at the last moment or matches whatever has since
            // taken that path. Read() still returns the record so the caller can say WHY.
            WriteZip();
            var rec = AccBundleRecord.ForFile(_zip, "S3", 7);
            Assert.True(AccBundleRecord.TryWrite(_record, rec, out _));

            File.Delete(_zip);

            Assert.Null(AccBundleRecord.ReadExisting(_record));
            Assert.NotNull(AccBundleRecord.Read(_record));
            Assert.Equal(_zip, AccBundleRecord.Read(_record).Path);
        }

        [Fact]
        public void NoRecord_IsNull_NotAnEmptyRecord()
        {
            Assert.Null(AccBundleRecord.Read(_record));
            Assert.Null(AccBundleRecord.ReadExisting(_record));
            Assert.Null(AccBundleRecord.Read(null));
            Assert.Null(AccBundleRecord.ReadExisting(null));
        }

        [Fact]
        public void ForFile_RefusesAZipThatWasNeverWritten()
        {
            // There is no such thing as a record of a file that does not exist. Writing one
            // would create a "bundle" that the uploader would then refuse — a confusing
            // report of a problem that started here.
            Assert.Null(AccBundleRecord.ForFile(_zip, "S3", 7));
            Assert.Null(AccBundleRecord.ForFile(null, "S3", 7));
            Assert.Null(AccBundleRecord.ForFile("", "S3", 7));
        }

        [Fact]
        public void AGarbageRecordFile_ReadsAsNoRecord_NotAsAThrow()
        {
            File.WriteAllText(_record, "not json at all {{{");
            Assert.Null(AccBundleRecord.Read(_record));
            Assert.Null(AccBundleRecord.ReadExisting(_record));
        }

        [Fact]
        public void ARecordWithNoPath_IsNoRecord()
        {
            File.WriteAllText(_record, "{\"suitability\":\"S3\",\"deliverableCount\":3}");
            Assert.Null(AccBundleRecord.Read(_record));
        }
    }

    public class AccModelUploadTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _file;

        public AccModelUploadTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sting-accupload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _file = Path.Combine(_dir, "bundle.zip");
            File.WriteAllBytes(_file, new byte[64]);
        }

        public void Dispose()
        {
            AccModelUpload.OverrideHostForTests(null);
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static AccCredentials FreshCreds() => new AccCredentials
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            RefreshToken = "test-refresh",
            ProjectId = "b.11111111-2222-3333-4444-555555555555",
            AccessToken = "test-access-token",
            AccessTokenExpiry = DateTime.UtcNow.AddHours(1),
            // Set, so the run goes straight to storage creation rather than resolving folders.
            FolderUrn = "urn:adsk.wipprod:fs.folder:co.TESTFOLDER",
        };

        [Fact]
        public async Task Http403OnStorageCreation_IsAnAuthKindFailure_NotABareNotOk()
        {
            using var server = LoopbackServer.Always(403, "{\"detail\":\"forbidden\"}");
            AccModelUpload.OverrideHostForTests(server.BaseUrl);

            var result = await AccModelUpload.UploadAsync(FreshCreds(), _file);

            Assert.False(result.Ok);
            Assert.Equal(AccFetchStatus.AuthFailed, result.Status);      // the point of A6
            Assert.Equal(403, result.HttpStatus);
            Assert.Contains("403", result.Message, StringComparison.Ordinal);
            // And the remedy is the one every other ACC path gives for an auth failure.
            Assert.Equal(AccCommandOutcome.Remedy(AccFetchStatus.AuthFailed), result.Remedy);
            Assert.Contains("Sign in", result.Remedy, StringComparison.OrdinalIgnoreCase);
            Assert.True(server.RequestCount >= 1, "the client must actually have made the request");
        }

        [Fact]
        public async Task Http404OnStorageCreation_IsNotFoundKind()
        {
            using var server = LoopbackServer.Always(404, "{\"detail\":\"no such project\"}");
            AccModelUpload.OverrideHostForTests(server.BaseUrl);

            var result = await AccModelUpload.UploadAsync(FreshCreds(), _file);

            Assert.False(result.Ok);
            Assert.Equal(AccFetchStatus.NotFound, result.Status);
            Assert.Equal(404, result.HttpStatus);
            Assert.NotEqual(AccFetchStatus.AuthFailed, result.Status);   // kinds are distinguished
        }

        [Fact]
        public async Task Http500OnStorageCreation_IsTransportKind()
        {
            using var server = LoopbackServer.Always(500, "{\"detail\":\"boom\"}");
            AccModelUpload.OverrideHostForTests(server.BaseUrl);

            var result = await AccModelUpload.UploadAsync(FreshCreds(), _file);

            Assert.False(result.Ok);
            Assert.Equal(AccFetchStatus.TransportFailed, result.Status);
            Assert.Equal(500, result.HttpStatus);
        }

        [Fact]
        public async Task LocalFailures_AreAttributedWithNoHttpStatus()
        {
            // No HTTP exchange happened, so claiming one would be a fabricated attribution.
            var noFile = await AccModelUpload.UploadAsync(FreshCreds(), Path.Combine(_dir, "missing.zip"));
            Assert.False(noFile.Ok);
            Assert.Equal(0, noFile.HttpStatus);
            Assert.Equal(AccFetchStatus.TransportFailed, noFile.Status);

            var noCreds = await AccModelUpload.UploadAsync(null, _file);
            Assert.False(noCreds.Ok);
            Assert.Equal(0, noCreds.HttpStatus);
        }

        [Fact]
        public async Task NotAuthenticated_IsAuthFailed()
        {
            // A stale token whose refresh the listener rejects.
            using var server = LoopbackServer.Always(401, "{\"error\":\"invalid_grant\"}");
            AccModelUpload.OverrideHostForTests(server.BaseUrl);
            AccIssueSync.OverrideHostForTests(server.BaseUrl);
            try
            {
                var creds = FreshCreds();
                creds.AccessToken = "";
                creds.AccessTokenExpiry = DateTime.UtcNow.AddHours(-1);

                var result = await AccModelUpload.UploadAsync(creds, _file);

                Assert.False(result.Ok);
                Assert.Equal(AccFetchStatus.AuthFailed, result.Status);
                Assert.Contains("Sign in", result.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally { AccIssueSync.OverrideHostForTests(null); }
        }

        [Fact]
        public void ASuccessfulResult_CarriesOkStatus_AndNoRemedy()
        {
            var ok = new AccModelUpload.UploadResult { Ok = true, Message = "Uploaded.", ItemUrn = "urn:x" };
            Assert.Equal(AccFetchStatus.Ok, ok.Status);
            Assert.Equal("", ok.Remedy);
        }
    }
}
