namespace KingdomStackServer.Api;

public sealed record AzureStorageIdentityInfo(
    string CredentialType,
    DateTimeOffset ExpiresOn,
    IReadOnlyDictionary<string, string[]> Claims);
