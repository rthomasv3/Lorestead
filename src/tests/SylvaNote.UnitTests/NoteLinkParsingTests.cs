using System.Collections.Generic;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Sync;
using Xunit;

namespace SylvaNote.UnitTests
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
        public void TimestampsSortLexicographically()
        {
            string earlier = Timestamps.UtcNowIso();
            System.Threading.Thread.Sleep(5);
            string later = Timestamps.UtcNowIso();
            Assert.True(string.CompareOrdinal(earlier, later) < 0);
        }
    }
}
