using System.Runtime.CompilerServices;

namespace SylvaNote.IntegrationTests
{
    internal static class TestSetup
    {
        // Tests reference the server project, so the provider here is SQLCipher
        // (bundle_e_sqlcipher) - a strict superset of SQLite, so every non-encryption
        // test runs unchanged.
        [ModuleInitializer]
        internal static void InitializeSqliteProvider()
        {
            SQLitePCL.Batteries_V2.Init();
        }
    }
}
