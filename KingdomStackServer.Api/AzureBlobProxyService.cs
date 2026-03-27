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
    private static readonly Regex InvalidStackStampIdCharacters = new("[^a-zA-Z0-9._-]+", RegexOptions.Compiled);
    private readonly AzureBlobProxyOptions _options;
    private readonly ILogger<AzureBlobProxyService> _logger;
    private readonly BlobContainerClient _blobContainerClient;
    private readonly BlobContainerClient _userGamesBlobContainerClient;
    private readonly BlobContainerClient _userPreferencesBlobContainerClient;
    private readonly BlobContainerClient _stackStampsBlobContainerClient;
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
        _stackStampsBlobContainerClient = CreateBlobContainerClient(
            string.IsNullOrWhiteSpace(_options.StackStampsStorageBaseUrl) ? _options.StorageBaseUrl : _options.StackStampsStorageBaseUrl,
            nameof(_options.StackStampsStorageBaseUrl));
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

    public async Task<AzureBlobContent?> GetSoundAssetAsync(
        string blobPath,
        CancellationToken cancellationToken)
        => await GetAssetAsync(_options.SoundsPrefix, blobPath, cancellationToken);

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

    public async Task<IReadOnlyList<TileAssetListItem>> ListSoundAssetsAsync(
        string? prefix,
        CancellationToken cancellationToken)
        => await ListAssetsAsync(_options.SoundsPrefix, prefix, cancellationToken);

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

    public async Task<UserStackStampListResult> ListUserStackStampsAsync(
        string scopeKey,
        string? prefix,
        string? search,
        string? tag,
        int? limit,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var normalizedPrefix = prefix?.Trim();
        var normalizedSearch = search?.Trim();
        var normalizedTag = tag?.Trim();
        var pageSize = Math.Clamp(limit ?? 50, 1, 200);
        var blobPrefix = string.IsNullOrWhiteSpace(normalizedPrefix)
            ? BuildUserStackStampsRootPrefix(scopeKey)
            : BuildBlobName(BuildUserStackStampsRootPrefix(scopeKey), normalizedPrefix);

        var items = new List<UserStackStampListItem>(pageSize);
        var pageable = _stackStampsBlobContainerClient.GetBlobsAsync(
            traits: BlobTraits.Metadata,
            states: BlobStates.None,
            prefix: blobPrefix,
            cancellationToken: cancellationToken);

        await foreach (var page in pageable.AsPages(continuationToken, pageSizeHint: pageSize))
        {
            foreach (var blobItem in page.Values)
            {
                if (!blobItem.Name.EndsWith("/definition.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var item = await CreateUserStackStampListItemAsync(blobItem, scopeKey, cancellationToken);
                if (item is null || !MatchesStackStampFilters(item, normalizedSearch, normalizedTag))
                {
                    continue;
                }

                items.Add(item);
            }

            if (items.Count >= pageSize)
            {
                return new UserStackStampListResult(items.Take(pageSize).ToArray(), page.ContinuationToken);
            }

            if (string.IsNullOrWhiteSpace(page.ContinuationToken))
            {
                return new UserStackStampListResult(items, null);
            }
        }

        return new UserStackStampListResult(items, null);
    }

    public async Task<bool> UserStackStampNameExistsAsync(
        string scopeKey,
        string name,
        string? excludeStackStampId,
        CancellationToken cancellationToken)
    {
        var rootPrefix = BuildUserStackStampsRootPrefix(scopeKey);
        var normalizedName = name.Trim();
        var normalizedExcludedId = string.IsNullOrWhiteSpace(excludeStackStampId)
            ? null
            : NormalizeStackStampId(excludeStackStampId);

        await foreach (var blobItem in _stackStampsBlobContainerClient.GetBlobsAsync(
                           traits: BlobTraits.Metadata,
                           states: BlobStates.None,
                           prefix: rootPrefix,
                           cancellationToken: cancellationToken))
        {
            if (!blobItem.Name.EndsWith("/definition.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var item = await CreateUserStackStampListItemAsync(blobItem, scopeKey, cancellationToken);
            if (item is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalizedExcludedId)
                && string.Equals(item.Id, normalizedExcludedId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(item.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<UserStackStampContent?> GetUserStackStampAsync(
        string scopeKey,
        string stackStampId,
        CancellationToken cancellationToken)
    {
        var normalizedStackStampId = NormalizeStackStampId(stackStampId);
        var relativeBlobPath = BuildStackStampDefinitionRelativePath(normalizedStackStampId);
        var blobName = BuildBlobName(BuildUserStackStampsRootPrefix(scopeKey), relativeBlobPath);
        var blobClient = _stackStampsBlobContainerClient.GetBlobClient(blobName);

        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var document = DeserializeStoredStackStampDocument(response.Value.Content.ToString());

            return new UserStackStampContent(
                document.Id,
                document.Name,
                document.Description,
                relativeBlobPath,
                document.HasPreview,
                document.SchemaVersion,
                document.Version,
                document.Tags,
                properties.Value.CreatedOn,
                properties.Value.LastModified,
                properties.Value.ETag.ToString(),
                document.Definition);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<UserStackStampSaveResult> SaveUserStackStampAsync(
        string scopeKey,
        string stackStampId,
        string name,
        string? description,
        IReadOnlyList<string>? tags,
        StackStampDefinitionDto definition,
        byte[]? previewBytes,
        string? previewContentType,
        string? ifMatchEtag,
        bool clearPreview,
        bool createOnly,
        CancellationToken cancellationToken)
    {
        var normalizedStackStampId = NormalizeStackStampId(stackStampId);
        var relativeBlobPath = BuildStackStampDefinitionRelativePath(normalizedStackStampId);
        var rootPrefix = BuildUserStackStampsRootPrefix(scopeKey);
        var blobName = BuildBlobName(rootPrefix, relativeBlobPath);
        var blobClient = _stackStampsBlobContainerClient.GetBlobClient(blobName);

        StoredStackStampDocument? existingDocument = null;
        if (!createOnly)
        {
            existingDocument = await GetStoredStackStampDocumentAsync(blobClient, cancellationToken);
            if (existingDocument is null)
            {
                throw new FileNotFoundException($"Stack stamp '{normalizedStackStampId}' was not found.");
            }
        }

        var requestConditions = BuildStackStampWriteConditions(ifMatchEtag, createOnly);
        var previewBlobClient = _stackStampsBlobContainerClient.GetBlobClient(
            BuildBlobName(rootPrefix, BuildStackStampPreviewRelativePath(normalizedStackStampId)));

        var finalHasPreview = existingDocument?.HasPreview ?? false;
        if (previewBytes is not null)
        {
            await using var previewStream = new MemoryStream(previewBytes);
            await previewBlobClient.UploadAsync(
                previewStream,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = string.IsNullOrWhiteSpace(previewContentType) ? "image/png" : previewContentType
                    }
                },
                cancellationToken);

            finalHasPreview = true;
        }
        else if (clearPreview)
        {
            await previewBlobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            finalHasPreview = false;
        }

        var document = BuildStoredStackStampDocument(
            normalizedStackStampId,
            name,
            description,
            NormalizeTags(tags),
            definition,
            existingDocument?.Version,
            finalHasPreview);

        var json = JsonSerializer.Serialize(document, JsonOptions);

        await using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
        {
            await blobClient.UploadAsync(
                stream,
                new BlobUploadOptions
                {
                    Conditions = requestConditions,
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = "application/json"
                    },
                    Metadata = BuildStackStampBlobMetadata(document)
                },
                cancellationToken);
        }

        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

        return new UserStackStampSaveResult(
            document.Id,
            document.Name,
            document.Description,
            relativeBlobPath,
            document.HasPreview,
            document.SchemaVersion,
            document.Version,
            document.Tags,
            properties.Value.CreatedOn,
            properties.Value.LastModified,
            properties.Value.ETag.ToString(),
            document.Definition);
    }

    public async Task<AzureBlobContent?> GetUserStackStampPreviewAsync(
        string scopeKey,
        string stackStampId,
        CancellationToken cancellationToken)
    {
        var normalizedStackStampId = NormalizeStackStampId(stackStampId);
        return await GetAssetAsync(
            _stackStampsBlobContainerClient,
            BuildUserStackStampsRootPrefix(scopeKey),
            BuildStackStampPreviewRelativePath(normalizedStackStampId),
            cancellationToken);
    }

    public async Task<bool> DeleteUserStackStampAsync(
        string scopeKey,
        string stackStampId,
        CancellationToken cancellationToken)
    {
        var normalizedStackStampId = NormalizeStackStampId(stackStampId);
        var rootPrefix = BuildUserStackStampsRootPrefix(scopeKey);
        var definitionBlobClient = _stackStampsBlobContainerClient.GetBlobClient(
            BuildBlobName(rootPrefix, BuildStackStampDefinitionRelativePath(normalizedStackStampId)));
        var previewBlobClient = _stackStampsBlobContainerClient.GetBlobClient(
            BuildBlobName(rootPrefix, BuildStackStampPreviewRelativePath(normalizedStackStampId)));

        var definitionDeleted = await definitionBlobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        var previewDeleted = await previewBlobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

        return definitionDeleted.Value || previewDeleted.Value;
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

    private async Task<UserStackStampListItem?> CreateUserStackStampListItemAsync(
        BlobItem blobItem,
        string scopeKey,
        CancellationToken cancellationToken)
    {
        var rootPrefix = BuildUserStackStampsRootPrefix(scopeKey);
        var relativeBlobPath = TrimPrefix(rootPrefix, blobItem.Name);
        var blobClient = _stackStampsBlobContainerClient.GetBlobClient(blobItem.Name);

        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
            var document = DeserializeStoredStackStampDocument(response.Value.Content.ToString());

            return new UserStackStampListItem(
                document.Id,
                document.Name,
                document.Description,
                relativeBlobPath,
                document.HasPreview,
                document.SchemaVersion,
                document.Version,
                document.Tags,
                document.Footprint,
                document.EntryCount,
                document.TileReferences,
                document.CharacterReferences,
                blobItem.Properties.CreatedOn,
                blobItem.Properties.LastModified,
                blobItem.Properties.ETag?.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static bool MatchesStackStampFilters(
        UserStackStampListItem item,
        string? search,
        string? tag)
    {
        var matchesSearch = string.IsNullOrWhiteSpace(search)
            || item.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(item.Description)
                && item.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

        var matchesTag = string.IsNullOrWhiteSpace(tag)
            || item.Tags.Any(existingTag => string.Equals(existingTag, tag, StringComparison.OrdinalIgnoreCase));

        return matchesSearch && matchesTag;
    }

    private async Task<StoredStackStampDocument?> GetStoredStackStampDocumentAsync(
        BlobClient blobClient,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
            return DeserializeStoredStackStampDocument(response.Value.Content.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static StoredStackStampDocument DeserializeStoredStackStampDocument(string json)
    {
        var document = JsonSerializer.Deserialize<StoredStackStampDocument>(json, JsonOptions);
        if (document is null)
        {
            throw new InvalidOperationException("Stack stamp document could not be parsed.");
        }

        return document;
    }

    private static BlobRequestConditions BuildStackStampWriteConditions(string? ifMatchEtag, bool createOnly)
    {
        if (createOnly)
        {
            return new BlobRequestConditions
            {
                IfNoneMatch = ETag.All
            };
        }

        if (string.IsNullOrWhiteSpace(ifMatchEtag))
        {
            throw new ArgumentException("An etag is required for updates.", nameof(ifMatchEtag));
        }

        return new BlobRequestConditions
        {
            IfMatch = new ETag(ifMatchEtag)
        };
    }

    private static Dictionary<string, string> BuildStackStampBlobMetadata(StoredStackStampDocument document)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = document.Id,
            ["name"] = document.Name,
            ["schemaversion"] = document.SchemaVersion.ToString(),
            ["version"] = document.Version.ToString(),
            ["entrycount"] = document.EntryCount.ToString(),
            ["haspreview"] = document.HasPreview ? "true" : "false"
        };
    }

    private static StoredStackStampDocument BuildStoredStackStampDocument(
        string id,
        string name,
        string? description,
        string[] tags,
        StackStampDefinitionDto definition,
        int? currentVersion,
        bool hasPreview)
    {
        var tileReferences = definition.Entries
            .Where(entry => string.Equals(entry.EntityType, "tile", StringComparison.OrdinalIgnoreCase))
            .Select(entry => TryGetPayloadString(entry.Payload, "tileId"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var characterReferences = definition.Entries
            .Where(entry => string.Equals(entry.EntityType, "character", StringComparison.OrdinalIgnoreCase))
            .Select(entry => TryGetPayloadString(entry.Payload, "characterId"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var footprint = BuildFootprint(definition);

        return new StoredStackStampDocument(
            id,
            name,
            description,
            definition.SchemaVersion,
            (currentVersion ?? 0) + 1,
            tags,
            definition.Entries.Length,
            tileReferences,
            characterReferences,
            footprint,
            hasPreview,
            definition);
    }

    private static StackStampFootprintResponse BuildFootprint(StackStampDefinitionDto definition)
    {
        var minDx = definition.Entries.Min(entry => entry.Dx);
        var minDy = definition.Entries.Min(entry => entry.Dy);
        var maxDx = definition.Entries.Max(entry => entry.Dx);
        var maxDy = definition.Entries.Max(entry => entry.Dy);

        return new StackStampFootprintResponse(
            minDx,
            minDy,
            maxDx,
            maxDy,
            maxDx - minDx + 1,
            maxDy - minDy + 1);
    }

    private static string? TryGetPayloadString(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private string BuildUserGamesRootPrefix(string scopeKey)
        => BuildBlobName(_options.UserGamesPrefix, $"users/{scopeKey}/saves");

    private string BuildUserPreferencesRootPrefix(string scopeKey)
        => BuildBlobName(_options.UserPreferencesPrefix, $"users/{scopeKey}");

    private string BuildUserStackStampsRootPrefix(string scopeKey)
        => BuildBlobName(_options.StackStampsPrefix, $"users/{scopeKey}/stack-stamps");

    private static string BuildStackStampDefinitionRelativePath(string stackStampId)
        => $"{stackStampId}/definition.json";

    private static string BuildStackStampPreviewRelativePath(string stackStampId)
        => $"{stackStampId}/preview.png";

    private static string NormalizeGameId(string gameId)
    {
        var normalized = InvalidGameIdCharacters.Replace(gameId.Trim(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A valid gameId is required.", nameof(gameId));
        }

        return normalized;
    }

    public static string NormalizeStackStampId(string stackStampId)
    {
        var normalized = InvalidStackStampIdCharacters.Replace(stackStampId.Trim(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A valid stackStampId is required.", nameof(stackStampId));
        }

        return normalized;
    }

    private static string[] NormalizeTags(IReadOnlyList<string>? tags)
    {
        return (tags ?? [])
            .Select(tag => tag?.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
