using System;
using System.Linq;
using System.Reflection;
using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// Provenance keys: what a stamped annotation is FOR. Dimensions, match-line
    /// captions and drainage IL notes used to be re-identified by references, text
    /// shape and proximity — a moved pipe or boundary stranded its old annotation,
    /// and unreadable references allowed duplicates. The stamp is only as good as
    /// the key, so the format is pinned here.
    /// </summary>
    public class AnnotationProvenanceTests
    {
        [Fact]
        public void Key_round_trips_host_and_part()
        {
            var k = AnnotationProvenance.Key("abc-123-0001", "US");
            Assert.Equal("abc-123-0001", AnnotationProvenance.HostOf(k));
            Assert.Equal("US", AnnotationProvenance.PartOf(k));
        }

        [Fact]
        public void Key_without_part_is_the_host()
        {
            var k = AnnotationProvenance.Key("abc-123-0001");
            Assert.Equal("abc-123-0001", AnnotationProvenance.HostOf(k));
            Assert.Null(AnnotationProvenance.PartOf(k));
        }

        [Fact]
        public void Match_line_segment_guids_with_colons_survive()
        {
            // viewPairGuid is "<scopePair>:<viewA>:<viewB>[:segN]" — colons must not split it.
            var host = "pair-1:viewA:viewB:seg2";
            Assert.Equal(host, AnnotationProvenance.HostOf(AnnotationProvenance.Key(host, "0")));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void A_key_needs_a_host(string host)
            => Assert.Throws<ArgumentException>(() => AnnotationProvenance.Key(host));

        [Fact]
        public void The_separator_cannot_be_smuggled_in()
        {
            Assert.Throws<ArgumentException>(() => AnnotationProvenance.Key("a|b"));
            Assert.Throws<ArgumentException>(() => AnnotationProvenance.Key("a", "U|S"));
        }

        [Fact]
        public void Producers_are_distinct()
        {
            // A stamp is matched only against its own producer; two producers sharing
            // a name would let one pass mistake the other's annotations for its own.
            var names = typeof(AnnotationProvenance)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue())
                .ToList();
            Assert.True(names.Count >= 7);
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        }
    }
}
