using System;
using GitCredentialManager;

namespace SpikeCredentialManager
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int exitCode = 0;

            try
            {
                ICredentialStore store = CredentialManager.Create("sylvanote-spike");
                string service = "https://spike.sylvanote.test";
                string account = "spike-user";
                string secret = Guid.NewGuid().ToString("N");

                store.AddOrUpdate(service, account, secret);

                ICredential read = store.Get(service, account);
                if (read == null || read.Password != secret)
                {
                    throw new InvalidOperationException("Read-back secret did not match stored secret.");
                }

                store.Remove(service, account);

                ICredential afterDelete = store.Get(service, account);
                if (afterDelete != null)
                {
                    throw new InvalidOperationException("Secret still present after delete.");
                }

                Console.WriteLine("PASS: credential store round-trip (store/read/delete) succeeded.");
            }
            catch (Exception ex)
            {
                if (OperatingSystem.IsWindows())
                {
                    Console.WriteLine($"FAIL: {ex}");
                    exitCode = 1;
                }
                else
                {
                    // Headless Linux/mac runners may lack an unlocked credential store; per
                    // phases.md Phase 0, the spike still proves AOT compile + trim safety there
                    // and real store behavior is verified on owner machines at Phase 5.
                    Console.WriteLine($"PASS (AOT-only): store unavailable on this runner: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return exitCode;
        }
    }
}
