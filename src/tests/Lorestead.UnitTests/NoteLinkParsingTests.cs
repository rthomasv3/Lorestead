using System.Collections.Generic;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Sync;
using Xunit;

namespace Lorestead.UnitTests
{
    public sealed class NoteLinkParsingTests
    {
        [Fact]
        public void ExtractsAndDedupesTargets()
        {
            string body =
                "See [one](note://0198C0DE-0000-7000-8000-000000000001) and " +
                "[two](note://0198c0de-0000-7000-8000-000000000002), plus " +
                "[one again](note://0198c0de-0000-7000-8000-000000000001).";
            IReadOnlyList<string> targets = NoteLinkRebuilder.ParseTargets(body);
            Assert.Equal(2, targets.Count);
            Assert.Contains("0198c0de-0000-7000-8000-000000000001", targets);
            Assert.Contains("0198c0de-0000-7000-8000-000000000002", targets);
        }

        [Fact]
        public void IgnoresNonLinksAndEmptyBodies()
        {
            Assert.Empty(NoteLinkRebuilder.ParseTargets(null));
            Assert.Empty(NoteLinkRebuilder.ParseTargets(""));
            Assert.Empty(NoteLinkRebuilder.ParseTargets("attachment://0198c0de-0000-7000-8000-000000000001"));
            Assert.Empty(NoteLinkRebuilder.ParseTargets("note://not-a-uuid"));
        }

        [Fact]
        public void SnippetCentresOnTheLinkAndShowsItsText()
        {
            string body =
                "Padding that runs well past the window on the left hand side of the link itself. " +
                "Then [the target](note://0198c0de-0000-7000-8000-000000000001) sits here. " +
                "And padding that runs well past the window on the right hand side too.";

            string snippet = NoteLinkRebuilder.ContextSnippet(body, "0198c0de-0000-7000-8000-000000000001", radius: 20);

            Assert.Contains("the target", snippet);
            Assert.DoesNotContain("note://", snippet);
            Assert.StartsWith("...", snippet);
            Assert.EndsWith("...", snippet);
        }

        [Fact]
        public void SnippetCollapsesWhitespaceAndKeepsShortBodiesWhole()
        {
            string body = "Line one\n\n   [target](note://0198c0de-0000-7000-8000-000000000001)   \nLine two";

            string snippet = NoteLinkRebuilder.ContextSnippet(body, "0198c0de-0000-7000-8000-000000000001");

            Assert.Equal("Line one target Line two", snippet);
        }

        [Fact]
        public void SnippetHandlesBareUrlsAndAbsentTargets()
        {
            Assert.Contains("note://0198c0de-0000-7000-8000-000000000001",
                NoteLinkRebuilder.ContextSnippet("A bare note://0198c0de-0000-7000-8000-000000000001 url",
                    "0198c0de-0000-7000-8000-000000000001"));
            Assert.Equal(string.Empty, NoteLinkRebuilder.ContextSnippet(null, "0198c0de-0000-7000-8000-000000000001"));
            Assert.Equal(string.Empty, NoteLinkRebuilder.ContextSnippet("   ", "0198c0de-0000-7000-8000-000000000001"));
        }

        [Fact]
        public void TimestampsSortLexicographically()
        {
            string earlier = Timestamps.UtcNowIso();
            System.Threading.Thread.Sleep(5);
            string later = Timestamps.UtcNowIso();
            Assert.True(string.CompareOrdinal(earlier, later) < 0);
        }
    }
}
