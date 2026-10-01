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

        // ── DTW-102: linked MEP runs are stamped by link instance + linked element ──

        [Fact]
        public void A_linked_host_names_the_link_instance_and_the_linked_element()
        {
            var host = AnnotationProvenance.LinkedHost("li-0001", "pipe-0042");
            Assert.True(AnnotationProvenance.IsLinkedHost(host));
            Assert.True(AnnotationProvenance.TryParseLinkedHost(host, out var li, out var el));
            Assert.Equal("li-0001", li);
            Assert.Equal("pipe-0042", el);
            // It is a valid key host, and HostOf gives it back for the stamped-set lookup.
            Assert.Equal(host, AnnotationProvenance.HostOf(AnnotationProvenance.Key(host)));
        }

        [Fact]
        public void A_linked_host_is_distinct_from_the_host_element_and_from_another_link()
        {
            // The same element UniqueId in the host, through link instance A and through
            // link instance B (the model linked twice) are three different stamps.
            var a = AnnotationProvenance.LinkedHost("li-A", "pipe-0042");
            var b = AnnotationProvenance.LinkedHost("li-B", "pipe-0042");
            Assert.NotEqual(a, b);
            Assert.NotEqual("pipe-0042", a);
            Assert.False(AnnotationProvenance.IsLinkedHost("pipe-0042"));
            Assert.False(AnnotationProvenance.TryParseLinkedHost("pipe-0042", out _, out _));
        }

        [Fact]
        public void A_linked_host_refuses_blank_or_separator_parts()
        {
            Assert.Throws<ArgumentException>(() => AnnotationProvenance.LinkedHost("", "pipe"));
            Assert.Throws<ArgumentException>(() => AnnotationProvenance.LinkedHost("li", null));
            Assert.Throws<ArgumentException>(() => AnnotationProvenance.LinkedHost("li|x", "pipe"));
            Assert.Throws<ArgumentException>(() => AnnotationProvenance.LinkedHost("li", "a/b"));
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
