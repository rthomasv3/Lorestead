using System;
using System.Collections.Generic;
using Lorestead.Core.Ordering;
using Xunit;

namespace Lorestead.UnitTests
{
    public sealed class FractionalIndexTests
    {
        [Fact]
        public void FirstKeyIsValid()
        {
            string key = FractionalIndex.Between(null, null);
            Assert.False(string.IsNullOrEmpty(key));
            Assert.False(key.EndsWith("0", StringComparison.Ordinal));
        }

        [Fact]
        public void AfterIsGreater()
        {
            string a = FractionalIndex.Between(null, null);
            string b = FractionalIndex.Between(a, null);
            Assert.True(string.CompareOrdinal(a, b) < 0);
        }

        [Fact]
        public void BeforeIsSmaller()
        {
            string b = FractionalIndex.Between(null, null);
            string a = FractionalIndex.Between(null, b);
            Assert.True(string.CompareOrdinal(a, b) < 0);
        }

        [Fact]
        public void BetweenIsStrictlyBetween()
        {
            string a = "V";
            string b = "W";
            string mid = FractionalIndex.Between(a, b);
            Assert.True(string.CompareOrdinal(a, mid) < 0);
            Assert.True(string.CompareOrdinal(mid, b) < 0);
        }

        [Fact]
        public void AppendingManyKeysStaysOrderedAndShort()
        {
            List<string> keys = new List<string>();
            string last = null;
            for (int i = 0; i < 500; i++)
            {
                last = FractionalIndex.Between(last, null);
                keys.Add(last);
            }
            AssertStrictlyOrdered(keys);
            Assert.True(keys[keys.Count - 1].Length <= 12);
        }

        [Fact]
        public void PrependingManyKeysStaysOrdered()
        {
            List<string> keys = new List<string>();
            string first = null;
            for (int i = 0; i < 200; i++)
            {
                first = FractionalIndex.Between(null, first);
                keys.Insert(0, first);
            }
            AssertStrictlyOrdered(keys);
        }

        [Fact]
        public void DenseInsertionBetweenNeighborsStaysOrdered()
        {
            string low = "V";
            string high = "W";
            List<string> keys = new List<string> { low, high };
            string a = low;
            string b = high;
            for (int i = 0; i < 200; i++)
            {
                string mid = FractionalIndex.Between(a, b);
                keys.Insert(keys.IndexOf(b), mid);
                if (i % 2 == 0)
                {
                    a = mid;
                }
                else
                {
                    b = mid;
                }
            }
            AssertStrictlyOrdered(keys);
        }

        [Fact]
        public void RandomInsertionsStayOrdered()
        {
            Random random = new Random(42);
            List<string> keys = new List<string> { FractionalIndex.Between(null, null) };
            for (int i = 0; i < 500; i++)
            {
                int slot = random.Next(keys.Count + 1);
                string lower = slot > 0 ? keys[slot - 1] : null;
                string upper = slot < keys.Count ? keys[slot] : null;
                keys.Insert(slot, FractionalIndex.Between(lower, upper));
            }
            AssertStrictlyOrdered(keys);
        }

        [Fact]
        public void RejectsInvalidInputs()
        {
            Assert.Throws<ArgumentException>(() => FractionalIndex.Between("", null));
            Assert.Throws<ArgumentException>(() => FractionalIndex.Between("V0", null));
            Assert.Throws<ArgumentException>(() => FractionalIndex.Between("V!", null));
            Assert.Throws<ArgumentException>(() => FractionalIndex.Between("W", "V"));
            Assert.Throws<ArgumentException>(() => FractionalIndex.Between("V", "V"));
        }

        private static void AssertStrictlyOrdered(List<string> keys)
        {
            for (int i = 1; i < keys.Count; i++)
            {
                Assert.True(
                    string.CompareOrdinal(keys[i - 1], keys[i]) < 0,
                    $"'{keys[i - 1]}' should sort before '{keys[i]}' (index {i}).");
            }
        }
    }
}
