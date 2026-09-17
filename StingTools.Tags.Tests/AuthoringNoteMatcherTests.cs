// Tests for Core/AuthoringNoteMatcher.cs.
//
// The risk in this check is not missing a note — it is calling a real annotation
// an authoring instruction, because the family is then flagged on every audit
// forever and the flag stops meaning anything. So the negative cases carry more
// weight here than the positive ones.

using System.Linq;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class AuthoringNoteMatcherTests
    {
        [Theory]
        [InlineData("Delete this note before saving")]
        [InlineData("DELETE THIS NOTE")]
        [InlineData("Note: set the tag's category in Family Categories and Parameters. Delete this note before saving.")]
        [InlineData("TODO: wire the visibility parameter")]
        [InlineData("FIXME broken leader")]
        [InlineData("remove before saving")]
        [InlineData("Erase this text before issue")]
        [InlineData("placeholder text — replace with the real schedule")]
        public void SelfDeclaredTemporaryText_IsFlagged(string text)
        {
            Assert.True(AuthoringNoteMatcher.LooksLikeAuthoringInstruction(text), text);
        }

        [Theory]
        // Real annotation. Every one of these would be a false positive that
        // nags on every audit of a correct family.
        [InlineData("Note: refer to drawing A-101 for setting out")]
        [InlineData("NOTES")]
        [InlineData("Note: all dimensions in millimetres unless stated otherwise")]
        [InlineData("Delete unused levels from the project browser")]  // verb, no deadline
        [InlineData("Verify all dimensions before saving the record copy")] // deadline, no delete verb
        [InlineData("Remove debris from the duct before commissioning")]
        [InlineData("This tag reports ASS_TAG_1_TXT")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void OrdinaryAnnotation_IsNotFlagged(string text)
        {
            Assert.False(AuthoringNoteMatcher.LooksLikeAuthoringInstruction(text), text ?? "(null)");
        }

        [Fact]
        public void Flag_CollapsesMultilineNotesOntoOneLine()
        {
            var hits = AuthoringNoteMatcher.Flag(new[]
            {
                "Note:\r\n  Set the category first.\r\n  Delete this note before saving.",
            });

            string one = Assert.Single(hits);
            Assert.DoesNotContain("\n", one);
            Assert.DoesNotContain("\r", one);
            Assert.Contains("Set the category first.", one);
        }

        [Fact]
        public void Flag_CollapsesDuplicates()
        {
            // A family repeats the same note per view; three copies is one finding.
            var hits = AuthoringNoteMatcher.Flag(new[]
            {
                "Delete this note", "Delete this note", "Delete this note",
            });
            Assert.Single(hits);
        }

        [Fact]
        public void Flag_TruncatesLongNotesAndMarksThem()
        {
            string longNote = "TODO: " + new string('x', 400);
            string one = Assert.Single(AuthoringNoteMatcher.Flag(new[] { longNote }, maxChars: 40));
            Assert.Equal(40, one.Length);
            Assert.EndsWith("…", one);
        }

        [Fact]
        public void Flag_ReturnsNothingForCleanOrMissingInput()
        {
            Assert.Empty(AuthoringNoteMatcher.Flag(null));
            Assert.Empty(AuthoringNoteMatcher.Flag(new string[0]));
            Assert.Empty(AuthoringNoteMatcher.Flag(new[] { "Note: refer to drawing A-101", null, "" }));
        }
    }
}
