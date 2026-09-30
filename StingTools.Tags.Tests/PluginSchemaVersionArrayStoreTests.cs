using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    // C1: EnsureFileVersion quarantined JSON-ARRAY stores (transmittals.json, deliverables.json)
    // and replaced them with a version object, so every earlier row left the live file.
    public class PluginSchemaVersionArrayStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sting-schema-" + Guid.NewGuid().ToString("N"));
        public PluginSchemaVersionArrayStoreTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }

        private string Write(string name, string text)
        {
            string p = Path.Combine(_dir, name);
            File.WriteAllText(p, text);
            return p;
        }

        [Fact]
        public void AnArrayStore_IsLeftExactlyAsItIs()
        {
            const string rows = "[\n  {\"id\": \"TX-0001\"},\n  {\"id\": \"TX-0002\"}\n]";
            string p = Write("transmittals.json", rows);
            PluginSchemaVersion.EnsureFileVersion(p, "planscape.transmittals", PluginSchemaVersion.CurrentTransmittals);
            Assert.Equal(rows, File.ReadAllText(p));
            Assert.Equal(2, JArray.Parse(File.ReadAllText(p)).Count);
            Assert.Empty(Directory.GetFiles(_dir, "*.corrupt.*"));
        }

        [Fact]
        public void ATruncatedArrayStore_IsNotQuarantinedOrReplaced()
        {
            const string broken = "  [ {\"id\": \"TX-0001\"}, {\"id\": ";
            string p = Write("deliverables.json", broken);
            PluginSchemaVersion.EnsureFileVersion(p, "planscape.deliverables", PluginSchemaVersion.CurrentDeliverables);
            Assert.Equal(broken, File.ReadAllText(p));
            Assert.Empty(Directory.GetFiles(_dir, "*.corrupt.*"));
        }

        [Fact]
        public void AnObjectStore_IsStillVersioned()
        {
            // The pair: object stores keep the migration behaviour the gate exists for.
            string p = Write("manifest.json", "{\"a\": 1}");
            PluginSchemaVersion.EnsureFileVersion(p, "planscape.manifest", 1);
            var o = JObject.Parse(File.ReadAllText(p));
            Assert.Equal(1, (int)o["$schemaVersion"]);
            Assert.Equal(1, (int)o["a"]);
        }

        [Theory]
        [InlineData("[1]", true)]
        [InlineData("﻿  \n [", true)]
        [InlineData("{\"a\":[1]}", false)]
        [InlineData("   ", false)]
        [InlineData("", false)]
        public void LooksLikeArray(string text, bool expected)
            => Assert.Equal(expected, PluginSchemaVersion.LooksLikeArray(text));
    }
}
