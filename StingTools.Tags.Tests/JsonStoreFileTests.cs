using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    // C4: an unreadable store read as empty and was then overwritten; the .bak the loader fell
    // back to was never written; a failed save was swallowed.
    public class JsonStoreFileTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-store-" + Guid.NewGuid().ToString("N"));
        private string P => Path.Combine(_dir, "document_register.json");
        public JsonStoreFileTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }

        [Fact]
        public void AMissingStore_IsEmpty_AndThatIsFine()
        {
            Assert.True(JsonStoreFile.TryLoadArray(P, out var a, out var err, out var note));
            Assert.Empty(a); Assert.Null(err); Assert.Null(note);
        }

        [Fact]
        public void AnUnreadableStore_WithNoBackup_IsRefused_NotEmpty()
        {
            File.WriteAllText(P, "[{\"doc_id\":\"DOC-0001\"},");
            Assert.False(JsonStoreFile.TryLoadArray(P, out var a, out var err, out _));
            Assert.Null(a);
            Assert.Contains("could not be read", err);
        }

        [Fact]
        public void AnObjectWhereAnArrayBelongs_IsRefused()
        {
            File.WriteAllText(P, "{\"$schemaVersion\":1}");
            Assert.False(JsonStoreFile.TryLoadArray(P, out _, out var err, out _));
            Assert.Contains("not an array", err);
        }

        [Fact]
        public void Replace_KeepsABackup_ThatAnUnreadableStoreThenFallsBackTo()
        {
            Assert.True(JsonStoreFile.Replace(P, "[{\"doc_id\":\"DOC-0001\"},{\"doc_id\":\"DOC-0002\"}]", out _));
            Assert.True(JsonStoreFile.Replace(P, "[{\"doc_id\":\"DOC-0001\"},{\"doc_id\":\"DOC-0002\"},{\"doc_id\":\"DOC-0003\"}]", out _));
            Assert.Equal(2, JArray.Parse(File.ReadAllText(P + ".bak")).Count);

            File.WriteAllText(P, "[ truncated");
            Assert.True(JsonStoreFile.TryLoadArray(P, out var a, out _, out var note));
            Assert.Equal(2, a.Count);
            Assert.Contains("backup", note);
        }

        [Fact]
        public void Replace_NeverOverwritesAnUnreadableStore_ItMovesItAside()
        {
            const string broken = "[{\"doc_id\":\"DOC-0001\",\"title\":\"hand-entered\"},";
            File.WriteAllText(P, broken);
            Assert.True(JsonStoreFile.Replace(P, "[{\"doc_id\":\"DOC-0009\"}]", out _));
            var aside = Directory.GetFiles(_dir, "document_register.json.unreadable.*");
            Assert.Single(aside);
            Assert.Equal(broken, File.ReadAllText(aside[0]));
            Assert.False(File.Exists(P + ".bak"));   // an unreadable file is not a backup
            Assert.Single(JArray.Parse(File.ReadAllText(P)));
        }

        [Fact]
        public void Replace_ReportsFailure_InsteadOfSwallowingIt()
        {
            // A directory where the file should be: the write cannot happen.
            Directory.CreateDirectory(P);
            Assert.False(JsonStoreFile.Replace(P, "[]", out var err));
            Assert.False(string.IsNullOrEmpty(err));
        }
    }
}
