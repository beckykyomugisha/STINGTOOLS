using System.Collections.Generic;
using System.Linq;
using StingTools.Commands.Electrical.Export;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>ELEC-8 — ETAP hand-off wrote rdf:ID = circuit number, which repeats per panel.</summary>
    public class CimIdAllocatorTests
    {
        [Fact]
        public void SameCircuitNumberOnTwoPanelsGetsTwoIds()
        {
            var ids = new CimIdAllocator();
            string a = ids.For(null, "CCT", "DB-1", "1");
            string b = ids.For(null, "CCT", "DB-2", "1");
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void NamesThatSanitiseAlikeAreStillUnique()
        {
            var ids = new CimIdAllocator();
            var all = new List<string>
            {
                ids.For(null, "CCT", "DB 1", "1"),
                ids.For(null, "CCT", "DB/1", "1"),
                ids.For(null, "CCT", "DB:1", "1"),
            };
            Assert.Equal(all.Count, all.Distinct().Count());
        }

        [Fact]
        public void KeyedIdIsStableSoReferencesResolve()
        {
            var ids = new CimIdAllocator();
            string panel = ids.For("P|MDB", "PNL", "MDB");
            Assert.Equal(panel, ids.For("P|MDB", "PNL", "MDB"));
        }

        [Fact]
        public void IdsAreNcNameSafe()
        {
            var ids = new CimIdAllocator();
            string id = ids.For(null, "1st floor", "→ 2");
            Assert.StartsWith("_", id);
            Assert.Matches("^[A-Za-z_][A-Za-z0-9_.\\-]*$", id);
        }
    }
}
