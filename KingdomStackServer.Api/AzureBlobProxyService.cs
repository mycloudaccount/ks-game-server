using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KingdomStackServer.Api;

public class AzureBlobProxyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] StorageScopes = ["https://storage.azure.com/.default"];
    private static readonly Regex InvalidGameIdCharacters = new("[^a-zA-Z0-9._-]+", RegexOptions.Compiled);
    private readonly AzureBlobProxyOptions _options;
    private readonly ILogger<AzureBlobProxyService> _logger;
    private readonly BlobContainerClient _blobContainerClient;
    private readonly BlobContainerClient _userGamesBlobContainerClient;
    private readonly BlobContainerClient _userPreferencesBlobContainerClient;
    private readonly TokenCredential _credential;

    public AzureBlobProxyService(
        IOptions<AzureBlobProxyOptions> options,
        ILogger<AzureBlobProxyService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _credential = CreateCredential();
        _blobContainerClient = CreateBlobContainerClient(_options.StorageBaseUrl, nameof(_options.StorageBaseUrl));
        _userGamesBlobContainerClient = CreateBlobContainerClient(
            string.IsNullOrWhiteSpace(_options.UserGamesStorageBaseUrl) ? _options.StorageBaseUrl : _options.UserGamesStorageBaseUrl,
            nameof(_options.UserGamesStorageBaseUrl));
        _userPreferencesBlobContainerClient = CreateBlobContainerClient(
            string.IsNullOrWhiteSpace(_options.UserPreferencesStorageBaseUrl) ? _options.StorageBaseUrl : _options.UserPreferencesStorageBaseUrl,
            nameof(_options.UserPreferencesStorageBaseUrl));
    }

    public async Task<string> GetTilesManifestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var blobClient = _blobContainerClient.GetBlobClient(BuildBlobName(_options.TilesPrefix, "tiles.json"));
            var response = await blobClient.DownloadContentAsync(cancellationToken);

            return response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException("tiles.json was not found in Azure blob storage.", ex);
        }
    }

    public async Task<TileManifest> GetTileManifestModelAsync(CancellationToken cancellationToken)
    {
        var manifestJson = await GetTilesManifestAsync(cancellationToken);
        var manifest = JsonSerializer.Deserialize<TileManifest>(manifestJson, JsonOptions);

        if (manifest is null)
        {
            throw new InvalidOperationException("tiles.json could not be parsed.");
        }

        return manifest;
    }

    public async Task<AzureBlobContent?> GetTileAssetAsync(
        string blobPath,
        CancellationToken cancellationToken)
        => await GetAssetAsync(_options.TilesPrefix, blobPath, cancellationToken);

    public async Task<AzureBlobContent?> GetCharacterAssetAsync(
        string blobPath,
        CancellationToken cancellationToken)
        => await GetAssetAsync(_options.CharactersPrefix, blobPath, cancellationToken);

    public async Task<AzureBlobContent?> GetUserGameAssetAsync(
        string scopeKey,
        string blobPath,
        CancellationToken cancellationToken)
        => await GetAssetAsync(
            _userGamesBlobContainerClient,
            BuildUserGamesRootPrefix(scopeKey),
            blobPath,
            cancellationToken);

    public async Task<AzureBlobContent?> GetAssetAsync(
        string prefix,
        string blobPath,
        CancellationToken cancellationToken)
        => await GetAssetAsync(_blobContainerClient, prefix, blobPath, cancellationToken);

    public async Task<AzureBlobContent?> GetAssetAsync(
        BlobContainerClient containerClient,
        string prefix,
        string blobPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var blobClient = containerClient.GetBlobClient(BuildBlobName(prefix, blobPath));
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);

            return new AzureBlobContent
            {
                Content = response.Value.Content,
                ContentType = response.Value.Details.ContentType ?? "application/octet-stream",
                ContentLength = response.Value.Details.ContentLength,
                ETag = response.Value.Details.ETag.ToString(),
                LastModified = response.Value.Details.LastModified
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<TileAssetListItem>> ListTileAssetsAsync(
        string? prefix,
        CancellationToken cancellationToken)
        => await ListAssetsAsync(_options.TilesPrefix, prefix, cancellationToken);

    public async Task<IReadOnlyList<TileAssetListItem>> ListCharacterAssetsAsync(
        string? prefix,
        CancellationToken cancellationToken)
        => await ListAssetsAsync(_options.CharactersPrefix, prefix, cancellationToken);

    public async Task<IReadOnlyList<TileAssetListItem>> ListAssetsAsync(
        string rootPrefix,
        string? prefix,
        CancellationToken cancellationToken)
    {
        var items = new List<TileAssetListItem>();
        var requestedPrefix = BuildBlobName(rootPrefix, prefix);

        await foreach (var blobItem in _blobContainerClient.GetBlobsAsync(
                           traits: BlobTraits.None,
                           states: BlobStates.None,
                           prefix: requestedPrefix,
                           cancellationToken: cancellationToken))
        {
            var relativeName = TrimPrefix(rootPrefix, blobItem.Name);
            if (string.IsNullOrWhiteSpace(relativeName))
            {
                continue;
            }

            items.Add(new TileAssetListItem(
                relativeName,
                blobItem.Properties.ContentLength,
                blobItem.Properties.LastModified));
        }

        return items;
    }

    public async Task<BlobUploadResult> UploadUserGameAsync(
        string scopeKey,
        string blobPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var normalizedBlobPath = NormalizeBlobPath(blobPath);
        var blobName = BuildBlobName(BuildUserGamesRootPrefix(scopeKey), normalizedBlobPath);
        var blobClient = _userGamesBlobContainerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = string.IsNullOrWhiteSpace(contentType)
                        ? "application/octet-stream"
                        : contentType
                }
            },
            cancellationToken);

        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

        return new BlobUploadResult(
            normalizedBlobPath,
            blobClient.Uri.ToString(),
            properties.Value.ETag.ToString(),
            properties.Value.ContentLength,
            properties.Value.LastModified);
    }

    public async Task<IReadOnlyList<UserGameMetadataResponse>> ListUserGameAssetsAsync(
        string scopeKey,
        string? prefix,
        CancellationToken cancellationToken)
    {
        var userRootPrefix = BuildUserGamesRootPrefix(scopeKey);
        var requestedPrefix = BuildBlobName(userRootPrefix, prefix);
        var items = new List<UserGameMetadataResponse>();

        await foreach (var blobItem in _userGamesBlobContainerClient.GetBlobsAsync(
                           traits: BlobTraits.Metadata,
                           states: BlobStates.None,
                           prefix: requestedPrefix,
                           cancellationToken: cancellationToken))
        {
            var relativeBlobPath = TrimPrefix(userRootPrefix, blobItem.Name);
            if (string.IsNullOrWhiteSpace(relativeBlobPath))
            {
                continue;
            }

            var fileName = Path.GetFileName(relativeBlobPath);
            var defaultGameId = Path.GetFileNameWithoutExtension(fileName);
            blobItem.Metadata.TryGetValue("gameid", out var gameId);
            blobItem.Metadata.TryGetValue("name", out var name);

            items.Add(new UserGameMetadataResponse(
                string.IsNullOrWhiteSpace(gameId) ? defaultGameId : gameId,
                string.IsNullOrWhiteSpace(name) ? defaultGameId : name,
                relativeBlobPath,
                string.Empty,
                string.Empty,
                blobItem.Properties.CreatedOn,
                blobItem.Properties.LastModified,
                blobItem.Properties.ContentLength));
        }

        return items
            .OrderByDescending(item => item.LastModified)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<UserGameSaveResult> SaveUserGameJsonAsync(
        string scopeKey,
        string gameId,
        string name,
        string jsonContent,
        CancellationToken cancellationToken)
    {
        var normalizedGameId = NormalizeGameId(gameId);
        var relativeBlobPath = $"{normalizedGameId}.json";
        var blobName = BuildBlobName(BuildUserGamesRootPrefix(scopeKey), relativeBlobPath);
        var blobClient = _userGamesBlobContainerClient.GetBlobClient(blobName);

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));
        await blobClient.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "application/json"
                },
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["gameid"] = normalizedGameId,
                    ["name"] = name
                }
            },
            cancellationToken);

        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

        return new UserGameSaveResult(
            normalizedGameId,
            name,
            relativeBlobPath,
            blobClient.Uri.ToString(),
            properties.Value.CreatedOn,
            properties.Value.LastModified,
            properties.Value.ContentLength);
    }

    public async Task<UserPreferencesSaveResult> SaveUserPreferencesJsonAsync(
        string scopeKey,
        string jsonContent,
        CancellationToken cancellationToken)
    {
        var relativeBlobPath = "preferences.json";
        var blobName = BuildBlobName(BuildUserPreferencesRootPrefix(scopeKey), relativeBlobPath);
        var blobClient = _userPreferencesBlobContainerClient.GetBlobClient(blobName);

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));
        await blobClient.UploadAsync(
            stream,
            overwrite: true,
            cancellationToken: cancellationToken);

        await blobClient.SetHttpHeadersAsync(
            new BlobHttpHeaders
            {
                ContentType = "application/json"
            },
            cancellationToken: cancellationToken);

        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

        return new UserPreferencesSaveResult(
            relativeBlobPath,
            blobClient.Uri.ToString(),
            properties.Value.CreatedOn,
            properties.Value.LastModified,
            properties.Value.ContentLength);
    }

    public async Task<UserPreferencesContentResponse?> GetUserPreferencesJsonAsync(
        string scopeKey,
        CancellationToken cancellationToken)
    {
        var relativeBlobPath = "preferences.json";
        var blobName = BuildBlobName(BuildUserPreferencesRootPrefix(scopeKey), relativeBlobPath);
        var blobClient = _userPreferencesBlobContainerClient.GetBlobClient(blobName);

        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var content = response.Value.Content.ToString();
            using var document = JsonDocument.Parse(content);

            return new UserPreferencesContentResponse(
                relativeBlobPath,
                properties.Value.ContentLength,
                properties.Value.CreatedOn,
                properties.Value.LastModified,
                document.RootElement.Clone());
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> DeleteUserPreferencesAsync(
        string scopeKey,
        CancellationToken cancellationToken)
    {
        var relativeBlobPath = "preferences.json";
        var blobName = BuildBlobName(BuildUserPreferencesRootPrefix(scopeKey), relativeBlobPath);
        var blobClient = _userPreferencesBlobContainerClient.GetBlobClient(blobName);
        var response = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

        return response.Value;
    }

    public async Task<UserGameContentResponse?> GetUserGameJsonAsync(
        string scopeKey,
        string gameId,
        CancellationToken cancellationToken)
    {
        var normalizedGameId = NormalizeGameId(gameId);
        var relativeBlobPath = $"{normalizedGameId}.json";
        var blobName = BuildBlobName(BuildUserGamesRootPrefix(scopeKey), relativeBlobPath);
        var blobClient = _userGamesBlobContainerClient.GetBlobClient(blobName);

        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var content = response.Value.Content.ToString();
            using var document = JsonDocument.Parse(content);
            var game = document.RootElement.Clone();

            properties.Value.Metadata.TryGetValue("name", out var name);

            return new UserGameContentResponse(
                normalizedGameId,
                string.IsNullOrWhiteSpace(name) ? normalizedGameId : name,
                relativeBlobPath,
                properties.Value.ContentLength,
                properties.Value.CreatedOn,
                string.Empty,
                string.Empty,
                properties.Value.LastModified,
                game);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> DeleteUserGameAsync(
        string scopeKey,
        string gameId,
        CancellationToken cancellationToken)
    {
        var normalizedGameId = NormalizeGameId(gameId);
        var relativeBlobPath = $"{normalizedGameId}.json";
        var blobName = BuildBlobName(BuildUserGamesRootPrefix(scopeKey), relativeBlobPath);
        var blobClient = _userGamesBlobContainerClient.GetBlobClient(blobName);
        var response = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

        return response.Value;
    }

    public async Task<AzureStorageIdentityInfo> GetStorageIdentityInfoAsync(CancellationToken cancellationToken)
    {
        var accessToken = await _credential.GetTokenAsync(
            new TokenRequestContext(StorageScopes),
            cancellationToken);

        var claims = ParseJwtClaims(accessToken.Token);

        return new AzureStorageIdentityInfo(
            _credential.GetType().Name,
            accessToken.ExpiresOn,
            claims);
    }

    public async Task<StorageUserContext> GetStorageUserContextAsync(CancellationToken cancellationToken)
    {
        var identity = await GetStorageIdentityInfoAsync(cancellationToken);

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
        var scopeKey = NormalizeScopeKey(objectId)
            ?? NormalizeScopeKey(email)
            ?? "local-dev-user";

        return new StorageUserContext(scopeKey, objectId, email, displayName);
    }

    private TokenCredential CreateCredential()
    {
        var credentialOptions = new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        };

        return new DefaultAzureCredential(credentialOptions);
    }

    private BlobContainerClient CreateBlobContainerClient(string storageBaseUrl, string optionName)
    {
        if (string.IsNullOrWhiteSpace(storageBaseUrl))
        {
            throw new InvalidOperationException(
                $"{AzureBlobProxyOptions.SectionName}:{optionName} must be configured.");
        }
        var containerUri = new Uri(storageBaseUrl.TrimEnd('/'), UriKind.Absolute);

        _logger.LogInformation("Using DefaultAzureCredential for Azure Blob container {ContainerUri}", containerUri);
        return new BlobContainerClient(containerUri, _credential);
    }

    private static string BuildBlobName(string prefix, string? blobPath)
    {
        var sanitizedPrefix = prefix.Trim('/');
        var sanitizedBlobPath = blobPath?.Trim('/');

        return string.IsNullOrWhiteSpace(sanitizedBlobPath)
            ? sanitizedPrefix
            : $"{sanitizedPrefix}/{sanitizedBlobPath}";
    }

    private static string TrimPrefix(string prefix, string blobName)
    {
        var sanitizedPrefix = prefix.Trim('/');
        if (blobName.StartsWith($"{sanitizedPrefix}/", StringComparison.OrdinalIgnoreCase))
        {
            return blobName[(sanitizedPrefix.Length + 1)..];
        }

        return blobName;
    }

    private static string NormalizeBlobPath(string blobPath)
    {
        var normalizedPath = blobPath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new ArgumentException("A blob path is required.", nameof(blobPath));
        }

        return normalizedPath;
    }

    private static IReadOnlyDictionary<string, string[]> ParseJwtClaims(string token)
    {
        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        var payload = segments[1]
            .Replace('-', '+')
            .Replace('_', '/');

        var remainder = payload.Length % 4;
        if (remainder > 0)
        {
            payload = payload.PadRight(payload.Length + (4 - remainder), '=');
        }

        var payloadBytes = Convert.FromBase64String(payload);
        using var document = JsonDocument.Parse(payloadBytes);
        var claims = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            claims[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.Array => property.Value.EnumerateArray()
                    .Select(element => element.ToString())
                    .ToArray(),
                _ => [property.Value.ToString()]
            };
        }

        return claims;
    }

    private string BuildUserGamesRootPrefix(string scopeKey)
        => BuildBlobName(_options.UserGamesPrefix, $"users/{scopeKey}/saves");

    private string BuildUserPreferencesRootPrefix(string scopeKey)
        => BuildBlobName(_options.UserPreferencesPrefix, $"users/{scopeKey}");

    private static string NormalizeGameId(string gameId)
    {
        var normalized = InvalidGameIdCharacters.Replace(gameId.Trim(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A valid gameId is required.", nameof(gameId));
        }

        return normalized;
    }

    private static string? NormalizeScopeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant()
            .Replace("live.com#", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('@', '_');

        normalized = InvalidGameIdCharacters.Replace(normalized, "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
