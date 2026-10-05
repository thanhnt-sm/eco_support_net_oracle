using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using DataGuard.Core.Models;
using DataGuard.Core.Security;
using DataGuard.Core.Security.SecretStores;

namespace DataGuard.Cli.Security;

/// <summary>
/// AWS Secrets Manager store registered by the CLI with <see cref="ZeroTrustCredentialProvider"/>. It lives in the CLI
/// so <c>DataGuard.Core</c> carries no AWS SDK dependency; library hosts that need it register their own
/// <see cref="ISecretStore"/>. Credentials come from the standard AWS chain (environment, profile, instance role).
/// </summary>
public sealed class AwsSecretsManagerSecretStore : ISecretStore
{
    public string Name => ZeroTrustCredentialProvider.AwsSecretsManagerStoreName;

    public bool IsConfigured(DataGuardConfiguration configuration) => !string.IsNullOrWhiteSpace(configuration?.AwsRegion);

    public async Task<string?> GetSecretAsync(string secretName, DataGuardConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        using var client = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(configuration.AwsRegion!));
        try
        {
            var response = await client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretName }, cancellationToken);
            return string.IsNullOrEmpty(response.SecretString) ? null : response.SecretString;
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }
}
