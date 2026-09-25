using System;
using System.Text;
using Lorestead.Core.Vault;
using Xunit;

namespace Lorestead.UnitTests
{
    public sealed class Argon2idKdfTests
    {
        private static readonly IPasswordKdf Kdf = new Argon2idKdf();
        private static readonly byte[] Password = Encoding.UTF8.GetBytes("correct horse battery staple");
        private static readonly byte[] Salt = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        // Computed with the reference implementation and cross-checked against independent ones.
        private const string ExpectedP4 = "6EC690471257037EE9C75B275E6161C1C2F4335AB541400534DBA6769A444397";
        private const string ExpectedP1 = "D8EBEB50632CC1993711EDA85DB155A107F0FB1CAFAB88A3A79F3D4661AC7A0C";

        [Fact]
        public void VaultDefaultParametersMatchTheReference()
        {
            KdfParameters parameters = KdfParameters.VaultDefault();
            Assert.Equal(4, parameters.Parallelism);

            byte[] key = Kdf.Derive(Password, Salt, parameters, 32);
            Assert.Equal(ExpectedP4, Convert.ToHexString(key));
        }

        [Fact]
        public void SingleLaneMatchesTheReference()
        {
            KdfParameters parameters = new KdfParameters { MemoryKiB = 64 * 1024, Iterations = 3, Parallelism = 1 };
            byte[] key = Kdf.Derive(Password, Salt, parameters, 32);
            Assert.Equal(ExpectedP1, Convert.ToHexString(key));
        }

        [Fact]
        public void DerivationIsDeterministic()
        {
            KdfParameters parameters = Small();
            Assert.Equal(Kdf.Derive(Password, Salt, parameters, 32), Kdf.Derive(Password, Salt, parameters, 32));
        }

        [Fact]
        public void SaltPasswordAndParametersAllChangeTheKey()
        {
            KdfParameters parameters = Small();
            byte[] baseline = Kdf.Derive(Password, Salt, parameters, 32);

            byte[] otherSalt = (byte[])Salt.Clone();
            otherSalt[0] ^= 1;
            Assert.NotEqual(baseline, Kdf.Derive(Password, otherSalt, parameters, 32));

            Assert.NotEqual(baseline, Kdf.Derive(Encoding.UTF8.GetBytes("Correct horse battery staple"), Salt, parameters, 32));

            KdfParameters morePasses = new KdfParameters { MemoryKiB = parameters.MemoryKiB, Iterations = parameters.Iterations + 1, Parallelism = parameters.Parallelism };
            Assert.NotEqual(baseline, Kdf.Derive(Password, Salt, morePasses, 32));
        }

        [Fact]
        public void OutputLengthIsHonored()
        {
            Assert.Equal(16, Kdf.Derive(Password, Salt, Small(), 16).Length);
            Assert.Equal(64, Kdf.Derive(Password, Salt, Small(), 64).Length);
        }

        [Fact]
        public void EmptyPasswordIsAllowed()
        {
            byte[] key = Kdf.Derive(ReadOnlySpan<byte>.Empty, Salt, Small(), 32);
            Assert.Equal(32, key.Length);
        }

        [Fact]
        public void InvalidParametersAreRejectedBeforeCallingNative()
        {
            Assert.Throws<ArgumentNullException>(() => Kdf.Derive(Password, Salt, null, 32));
            Assert.Throws<ArgumentOutOfRangeException>(() => Kdf.Derive(Password, Salt, new KdfParameters { MemoryKiB = 64, Iterations = 0, Parallelism = 1 }, 32));
            Assert.Throws<ArgumentOutOfRangeException>(() => Kdf.Derive(Password, Salt, new KdfParameters { MemoryKiB = 64, Iterations = 1, Parallelism = 0 }, 32));
            Assert.Throws<ArgumentOutOfRangeException>(() => Kdf.Derive(Password, Salt, new KdfParameters { MemoryKiB = 8, Iterations = 1, Parallelism = 4 }, 32));
            Assert.Throws<ArgumentException>(() => Kdf.Derive(Password, new byte[4], Small(), 32));
            Assert.Throws<ArgumentOutOfRangeException>(() => Kdf.Derive(Password, Salt, Small(), 2));
        }

        private static KdfParameters Small()
        {
            return new KdfParameters { MemoryKiB = 256, Iterations = 2, Parallelism = 2 };
        }
    }
}
