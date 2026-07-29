using System;
using GitCredentialManager;
using Lorestead.Client.Services.Abstractions;

namespace Lorestead.Client.Services;

// The bearer token lives in the OS credential store (Windows Credential Manager /
// macOS Keychain / Linux Secret Service), never the DB. Linux needs an explicit
// backing store selected before Create, with gpg as the fallback when no Secret
// Service is running - the AudibleDownloader pattern the Phase 0 spike validated.
public sealed class SyncCredentialStore
{
    private const string StoreName = "lorestead";
    // The Windows backend parses the service as a URI when building the credential
    // target name, so it must be URI-shaped (matches the Phase 0 spike).
    private const string Service = "https://sync.lorestead";
    private const string Account = "bearer-token";

    private readonly ILoggingService _logger;
    private ICredentialStore _store;

    public SyncCredentialStore(ILoggingService logger)
    {
        _logger = logger;
    }

    // Null when no token is stored or the credential store is unavailable - the
    // engine reports the latter through the Settings status label, never a popup.
    public string GetToken()
    {
        string token = null;

        try
        {
            ICredential credential = GetStore().Get(Service, Account);
            token = credential?.Password;
        }
        catch (Exception ex)
        {
            _logger.Error("Sync", "Reading the sync token from the credential store failed", ex);
        }

        return token;
    }

    public bool HasToken()
    {
        return !string.IsNullOrEmpty(GetToken());
    }

    public void SaveToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            GetStore().Remove(Service, Account);
        }
        else
        {
            GetStore().AddOrUpdate(Service, Account, token);
        }
    }

    private ICredentialStore GetStore()
    {
        if (_store == null)
        {
            if (OperatingSystem.IsLinux())
            {
                Environment.SetEnvironmentVariable("GCM_CREDENTIAL_STORE", "secretservice");

                try
                {
                    _store = CredentialManager.Create(StoreName);
                }
                catch
                {
                    Environment.SetEnvironmentVariable("GCM_CREDENTIAL_STORE", "gpg");
                    _store = CredentialManager.Create(StoreName);
                }
            }
            else
            {
                _store = CredentialManager.Create(StoreName);
            }
        }

        return _store;
    }
}
