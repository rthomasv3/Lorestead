using System;
using System.Security.Cryptography;
using System.Text;
using Lorestead.Core.Vault;
using Xunit;

namespace Lorestead.UnitTests
{
    public sealed class VaultCipherTests
    {
        private static readonly byte[] Key = VaultKeys.GenerateVaultKey();
        private static readonly byte[] Body = Encoding.UTF8.GetBytes("# Secret\n\nThe body of a vault note.");
        private static readonly byte[] BodyBinding = VaultBinding.ForField("item-1", "body", 1);

        [Fact]
        public void RoundTrips()
        {
            byte[] stored = VaultCipher.Encrypt(Key, Body, BodyBinding);
            byte[] plaintext = VaultCipher.Decrypt(Key, stored, BodyBinding);
            Assert.Equal(Body, plaintext);
        }

        [Fact]
        public void LayoutIsVersionNonceCiphertextTag()
        {
            byte[] stored = VaultCipher.Encrypt(Key, Body, BodyBinding);
            Assert.Equal(1 + VaultCipher.NonceLength + Body.Length + VaultCipher.TagLength, stored.Length);
            Assert.Equal(VaultCipher.FormatVersion, stored[0]);
        }

        [Fact]
        public void EmptyPlaintextRoundTrips()
        {
            byte[] stored = VaultCipher.Encrypt(Key, Array.Empty<byte>(), BodyBinding);
            Assert.Equal(1 + VaultCipher.NonceLength + VaultCipher.TagLength, stored.Length);
            Assert.Empty(VaultCipher.Decrypt(Key, stored, BodyBinding));
        }

        [Fact]
        public void FreshNoncePerEncryption()
        {
            byte[] first = VaultCipher.Encrypt(Key, Body, BodyBinding);
            byte[] second = VaultCipher.Encrypt(Key, Body, BodyBinding);
            Assert.NotEqual(first.AsSpan(1, VaultCipher.NonceLength).ToArray(), second.AsSpan(1, VaultCipher.NonceLength).ToArray());
            Assert.NotEqual(first, second);
        }

        [Fact]
        public void TransplantedCiphertextFailsAuthentication()
        {
            byte[] stored = VaultCipher.Encrypt(Key, Body, BodyBinding);
            byte[] otherItem = VaultBinding.ForField("item-2", "body", 1);
            Assert.ThrowsAny<CryptographicException>(() => VaultCipher.Decrypt(Key, stored, otherItem));
        }

        [Fact]
        public void CiphertextMovedToAnotherFieldFails()
        {
            byte[] stored = VaultCipher.Encrypt(Key, Body, BodyBinding);
            byte[] titleField = VaultBinding.ForField("item-1", "title", 1);
            Assert.ThrowsAny<CryptographicException>(() => VaultCipher.Decrypt(Key, stored, titleField));
        }

        [Fact]
        public void CiphertextUnderOtherKeyVersionFails()
        {
            byte[] stored = VaultCipher.Encrypt(Key, Body, BodyBinding);
            byte[] nextVersion = VaultBinding.ForField("item-1", "body", 2);
            Assert.ThrowsAny<CryptographicException>(() => VaultCipher.Decrypt(Key, stored, nextVersion));
        }

        [Fact]
        public void WrongKeyFails()
        {
            byte[] stored = VaultCipher.Encrypt(Key, Body, BodyBinding);
            Assert.ThrowsAny<CryptographicException>(() => VaultCipher.Decrypt(VaultKeys.GenerateVaultKey(), stored, BodyBinding));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(20)]
        [InlineData(-1)]
        public void AnyModifiedByteFails(int offset)
        {
            byte[] stored = VaultCipher.Encrypt(Key, Body, BodyBinding);
            int index = offset < 0 ? stored.Length + offset : offset;
            stored[index] ^= 0x01;
            Assert.ThrowsAny<CryptographicException>(() => VaultCipher.Decrypt(Key, stored, BodyBinding));
        }

        [Fact]
        public void TruncatedStoredBytesFail()
        {
            byte[] stored = VaultCipher.Encrypt(Key, Body, BodyBinding);
            byte[] truncated = stored.AsSpan(0, stored.Length - 1).ToArray();
            Assert.ThrowsAny<CryptographicException>(() => VaultCipher.Decrypt(Key, truncated, BodyBinding));
            Assert.ThrowsAny<CryptographicException>(() => VaultCipher.Decrypt(Key, new byte[5], BodyBinding));
        }

        [Fact]
        public void WrongKeyLengthIsRejectedBeforeAnyCrypto()
        {
            Assert.Throws<ArgumentException>(() => VaultCipher.Encrypt(new byte[16], Body, BodyBinding));
            Assert.Throws<ArgumentException>(() => VaultCipher.Decrypt(new byte[31], new byte[64], BodyBinding));
        }

        [Fact]
        public void BindingsForDifferentPartsNeverCollide()
        {
            Assert.NotEqual(VaultBinding.ForField("ab", "c", 1), VaultBinding.ForField("a", "bc", 1));
            Assert.NotEqual(VaultBinding.ForField("x", "y", 1), VaultBinding.ForKeyRow("x", "y", 1));
            Assert.NotEqual(VaultBinding.ForKeyRow("v", "k", 0), VaultBinding.ForKeyRow("v", "k", 1));
        }
    }
}
