using System;
using System.Security.Cryptography;
using System.Text;

namespace Lorestead.Core.Vault
{
    /// <summary>
    /// Generates the vault key, salts, and recovery keys, and wraps the vault key under a password or recovery key.
    /// </summary>
    public static class VaultKeys
    {
        public const int SaltLength = 16;
        public const int RecoveryKeyLength = 16;

        public static byte[] GenerateVaultKey()
        {
            return RandomNumberGenerator.GetBytes(VaultCipher.KeyLength);
        }

        public static byte[] GenerateSalt()
        {
            return RandomNumberGenerator.GetBytes(SaltLength);
        }

        public static byte[] GenerateRecoveryKey()
        {
            return RandomNumberGenerator.GetBytes(RecoveryKeyLength);
        }

        public static byte[] DerivePasswordKey(IPasswordKdf kdf, string password, ReadOnlySpan<byte> salt, KdfParameters parameters)
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            try
            {
                return kdf.Derive(passwordBytes, salt, parameters, VaultCipher.KeyLength);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }

        /// <summary>
        /// Widens the 16-byte recovery key to a full cipher key with HKDF.
        /// </summary>
        public static byte[] RecoveryWrappingKey(ReadOnlySpan<byte> recoveryKey)
        {
            if (recoveryKey.Length != RecoveryKeyLength)
            {
                throw new ArgumentException($"Recovery keys are {RecoveryKeyLength} bytes.", nameof(recoveryKey));
            }

            byte[] widened = new byte[VaultCipher.KeyLength];
            HKDF.DeriveKey(HashAlgorithmName.SHA256, recoveryKey, widened, salt: null, info: Encoding.ASCII.GetBytes("lorestead-vault-recovery"));
            return widened;
        }

        public static byte[] Wrap(ReadOnlySpan<byte> wrappingKey, ReadOnlySpan<byte> vaultKey, ReadOnlySpan<byte> rowBinding)
        {
            if (vaultKey.Length != VaultCipher.KeyLength)
            {
                throw new ArgumentException($"Vault keys are {VaultCipher.KeyLength} bytes.", nameof(vaultKey));
            }

            return VaultCipher.Encrypt(wrappingKey, vaultKey, rowBinding);
        }

        /// <summary>
        /// A wrong password or recovery key surfaces as <see cref="CryptographicException"/> from the failed tag.
        /// </summary>
        public static byte[] Unwrap(ReadOnlySpan<byte> wrappingKey, ReadOnlySpan<byte> wrapped, ReadOnlySpan<byte> rowBinding)
        {
            byte[] vaultKey = VaultCipher.Decrypt(wrappingKey, wrapped, rowBinding);

            if (vaultKey.Length != VaultCipher.KeyLength)
            {
                CryptographicOperations.ZeroMemory(vaultKey);
                throw new CryptographicException("Unwrapped vault key has the wrong length.");
            }

            return vaultKey;
        }
    }
}
