using System;
using System.Security.Cryptography;

namespace Lorestead.Core.Vault
{
    /// <summary>
    /// AES-256-GCM with the stored layout [version byte][12-byte nonce][ciphertext][16-byte tag].
    /// </summary>
    public static class VaultCipher
    {
        public const int KeyLength = 32;
        public const int NonceLength = 12;
        public const int TagLength = 16;
        public const byte FormatVersion = 1;

        private const int HeaderLength = 1 + NonceLength;

        public static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
        {
            RequireKey(key);

            byte[] output = new byte[HeaderLength + plaintext.Length + TagLength];
            output[0] = FormatVersion;

            Span<byte> nonce = output.AsSpan(1, NonceLength);
            RandomNumberGenerator.Fill(nonce);

            Span<byte> ciphertext = output.AsSpan(HeaderLength, plaintext.Length);
            Span<byte> tag = output.AsSpan(HeaderLength + plaintext.Length, TagLength);

            using AesGcm aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

            return output;
        }

        /// <summary>
        /// Throws <see cref="CryptographicException"/> for a wrong key, wrong associated data, or any modified byte.
        /// </summary>
        public static byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> stored, ReadOnlySpan<byte> associatedData)
        {
            RequireKey(key);

            if (stored.Length < HeaderLength + TagLength)
            {
                throw new CryptographicException("Vault ciphertext is too short.");
            }

            if (stored[0] != FormatVersion)
            {
                throw new CryptographicException($"Unknown vault ciphertext version {stored[0]}.");
            }

            ReadOnlySpan<byte> nonce = stored.Slice(1, NonceLength);
            ReadOnlySpan<byte> ciphertext = stored.Slice(HeaderLength, stored.Length - HeaderLength - TagLength);
            ReadOnlySpan<byte> tag = stored.Slice(stored.Length - TagLength, TagLength);

            byte[] plaintext = new byte[ciphertext.Length];

            using AesGcm aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

            return plaintext;
        }

        private static void RequireKey(ReadOnlySpan<byte> key)
        {
            if (key.Length != KeyLength)
            {
                throw new ArgumentException($"Vault keys are {KeyLength} bytes.", nameof(key));
            }
        }
    }
}
