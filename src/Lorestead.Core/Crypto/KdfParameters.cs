namespace Lorestead.Core.Crypto
{
    /// <summary>
    /// Argon2id cost parameters, stored alongside the wrapped key they produced.
    /// </summary>
    public sealed class KdfParameters
    {
        public int MemoryKiB { get; set; }
        public int Iterations { get; set; }
        public int Parallelism { get; set; }

        public static KdfParameters VaultDefault()
        {
            return new KdfParameters
            {
                MemoryKiB = 64 * 1024,
                Iterations = 3,
                Parallelism = 4,
            };
        }
    }
}
