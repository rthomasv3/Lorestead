using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Lorestead.Core.Crypto
{
    /// <summary>
    /// Encodes a recovery key as a twelve-word BIP39 phrase: English list, 11 bits per word, 4-bit SHA-256 checksum.
    /// </summary>
    public static class RecoveryKeyFormat
    {
        public const int WordCount = 12;

        private const int WordBits = 11;
        private const int ChecksumBits = 4;
        private const int ListSize = 1 << WordBits;

        private static readonly char[] Separators = { ' ', '\t', '\r', '\n', ',', ';', '-', '_', '.', '/' };
        private static readonly Lazy<string[]> Words = new Lazy<string[]>(LoadWords);
        private static readonly Lazy<Dictionary<string, int>> Index = new Lazy<Dictionary<string, int>>(BuildIndex);

        public static string Encode(ReadOnlySpan<byte> recoveryKey)
        {
            if (recoveryKey.Length != VaultKeys.RecoveryKeyLength)
            {
                throw new ArgumentException($"Recovery keys are {VaultKeys.RecoveryKeyLength} bytes.", nameof(recoveryKey));
            }

            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(recoveryKey, hash);

            Span<byte> bits = stackalloc byte[VaultKeys.RecoveryKeyLength + 1];
            recoveryKey.CopyTo(bits);
            bits[VaultKeys.RecoveryKeyLength] = hash[0];

            string[] words = Words.Value;
            StringBuilder phrase = new StringBuilder(WordCount * 9);

            for (int word = 0; word < WordCount; word++)
            {
                int index = ReadBits(bits, word * WordBits, WordBits);
                if (word > 0)
                {
                    phrase.Append(' ');
                }
                phrase.Append(words[index]);
            }

            return phrase.ToString();
        }

        /// <summary>
        /// Returns false for any malformed phrase without saying why, so callers show one message for every failure.
        /// </summary>
        public static bool TryDecode(string text, out byte[] recoveryKey)
        {
            recoveryKey = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] typed = text.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (typed.Length != WordCount)
            {
                return false;
            }

            Dictionary<string, int> index = Index.Value;
            Span<byte> bits = stackalloc byte[VaultKeys.RecoveryKeyLength + 1];

            for (int word = 0; word < WordCount; word++)
            {
                if (!index.TryGetValue(typed[word], out int value))
                {
                    return false;
                }
                WriteBits(bits, word * WordBits, WordBits, value);
            }

            byte[] key = bits.Slice(0, VaultKeys.RecoveryKeyLength).ToArray();

            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(key, hash);

            int expectedChecksum = hash[0] >> (8 - ChecksumBits);
            int actualChecksum = bits[VaultKeys.RecoveryKeyLength] >> (8 - ChecksumBits);
            if (expectedChecksum != actualChecksum)
            {
                CryptographicOperations.ZeroMemory(key);
                return false;
            }

            recoveryKey = key;
            return true;
        }

        private static int ReadBits(ReadOnlySpan<byte> bits, int offset, int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++)
            {
                int position = offset + i;
                int bit = (bits[position / 8] >> (7 - position % 8)) & 1;
                value = (value << 1) | bit;
            }
            return value;
        }

        private static void WriteBits(Span<byte> bits, int offset, int count, int value)
        {
            for (int i = 0; i < count; i++)
            {
                int position = offset + i;
                int bit = (value >> (count - 1 - i)) & 1;
                if (bit != 0)
                {
                    bits[position / 8] |= (byte)(1 << (7 - position % 8));
                }
            }
        }

        private static string[] LoadWords()
        {
            using Stream stream = typeof(RecoveryKeyFormat).Assembly.GetManifestResourceStream("Lorestead.Core.Crypto.bip39-english.txt");
            if (stream == null)
            {
                throw new InvalidOperationException("The BIP39 wordlist resource is missing.");
            }

            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            List<string> words = new List<string>(ListSize);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string word = line.Trim();
                if (word.Length > 0)
                {
                    words.Add(word);
                }
            }

            if (words.Count != ListSize)
            {
                throw new InvalidOperationException($"The BIP39 wordlist has {words.Count} words, expected {ListSize}.");
            }

            return words.ToArray();
        }

        private static Dictionary<string, int> BuildIndex()
        {
            string[] words = Words.Value;
            Dictionary<string, int> index = new Dictionary<string, int>(words.Length, StringComparer.Ordinal);
            for (int i = 0; i < words.Length; i++)
            {
                index[words[i]] = i;
            }
            return index;
        }
    }
}
