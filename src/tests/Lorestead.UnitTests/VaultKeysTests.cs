using System;
using System.Security.Cryptography;
using Lorestead.Core.Crypto;
using Xunit;

namespace Lorestead.UnitTests
{
    public sealed class VaultKeysTests
    {
        /// <summary>
        /// Cheap stand-in for Argon2id so these tests exercise wrapping, not derivation.
        /// </summary>
        private sealed class FakeKdf : IPasswordKdf
        {
            public byte[] Derive(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, KdfParameters parameters, int length)
            {
                byte[] material = new byte[password.Length + salt.Length];
                password.CopyTo(material);
                salt.CopyTo(material.AsSpan(password.Length));
                return SHA256.HashData(material);
            }
        }

        private static readonly IPasswordKdf Kdf = new FakeKdf();
        private static readonly KdfParameters Parameters = KdfParameters.VaultDefault();
        private static readonly byte[] PasswordRow = VaultBinding.ForKeyRow("vault-1", "key-password", 0);
        private static readonly byte[] RecoveryRow = VaultBinding.ForKeyRow("vault-1", "key-recovery", 1);

        [Fact]
        public void PasswordWrapAndUnwrap()
        {
            byte[] vaultKey = VaultKeys.GenerateVaultKey();
            byte[] salt = VaultKeys.GenerateSalt();
            byte[] passwordKey = VaultKeys.DerivePasswordKey(Kdf, "correct horse battery staple", salt, Parameters);

            byte[] wrapped = VaultKeys.Wrap(passwordKey, vaultKey, PasswordRow);
            byte[] again = VaultKeys.DerivePasswordKey(Kdf, "correct horse battery staple", salt, Parameters);

            Assert.Equal(vaultKey, VaultKeys.Unwrap(again, wrapped, PasswordRow));
        }

        [Fact]
        public void WrongPasswordFailsTheTag()
        {
            byte[] vaultKey = VaultKeys.GenerateVaultKey();
            byte[] salt = VaultKeys.GenerateSalt();
            byte[] wrapped = VaultKeys.Wrap(VaultKeys.DerivePasswordKey(Kdf, "right", salt, Parameters), vaultKey, PasswordRow);

            byte[] wrong = VaultKeys.DerivePasswordKey(Kdf, "wrong", salt, Parameters);
            Assert.ThrowsAny<CryptographicException>(() => VaultKeys.Unwrap(wrong, wrapped, PasswordRow));
        }

        [Fact]
        public void RecoveryWrapAndUnwrap()
        {
            byte[] vaultKey = VaultKeys.GenerateVaultKey();
            byte[] recoveryKey = VaultKeys.GenerateRecoveryKey();

            byte[] wrapped = VaultKeys.Wrap(VaultKeys.RecoveryWrappingKey(recoveryKey), vaultKey, RecoveryRow);

            string written = RecoveryKeyFormat.Encode(recoveryKey);
            Assert.True(RecoveryKeyFormat.TryDecode(written, out byte[] typed));
            Assert.Equal(vaultKey, VaultKeys.Unwrap(VaultKeys.RecoveryWrappingKey(typed), wrapped, RecoveryRow));
        }

        [Fact]
        public void BothWrappingsOpenTheSameVaultKey()
        {
            byte[] vaultKey = VaultKeys.GenerateVaultKey();
            byte[] salt = VaultKeys.GenerateSalt();
            byte[] recoveryKey = VaultKeys.GenerateRecoveryKey();

            byte[] byPassword = VaultKeys.Wrap(VaultKeys.DerivePasswordKey(Kdf, "pw", salt, Parameters), vaultKey, PasswordRow);
            byte[] byRecovery = VaultKeys.Wrap(VaultKeys.RecoveryWrappingKey(recoveryKey), vaultKey, RecoveryRow);

            Assert.Equal(
                VaultKeys.Unwrap(VaultKeys.DerivePasswordKey(Kdf, "pw", salt, Parameters), byPassword, PasswordRow),
                VaultKeys.Unwrap(VaultKeys.RecoveryWrappingKey(recoveryKey), byRecovery, RecoveryRow));
        }

        [Fact]
        public void PasswordChangeRewrapsWithoutTouchingTheVaultKey()
        {
            byte[] vaultKey = VaultKeys.GenerateVaultKey();
            byte[] oldSalt = VaultKeys.GenerateSalt();
            byte[] oldWrapped = VaultKeys.Wrap(VaultKeys.DerivePasswordKey(Kdf, "old", oldSalt, Parameters), vaultKey, PasswordRow);

            byte[] unwrapped = VaultKeys.Unwrap(VaultKeys.DerivePasswordKey(Kdf, "old", oldSalt, Parameters), oldWrapped, PasswordRow);
            byte[] newSalt = VaultKeys.GenerateSalt();
            byte[] newWrapped = VaultKeys.Wrap(VaultKeys.DerivePasswordKey(Kdf, "new", newSalt, Parameters), unwrapped, PasswordRow);

            Assert.Equal(vaultKey, VaultKeys.Unwrap(VaultKeys.DerivePasswordKey(Kdf, "new", newSalt, Parameters), newWrapped, PasswordRow));
            Assert.ThrowsAny<CryptographicException>(() => VaultKeys.Unwrap(VaultKeys.DerivePasswordKey(Kdf, "old", oldSalt, Parameters), newWrapped, PasswordRow));
        }

        [Fact]
        public void WrappedKeyMovedToAnotherRowFails()
        {
            byte[] vaultKey = VaultKeys.GenerateVaultKey();
            byte[] wrappingKey = VaultKeys.GenerateVaultKey();
            byte[] wrapped = VaultKeys.Wrap(wrappingKey, vaultKey, PasswordRow);

            byte[] otherRow = VaultBinding.ForKeyRow("vault-1", "key-password-2", 0);
            Assert.ThrowsAny<CryptographicException>(() => VaultKeys.Unwrap(wrappingKey, wrapped, otherRow));
        }

        [Fact]
        public void GeneratedMaterialHasTheRightSizesAndIsRandom()
        {
            Assert.Equal(VaultCipher.KeyLength, VaultKeys.GenerateVaultKey().Length);
            Assert.Equal(VaultKeys.SaltLength, VaultKeys.GenerateSalt().Length);
            Assert.Equal(VaultKeys.RecoveryKeyLength, VaultKeys.GenerateRecoveryKey().Length);
            Assert.NotEqual(VaultKeys.GenerateVaultKey(), VaultKeys.GenerateVaultKey());
        }

        // BIP39 test vectors for 128-bit entropy.
        [Theory]
        [InlineData("00000000000000000000000000000000", "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about")]
        [InlineData("7f7f7f7f7f7f7f7f7f7f7f7f7f7f7f7f", "legal winner thank year wave sausage worth useful legal winner thank yellow")]
        [InlineData("80808080808080808080808080808080", "letter advice cage absurd amount doctor acoustic avoid letter advice cage above")]
        [InlineData("ffffffffffffffffffffffffffffffff", "zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo wrong")]
        [InlineData("9e885d952ad362caeb4efe34a8e91bd2", "ozone drill grab fiber curtain grace pudding thank cruise elder eight picnic")]
        public void RecoveryPhraseMatchesBip39Vectors(string entropyHex, string phrase)
        {
            byte[] entropy = Convert.FromHexString(entropyHex);
            Assert.Equal(phrase, RecoveryKeyFormat.Encode(entropy));
            Assert.True(RecoveryKeyFormat.TryDecode(phrase, out byte[] decoded));
            Assert.Equal(entropy, decoded);
        }

        [Fact]
        public void RecoveryPhraseRoundTripsRandomKeys()
        {
            for (int i = 0; i < 50; i++)
            {
                byte[] key = VaultKeys.GenerateRecoveryKey();
                string phrase = RecoveryKeyFormat.Encode(key);
                Assert.Equal(RecoveryKeyFormat.WordCount, phrase.Split(' ').Length);
                Assert.True(RecoveryKeyFormat.TryDecode(phrase, out byte[] decoded));
                Assert.Equal(key, decoded);
            }
        }

        [Theory]
        [InlineData("LEGAL WINNER THANK YEAR WAVE SAUSAGE WORTH USEFUL LEGAL WINNER THANK YELLOW")]
        [InlineData("  legal   winner\tthank year\nwave sausage worth useful legal winner thank yellow \n")]
        [InlineData("legal, winner, thank, year, wave, sausage, worth, useful, legal, winner, thank, yellow")]
        [InlineData("legal-winner-thank-year-wave-sausage-worth-useful-legal-winner-thank-yellow")]
        public void RecoveryPhraseDecodeForgivesCaseAndSeparators(string typed)
        {
            Assert.True(RecoveryKeyFormat.TryDecode(typed, out byte[] key));
            Assert.Equal("7F7F7F7F7F7F7F7F7F7F7F7F7F7F7F7F", Convert.ToHexString(key));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("legal winner thank year wave sausage worth useful legal winner thank")]
        [InlineData("legal winner thank year wave sausage worth useful legal winner thank yellow zoo")]
        [InlineData("legal winner thank year wave sausage worth useful legal winner thank yelow")]
        [InlineData("legal winner thank year wave sausage worth useful legal winner thank zoo")]
        [InlineData("winner legal thank year wave sausage worth useful legal winner thank yellow")]
        public void RecoveryPhraseDecodeRejectsBadInputWithoutSayingWhy(string typed)
        {
            Assert.False(RecoveryKeyFormat.TryDecode(typed, out byte[] key));
            Assert.Null(key);
        }

        [Fact]
        public void RecoveryPhraseChecksumCatchesMostSingleWordErrors()
        {
            byte[] key = VaultKeys.GenerateRecoveryKey();
            string[] words = RecoveryKeyFormat.Encode(key).Split(' ');
            int rejected = 0;
            string[] candidates = { "abandon", "zoo", "legal", "letter", "ozone", "wrong", "about", "yellow" };
            foreach (string candidate in candidates)
            {
                string[] altered = (string[])words.Clone();
                altered[5] = candidate;
                if (!RecoveryKeyFormat.TryDecode(string.Join(' ', altered), out _))
                {
                    rejected++;
                }
            }
            Assert.True(rejected >= 5, $"only {rejected} of {candidates.Length} substitutions were rejected");
        }
    }
}
