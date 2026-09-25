using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Lorestead.Core.Crypto
{
    /// <summary>
    /// Argon2id through the reference C library; parallelism is the thread count.
    /// </summary>
    public sealed class Argon2idKdf : IPasswordKdf
    {
        private const string LibraryName = "libargon2";

        static Argon2idKdf()
        {
            // Statically linked on iOS, so the import resolves against the executable.
            if (OperatingSystem.IsIOS())
            {
                NativeLibrary.SetDllImportResolver(typeof(Argon2idKdf).Assembly, ResolveInMainProgram);
            }
        }

        public byte[] Derive(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, KdfParameters parameters, int length)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            if (parameters.MemoryKiB < 8 * parameters.Parallelism || parameters.Iterations < 1 || parameters.Parallelism < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters), "Argon2id needs at least one pass, one lane, and 8 KiB per lane.");
            }
            if (salt.Length < 8)
            {
                throw new ArgumentException("Argon2id salts are at least 8 bytes.", nameof(salt));
            }
            if (length < 4)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Argon2id output is at least 4 bytes.");
            }

            byte[] output = new byte[length];
            int result;

            unsafe
            {
                fixed (byte* passwordPtr = password)
                fixed (byte* saltPtr = salt)
                fixed (byte* outputPtr = output)
                {
                    result = argon2id_hash_raw(
                        (uint)parameters.Iterations,
                        (uint)parameters.MemoryKiB,
                        (uint)parameters.Parallelism,
                        passwordPtr, (nuint)password.Length,
                        saltPtr, (nuint)salt.Length,
                        outputPtr, (nuint)output.Length);
                }
            }

            if (result != 0)
            {
                CryptographicOperations.ZeroMemory(output);
                throw new CryptographicException($"Argon2id failed: {Marshal.PtrToStringUTF8(argon2_error_message(result))} ({result}).");
            }

            return output;
        }

        private static IntPtr ResolveInMainProgram(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            return libraryName == LibraryName ? NativeLibrary.GetMainProgramHandle() : IntPtr.Zero;
        }

        [DllImport(LibraryName)]
        private static extern unsafe int argon2id_hash_raw(
            uint t_cost, uint m_cost, uint parallelism,
            byte* pwd, nuint pwdlen,
            byte* salt, nuint saltlen,
            byte* hash, nuint hashlen);

        [DllImport(LibraryName)]
        private static extern IntPtr argon2_error_message(int error_code);
    }
}
