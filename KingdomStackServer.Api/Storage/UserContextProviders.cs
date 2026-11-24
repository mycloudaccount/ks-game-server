using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace KingdomStackServer.Api.Storage;

/// <summary>Resolves the user whose data the API reads and writes (the storage "scope key").</summary>
public interface IUserContextProvider
{
    Task<StorageUserContext> GetUserContextAsync(CancellationToken cancellationToken);

    Task<AzureStorageIdentityInfo> GetIdentityInfoAsync(CancellationToken cancellationToken);
}

public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// "AzureCredential" (default): the user is whoever owns the server's Azure credential.
    /// "Configured": a fixed local user from the settings below. Development only.
    /// </summary>
    public string Mode { get; set; } = IdentityModes.AzureCredential;

    public string? ScopeKey { get; set; }

    public string? Email { get; set; }

    public string? DisplayName { get; set; }
}

public static class IdentityModes
{
    public const string AzureCredential = "AzureCredential";
    public const string Configured = "Configured";
}

internal static partial class ScopeKeys
{
    [GeneratedRegex("[^a-zA-Z0-9._-]+")]
    private static partial Regex InvalidCharacters();

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant()
            .Replace("live.com#", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('@', '_');
        normalized = InvalidCharacters().Replace(normalized, "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

/// <summary>Original behavior: reads the user from the claims of the server's Azure storage token.</summary>
public sealed class AzureCredentialUserContextProvider(BlobStoreFactory blobStoreFactory) : IUserContextProvider
{
    private static readonly string[] StorageScopes = ["https://storage.azure.com/.default"];

    public async Task<AzureStorageIdentityInfo> GetIdentityInfoAsync(CancellationToken cancellationToken)
    {
        var credential = blobStoreFactory.AzureCredential;
        var accessToken = await credential.GetTokenAsync(new TokenRequestContext(StorageScopes), cancellationToken);

        return new AzureStorageIdentityInfo(
            credential.GetType().Name,
            accessToken.ExpiresOn,
            ParseJwtClaims(accessToken.Token));
    }

    public async Task<StorageUserContext> GetUserContextAsync(CancellationToken cancellationToken)
    {
        var identity = await GetIdentityInfoAsync(cancellationToken);

        identity.Claims.TryGetValue("oid", out var objectIds);
        identity.Claims.TryGetValue("email", out var emails);
        identity.Claims.TryGetValue("preferred_username", out var preferredUsernames);
        identity.Claims.TryGetValue("unique_name", out var uniqueNames);
        identity.Claims.TryGetValue("name", out var names);

        var objectId = objectIds?.FirstOrDefault();
        var email = emails?.FirstOrDefault()
            ?? preferredUsernames?.FirstOrDefault()
            ?? uniqueNames?.FirstOrDefault();
        var displayName = names?.FirstOrDefault();
        var scopeKey = ScopeKeys.Normalize(objectId)
            ?? ScopeKeys.Normalize(email)
            ?? "local-dev-user";

        return new StorageUserContext(scopeKey, objectId, email, displayName);
    }

    private static IReadOnlyDictionary<string, string[]> ParseJwtClaims(string token)
    {
        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        var payload = segments[1].Replace('-', '+').Replace('_', '/');
        var remainder = payload.Length % 4;
        if (remainder > 0)
        {
            payload = payload.PadRight(payload.Length + (4 - remainder), '=');
        }

        using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
        var claims = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            claims[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.Array => property.Value.EnumerateArray().Select(element => element.ToString()).ToArray(),
                _ => [property.Value.ToString()]
            };
        }

        return claims;
    }
}

/// <summary>A fixed user from configuration, for running locally against S3 before real sign-in exists.</summary>
public sealed class ConfiguredUserContextProvider : IUserContextProvider
{
    private readonly StorageUserContext _userContext;

    public ConfiguredUserContextProvider(IOptions<IdentityOptions> options)
    {
        var value = options.Value;
        var scopeKey = ScopeKeys.Normalize(value.ScopeKey)
            ?? throw new InvalidOperationException(
                $"{IdentityOptions.SectionName}:ScopeKey must be set when {IdentityOptions.SectionName}:Mode is {IdentityModes.Configured}.");

        _userContext = new StorageUserContext(scopeKey, value.ScopeKey, value.Email, value.DisplayName);
    }

    public Task<StorageUserContext> GetUserContextAsync(CancellationToken cancellationToken)
        => Task.FromResult(_userContext);

    public Task<AzureStorageIdentityInfo> GetIdentityInfoAsync(CancellationToken cancellationToken)
    {
        var claims = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["oid"] = [_userContext.ObjectId ?? _userContext.ScopeKey]
        };
        if (!string.IsNullOrWhiteSpace(_userContext.Email))
        {
            claims["email"] = [_userContext.Email];
        }
        if (!string.IsNullOrWhiteSpace(_userContext.DisplayName))
        {
            claims["name"] = [_userContext.DisplayName];
        }

        return Task.FromResult(new AzureStorageIdentityInfo(IdentityModes.Configured, DateTimeOffset.MaxValue, claims));
    }
}
