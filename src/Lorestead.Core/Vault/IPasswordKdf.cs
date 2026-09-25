using System;

namespace Lorestead.Core.Vault
{
    /// <summary>
    /// Derives a key of the requested length from a password and salt.
    /// </summary>
    public interface IPasswordKdf
    {
        byte[] Derive(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, KdfParameters parameters, int length);
    }
}
