using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Planscape.API.Controllers;
using Planscape.API.Services;

namespace Planscape.Tests;

/// <summary>
/// Phase A4 — server half of the substrate drift-check. Proves
/// <see cref="SubstrateManifestProvider.ComputeFromFile"/> returns a 64-hex
/// SHA-256 + the manifest's schema/enum counts, that the hash is
/// newline-normalised (so a Windows host and a Linux server agree), and that
/// <see cref="SubstrateController"/> surfaces it.
/// </summary>
public class SubstrateManifestTests
{
    private const string SampleManifest =
        "{\n  \"schema_version\": 2,\n  \"total_enums\": 52,\n  \"enums\": []\n}\n";

    private static string WriteTemp(string content, string newline)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sting_substrate_{Guid.NewGuid():N}.json");
        var normalized = content.Replace("\r\n", "\n").Replace("\n", newline);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(normalized));
        return path;
    }

    [Fact]
    public void ComputeFromFile_ReturnsSixtyFourHexSha_AndCounts()
    {
        var path = WriteTemp(SampleManifest, "\n");
        try
        {
            var resp = SubstrateManifestProvider.ComputeFromFile(path);

            Assert.Equal(64, resp.Sha256.Length);
            Assert.Matches("^[0-9a-f]{64}$", resp.Sha256);
            Assert.Equal(2, resp.SchemaVersion);
            Assert.Equal(52, resp.TotalEnums);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ComputeFromFile_HashIsNewlineNormalized()
    {
        // CRLF (Windows checkout) and LF (Linux checkout) of identical content
        // must hash the same, or every host drifts against the server forever.
        var lf = WriteTemp(SampleManifest, "\n");
        var crlf = WriteTemp(SampleManifest, "\r\n");
        try
        {
            Assert.Equal(
                SubstrateManifestProvider.ComputeFromFile(lf).Sha256,
                SubstrateManifestProvider.ComputeFromFile(crlf).Sha256);
        }
        finally { File.Delete(lf); File.Delete(crlf); }
    }

    [Fact]
    public void ComputeFromFile_MatchesExpectedSha_ForKnownBytes()
    {
        var path = WriteTemp(SampleManifest, "\n");
        try
        {
            var expected = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(SampleManifest.Replace("\r\n", "\n"))))
                .ToLowerInvariant();
            Assert.Equal(expected, SubstrateManifestProvider.ComputeFromFile(path).Sha256);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Controller_ReturnsManifestFromProvider()
    {
        var stub = new StubProvider(new SubstrateManifestResponse
        {
            Sha256 = new string('a', 64),
            SchemaVersion = 2,
            TotalEnums = 52,
        });
        var controller = new SubstrateController(stub);

        var result = controller.GetManifest();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<SubstrateManifestResponse>(ok.Value);
        Assert.Equal(64, body.Sha256.Length);
        Assert.Matches("^[0-9a-f]{64}$", body.Sha256);
        Assert.Equal(52, body.TotalEnums);
    }

    private sealed class StubProvider : ISubstrateManifestProvider
    {
        private readonly SubstrateManifestResponse _r;
        public StubProvider(SubstrateManifestResponse r) => _r = r;
        public SubstrateManifestResponse Get() => _r;
    }
}
