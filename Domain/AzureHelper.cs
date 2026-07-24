using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;


namespace Domain
{
    public static class AzureHelper
    {
        private static SecretClient? _secretClient;
        public static void Initialize(string keyVaultUrl, bool isDevelopment, string? userAssignedManagedIdentityClientId = null)
        {
            if (string.IsNullOrWhiteSpace(keyVaultUrl))
            {
                throw new ArgumentException(
                    "Key Vault URL is required.",
                    nameof(keyVaultUrl));
            }

            TokenCredential credential;

            if (isDevelopment)
            {
                credential = new DefaultAzureCredential(
                    new DefaultAzureCredentialOptions
                    {
                        ExcludeManagedIdentityCredential = true
                    });
            }
            else if (!string.IsNullOrWhiteSpace(userAssignedManagedIdentityClientId))
            {
                credential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(userAssignedManagedIdentityClientId));
            }
            else
            {
                // Uses the VM's system-assigned managed identity.
                //credential = new ManagedIdentityCredential();
                credential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
            }

            _secretClient = new SecretClient(
                new Uri(keyVaultUrl),
                credential);
        }

        public static async Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
        {
            if (_secretClient is null)
            {
                throw new InvalidOperationException("AzureHelper.Initialize() must be called first.");
            }

            KeyVaultSecret secret =
                await _secretClient.GetSecretAsync(secretName, cancellationToken: cancellationToken);

            return secret.Value;
        }
    }
}
