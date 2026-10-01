using StingTools.Core.Drawing;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DTW-108: DrawingProducer's batch caches (including the STACK-1 sheet-claim table)
    /// are primed by the outermost scope and dropped only when that scope ends. A scope
    /// opened inside it on the same document (the Setup Wizard run from a batch) neither
    /// re-primes nor resets them. The plugin calls the same class.
    /// </summary>
    public class BatchScopeDepthTests
    {
        [Fact]
        public void The_outermost_scope_primes_and_resets()
        {
            var d = new BatchScopeDepth();
            Assert.True(d.Enter("doc"));
            Assert.True(d.Exit());
            Assert.Equal(0, d.Depth);
        }

        [Fact]
        public void A_nested_scope_on_the_same_document_neither_primes_nor_resets()
        {
            var d = new BatchScopeDepth();
            Assert.True(d.Enter("doc"));
            Assert.False(d.Enter("DOC"));   // document keys compare case-insensitively
            Assert.False(d.Exit());
            Assert.Equal(1, d.Depth);
            Assert.True(d.Exit());
        }

        [Fact]
        public void Three_levels_reset_only_at_the_outermost_exit()
        {
            var d = new BatchScopeDepth();
            d.Enter("doc"); d.Enter("doc"); d.Enter("doc");
            Assert.False(d.Exit());
            Assert.False(d.Exit());
            Assert.True(d.Exit());
        }

        [Fact]
        public void A_nested_scope_on_another_document_primes_but_does_not_reset()
        {
            var d = new BatchScopeDepth();
            Assert.True(d.Enter("a"));
            Assert.True(d.Enter("b"));
            Assert.False(d.Exit());
            Assert.True(d.Exit());
        }

        [Fact]
        public void A_reset_with_no_open_scope_still_resets()
        {
            var d = new BatchScopeDepth();
            Assert.True(d.Exit());
            Assert.Equal(0, d.Depth);
            Assert.True(d.Enter("doc"));   // and the next scope is outermost again
        }
    }
}
