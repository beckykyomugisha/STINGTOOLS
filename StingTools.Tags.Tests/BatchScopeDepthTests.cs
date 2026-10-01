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

        // DTW-142: a nested scope on another document used to re-prime over the outer
        // batch's caches (wiping its STACK-1 claims) and, on exit, leave the scope on the
        // inner document. The outer caches are now set aside and handed back.

        [Fact]
        public void A_nested_scope_on_another_document_sets_the_outer_caches_aside()
        {
            var d = new BatchScopeDepth();
            int captured = 0;
            Assert.True(d.Enter("a", () => { captured++; return "caches of a"; }));
            Assert.Equal(0, captured);                 // nothing to set aside at the outermost
            Assert.True(d.Enter("b", () => { captured++; return "caches of a"; }));
            Assert.Equal(1, captured);
            Assert.Equal("b", d.DocKey);
        }

        [Fact]
        public void Leaving_the_other_document_resets_its_caches_and_restores_the_outer()
        {
            var d = new BatchScopeDepth();
            d.Enter("a", () => "unused");
            d.Enter("b", () => "caches of a");
            Assert.True(d.Exit(out object outer));     // the inner document's caches go
            Assert.Equal("caches of a", outer);        // and the outer batch's come back
            Assert.Equal("a", d.DocKey);
            Assert.Equal(1, d.Depth);
            Assert.True(d.Exit(out object none));      // the outermost exit resets, nothing to restore
            Assert.Null(none);
            Assert.Null(d.DocKey);
        }

        [Fact]
        public void Same_document_nesting_inside_the_other_document_is_still_counted()
        {
            var d = new BatchScopeDepth();
            d.Enter("a", () => "x");
            d.Enter("b", () => "caches of a");
            Assert.False(d.Enter("B", () => "never"));  // same document as the top scope
            Assert.Equal(3, d.Depth);
            Assert.False(d.Exit(out object o1));
            Assert.Null(o1);
            Assert.True(d.Exit(out object o2));
            Assert.Equal("caches of a", o2);
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
