using Lorestead.Core.Notes;
using Xunit;

namespace Lorestead.UnitTests
{
    public sealed class NoteTitleTests
    {
        [Fact]
        public void PunctuationIsContentAndSurvives()
        {
            Assert.Equal("Q3: Roadmap", NoteTitle.Normalize("Q3: Roadmap"));
            Assert.Equal("Auth / Session flow", NoteTitle.Normalize("Auth / Session flow"));
            Assert.Equal("What's next?", NoteTitle.Normalize("What's next?"));
        }

        [Fact]
        public void WhitespaceAndControlCharactersAreNot()
        {
            Assert.Equal("Meeting notes", NoteTitle.Normalize("  Meeting notes  "));
            Assert.Equal("Meeting notes", NoteTitle.Normalize("Meeting\n\tnotes"));
            Assert.Equal("Meeting notes", NoteTitle.Normalize("Meeting    notes"));
            Assert.Equal("Meeting notes", NoteTitle.Normalize("Meeting" + (char)0x00A0 + "notes"));
            Assert.Equal(string.Empty, NoteTitle.Normalize(null));
            Assert.Equal(string.Empty, NoteTitle.Normalize("   "));
        }
    }
}
