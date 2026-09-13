namespace DataGuard.Core.Security.SecretStores;

/// <summary>Explicit platform secret-store contract used for encrypt-required credential persistence.</summary>
public interface ICredentialSecretStore
{
    string ReferencePrefix { get; }

    void Store(string service, string account, string value);

    string Read(string service, string account);
}

internal sealed class PlatformCredentialSecretStore : ICredentialSecretStore
{
    private readonly Func<string, string, string, string> _store;
    private readonly Func<string, string, string> _read;

    private PlatformCredentialSecretStore(string prefix, Action<string, string, string> store, Func<string, string, string> read)
    {
        ReferencePrefix = prefix;
        _store = (service, account, value) =>
        {
            store(service, account, value);
            return prefix;
        };
        _read = read;
    }

    public string ReferencePrefix { get; }

    public void Store(string service, string account, string value) => _ = _store(service, account, value);

    public string Read(string service, string account) => _read(service, account);

    public static ICredentialSecretStore Create()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new PlatformCredentialSecretStore(
                "KEYCHAIN:",
                MacOsKeychainSecretStore.Store,
                MacOsKeychainSecretStore.Read);
        }

        if (OperatingSystem.IsLinux())
        {
            return new PlatformCredentialSecretStore(
                "SECRET-SERVICE:",
                LinuxSecretServiceSecretStore.Store,
                LinuxSecretServiceSecretStore.Read);
        }

        return new UnavailableCredentialSecretStore();
    }
}

internal sealed class UnavailableCredentialSecretStore : ICredentialSecretStore
{
    public string ReferencePrefix => string.Empty;

    public void Store(string service, string account, string value) =>
        throw new PlatformNotSupportedException("No platform credential secret store is available.");

    public string Read(string service, string account) =>
        throw new PlatformNotSupportedException("No platform credential secret store is available.");
}
