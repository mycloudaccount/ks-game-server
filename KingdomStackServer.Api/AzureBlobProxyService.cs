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
    private const string StoredFormatCanonical = "canonical";
    private const string StoredFormatLegacy = "legacy";
    private const string StoredSchemaKsGame = "ks.game";
    private const string ParticleEffectTypeFootTrail = "footTrail";
    private const string ParticleEffectTypeLandingImpact = "landingImpact";
    private static readonly Regex InvalidGameIdCharacters = new("[^a-zA-Z0-9._-]+", RegexOptions.Compiled);
    private static readonly Regex InvalidStackStampIdCharacters = new("[^a-zA-Z0-9._-]+", RegexOptions.Compiled);
    private static readonly Regex InvalidParticleEffectIdCharacters = new("[^a-zA-Z0-9._-]+", RegexOptions.Compiled);
    private readonly AzureBlobProxyOptions _options;
    private readonly ILogger<AzureBlobProxyService> _logger;
    private readonly BlobContainerClient _blobContainerClient;
    private readonly BlobContainerClient _userGamesBlobContainerClient;
    private readonly BlobContainerClient _userPreferencesBlobContainerClient;
    private readonly BlobContainerClient _clientMessagesBlobContainerClient;
    private readonly BlobContainerClient _stackStampsBlobContainerClient;
    private readonly BlobContainerClient _particleEffectsBlobContainerClient;
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
        _clientMessagesBlobContainerClient = CreateBlobContainerClient(
            string.IsNullOrWhiteSpace(_options.ClientMessagesStorageBaseUrl) ? _options.StorageBaseUrl : _options.ClientMessagesStorageBaseUrl,
            nameof(_options.ClientMessagesStorageBaseUrl));
        _stackStampsBlobContainerClient = CreateBlobContainerClient(
            string.IsNullOrWhiteSpace(_options.StackStampsStorageBaseUrl) ? _options.StorageBaseUrl : _options.StackStampsStorageBaseUrl,
            nameof(_options.StackStampsStorageBaseUrl));
        _particleEffectsBlobContainerClient = CreateBlobContainerClient(
            string.IsNullOrWhiteSpace(_options.ParticleEffectsStorageBaseUrl) ? _options.StorageBaseUrl : _options.ParticleEffectsStorageBaseUrl,
            nameof(_options.ParticleEffectsStorageBaseUrl));
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
            blobItem.Metadata.TryGetValue("format", out var storedFormat);
            blobItem.Metadata.TryGetValue("schema", out var storedSchema);
            blobItem.Metadata.TryGetValue("schemaversion", out var storedVersionRaw);
            var storedVersion = TryParseNullableInt(storedVersionRaw);

            items.Add(new UserGameMetadataResponse(
                string.IsNullOrWhiteSpace(gameId) ? defaultGameId : gameId,
                string.IsNullOrWhiteSpace(name) ? defaultGameId : name,
                relativeBlobPath,
                string.Empty,
                string.Empty,
                blobItem.Properties.CreatedOn,
                blobItem.Properties.LastModified,
                blobItem.Properties.ContentLength,
                string.IsNullOrWhiteSpace(storedFormat) ? null : storedFormat,
                string.IsNullOrWhiteSpace(storedSchema) ? null : storedSchema,
                storedVersion));
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
        string storedFormat,
        string? storedSchema,
        int? storedVersion,
        CancellationToken cancellationToken)
    {
        var normalizedGameId = NormalizeGameId(gameId);
        var relativeBlobPath = $"{normalizedGameId}.json";
        var blobName = BuildBlobName(BuildUserGamesRootPrefix(scopeKey), relativeBlobPath);
        var blobClient = _userGamesBlobContainerClient.GetBlobClient(blobName);
        var metadata = BuildGameBlobMetadata(
            normalizedGameId,
            name,
            storedFormat,
            storedSchema,
            storedVersion);

        try
        {
            _logger.LogInformation(
                "Saving user game to blob storage. ScopeKey={ScopeKey}, GameId={GameId}, BlobName={BlobName}, Container={ContainerUri}, Credential={CredentialType}, StoredFormat={StoredFormat}, StoredSchema={StoredSchema}, StoredVersion={StoredVersion}",
                scopeKey,
                normalizedGameId,
                blobName,
                _userGamesBlobContainerClient.Uri,
                _credential.GetType().Name,
                storedFormat,
                storedSchema,
                storedVersion);

            await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));
            await blobClient.UploadAsync(
                stream,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = "application/json"
                    },
                    Metadata = metadata
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
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "Failed to save user game blob. ScopeKey={ScopeKey}, GameId={GameId}, BlobName={BlobName}, BlobUri={BlobUri}, Container={ContainerUri}, Credential={CredentialType}, Status={Status}, ErrorCode={ErrorCode}",
                scopeKey,
                normalizedGameId,
                blobName,
                blobClient.Uri,
                _userGamesBlobContainerClient.Uri,
                _credential.GetType().Name,
                ex.Status,
                ex.ErrorCode);
            throw;
        }
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

    public async Task<ClientMessagesResponse> GetClientMessagesAsync(
        StorageUserContext userContext,
        CancellationToken cancellationToken)
    {
        var document = await GetClientMessagesDocumentAsync(cancellationToken);
        var state = await GetClientMessagesStateAsync(userContext.ScopeKey, cancellationToken);
        var messages = document.Messages
            .Where(message => MessageMatchesUser(message, userContext))
            .Select(message =>
            {
                state.TryGetValue(message.Id, out var userState);
                return message with
                {
                    Read = userState?.ReadAt is not null,
                    Dismissed = userState?.DismissedAt is not null,
                    ReadAt = userState?.ReadAt,
                    DismissedAt = userState?.DismissedAt
                };
            })
            .ToArray();

        return document with { Messages = messages };
    }

    public async Task MarkClientMessageReadAsync(
        string scopeKey,
        string messageId,
        CancellationToken cancellationToken)
    {
        await UpdateClientMessageStateAsync(
            scopeKey,
            messageId,
            readAt: DateTimeOffset.UtcNow,
            dismissedAt: null,
            markDismissed: false,
            cancellationToken);
    }

    public async Task DismissClientMessageAsync(
        string scopeKey,
        string messageId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await UpdateClientMessageStateAsync(
            scopeKey,
            messageId,
            readAt: now,
            dismissedAt: now,
            markDismissed: true,
            cancellationToken);
    }

    public async Task MarkAllClientMessagesReadAsync(
        StorageUserContext userContext,
        CancellationToken cancellationToken)
    {
        var document = await GetClientMessagesDocumentAsync(cancellationToken);
        var state = await GetClientMessagesStateAsync(userContext.ScopeKey, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var message in document.Messages.Where(message => MessageMatchesUser(message, userContext)))
        {
            state.TryGetValue(message.Id, out var existingState);
            state[message.Id] = new ClientMessageUserState(
                existingState?.ReadAt ?? now,
                existingState?.DismissedAt);
        }

        await SaveClientMessagesStateAsync(userContext.ScopeKey, state, cancellationToken);
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
            var payload = document.RootElement.Clone();

            properties.Value.Metadata.TryGetValue("name", out var name);
            properties.Value.Metadata.TryGetValue("format", out var storedFormatRaw);
            properties.Value.Metadata.TryGetValue("schema", out var storedSchemaRaw);
            properties.Value.Metadata.TryGetValue("schemaversion", out var storedVersionRaw);
            var resolvedFormat = ResolveStoredGameFormat(storedFormatRaw, payload);
            var resolvedSchema = string.IsNullOrWhiteSpace(storedSchemaRaw)
                ? (resolvedFormat == StoredFormatCanonical ? StoredSchemaKsGame : null)
                : storedSchemaRaw;
            var resolvedVersion = TryParseNullableInt(storedVersionRaw);
            var asDocument = resolvedFormat == StoredFormatCanonical ? payload : (JsonElement?)null;
            var asGame = resolvedFormat == StoredFormatLegacy ? payload : (JsonElement?)null;

            return new UserGameContentResponse(
                normalizedGameId,
                string.IsNullOrWhiteSpace(name) ? normalizedGameId : name,
                relativeBlobPath,
                properties.Value.ContentLength,
                properties.Value.CreatedOn,
                string.Empty,
                string.Empty,
                properties.Value.LastModified,
                resolvedFormat,
                resolvedSchema,
                resolvedVersion,
                asDocument,
                asGame);
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

    public async Task<UserParticleEffectListResult> ListUserFootTrailParticleEffectsAsync(
        string scopeKey,
        string? prefix,
        int? limit,
        string? continuationToken,
        CancellationToken cancellationToken)
        => await ListUserParticleEffectsAsync(
            scopeKey,
            ParticleEffectTypeFootTrail,
            prefix,
            limit,
            continuationToken,
            cancellationToken);

    public async Task<UserParticleEffectListResult> ListUserLandingImpactParticleEffectsAsync(
        string scopeKey,
        string? prefix,
        int? limit,
        string? continuationToken,
        CancellationToken cancellationToken)
        => await ListUserParticleEffectsAsync(
            scopeKey,
            ParticleEffectTypeLandingImpact,
            prefix,
            limit,
            continuationToken,
            cancellationToken);

    public async Task<UserParticleEffectListResult> ListUserParticleEffectsAsync(
        string scopeKey,
        string effectType,
        string? prefix,
        int? limit,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var normalizedPrefix = prefix?.Trim();
        var pageSize = Math.Clamp(limit ?? 50, 1, 200);
        var rootPrefix = BuildUserParticleEffectsRootPrefix(scopeKey, effectType);
        var blobPrefix = string.IsNullOrWhiteSpace(normalizedPrefix)
            ? rootPrefix
            : BuildBlobName(rootPrefix, normalizedPrefix);
        var items = new List<UserParticleEffectListItem>(pageSize);
        var pageable = _particleEffectsBlobContainerClient.GetBlobsAsync(
            traits: BlobTraits.Metadata,
            states: BlobStates.None,
            prefix: blobPrefix,
            cancellationToken: cancellationToken);

        await foreach (var page in pageable.AsPages(continuationToken, pageSizeHint: pageSize))
        {
            foreach (var blobItem in page.Values)
            {
                if (!blobItem.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var item = await CreateUserParticleEffectListItemAsync(blobItem, scopeKey, effectType, cancellationToken);
                if (item is null)
                {
                    continue;
                }

                items.Add(item);
            }

            if (items.Count >= pageSize)
            {
                return new UserParticleEffectListResult(items.Take(pageSize).ToArray(), page.ContinuationToken);
            }

            if (string.IsNullOrWhiteSpace(page.ContinuationToken))
            {
                return new UserParticleEffectListResult(items, null);
            }
        }

        return new UserParticleEffectListResult(items, null);
    }

    public async Task<UserParticleEffectContent?> GetUserFootTrailParticleEffectAsync(
        string scopeKey,
        string particleEffectId,
        CancellationToken cancellationToken)
        => await GetUserParticleEffectAsync(
            scopeKey,
            ParticleEffectTypeFootTrail,
            particleEffectId,
            cancellationToken);

    public async Task<UserParticleEffectContent?> GetUserLandingImpactParticleEffectAsync(
        string scopeKey,
        string particleEffectId,
        CancellationToken cancellationToken)
        => await GetUserParticleEffectAsync(
            scopeKey,
            ParticleEffectTypeLandingImpact,
            particleEffectId,
            cancellationToken);

    public async Task<UserParticleEffectContent?> GetUserParticleEffectAsync(
        string scopeKey,
        string effectType,
        string particleEffectId,
        CancellationToken cancellationToken)
    {
        var normalizedParticleEffectId = NormalizeParticleEffectId(particleEffectId);
        var relativeBlobPath = BuildParticleEffectRelativePath(normalizedParticleEffectId);
        var blobName = BuildBlobName(BuildUserParticleEffectsRootPrefix(scopeKey, effectType), relativeBlobPath);
        var blobClient = _particleEffectsBlobContainerClient.GetBlobClient(blobName);

        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var document = DeserializeStoredParticleEffectDocument(response.Value.Content.ToString());

            return new UserParticleEffectContent(
                document.Id,
                document.Name,
                document.Type,
                relativeBlobPath,
                document.SchemaVersion,
                document.Version,
                properties.Value.CreatedOn,
                properties.Value.LastModified,
                properties.Value.ETag.ToString(),
                document.Effect);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<UserParticleEffectSaveResult> SaveUserFootTrailParticleEffectAsync(
        string scopeKey,
        string particleEffectId,
        string name,
        JsonElement effect,
        string? ifMatchEtag,
        bool createOnly,
        CancellationToken cancellationToken)
        => await SaveUserParticleEffectAsync(
            scopeKey,
            ParticleEffectTypeFootTrail,
            particleEffectId,
            name,
            effect,
            ifMatchEtag,
            createOnly,
            cancellationToken);

    public async Task<UserParticleEffectSaveResult> SaveUserLandingImpactParticleEffectAsync(
        string scopeKey,
        string particleEffectId,
        string name,
        JsonElement effect,
        string? ifMatchEtag,
        bool createOnly,
        CancellationToken cancellationToken)
        => await SaveUserParticleEffectAsync(
            scopeKey,
            ParticleEffectTypeLandingImpact,
            particleEffectId,
            name,
            effect,
            ifMatchEtag,
            createOnly,
            cancellationToken);

    public async Task<UserParticleEffectSaveResult> SaveUserParticleEffectAsync(
        string scopeKey,
        string effectType,
        string particleEffectId,
        string name,
        JsonElement effect,
        string? ifMatchEtag,
        bool createOnly,
        CancellationToken cancellationToken)
    {
        var normalizedParticleEffectId = NormalizeParticleEffectId(particleEffectId);
        var relativeBlobPath = BuildParticleEffectRelativePath(normalizedParticleEffectId);
        var blobName = BuildBlobName(BuildUserParticleEffectsRootPrefix(scopeKey, effectType), relativeBlobPath);
        var blobClient = _particleEffectsBlobContainerClient.GetBlobClient(blobName);

        StoredParticleEffectDocument? existingDocument = null;
        if (!createOnly)
        {
            existingDocument = await GetStoredParticleEffectDocumentAsync(blobClient, cancellationToken);
            if (existingDocument is null)
            {
                throw new FileNotFoundException($"Particle effect '{normalizedParticleEffectId}' was not found.");
            }
        }

        var document = new StoredParticleEffectDocument(
            normalizedParticleEffectId,
            name,
            effectType,
            1,
            (existingDocument?.Version ?? 0) + 1,
            effect);
        var json = JsonSerializer.Serialize(document, JsonOptions);

        await using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
        {
            await blobClient.UploadAsync(
                stream,
                new BlobUploadOptions
                {
                    Conditions = BuildJsonAssetWriteConditions(ifMatchEtag, createOnly),
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = "application/json"
                    },
                    Metadata = BuildParticleEffectBlobMetadata(document)
                },
                cancellationToken);
        }

        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

        return new UserParticleEffectSaveResult(
            document.Id,
            document.Name,
            document.Type,
            relativeBlobPath,
            document.SchemaVersion,
            document.Version,
            properties.Value.CreatedOn,
            properties.Value.LastModified,
            properties.Value.ETag.ToString(),
            document.Effect);
    }

    public async Task<bool> DeleteUserFootTrailParticleEffectAsync(
        string scopeKey,
        string particleEffectId,
        CancellationToken cancellationToken)
        => await DeleteUserParticleEffectAsync(
            scopeKey,
            ParticleEffectTypeFootTrail,
            particleEffectId,
            cancellationToken);

    public async Task<bool> DeleteUserLandingImpactParticleEffectAsync(
        string scopeKey,
        string particleEffectId,
        CancellationToken cancellationToken)
        => await DeleteUserParticleEffectAsync(
            scopeKey,
            ParticleEffectTypeLandingImpact,
            particleEffectId,
            cancellationToken);

    public async Task<bool> DeleteUserParticleEffectAsync(
        string scopeKey,
        string effectType,
        string particleEffectId,
        CancellationToken cancellationToken)
    {
        var normalizedParticleEffectId = NormalizeParticleEffectId(particleEffectId);
        var blobName = BuildBlobName(
            BuildUserParticleEffectsRootPrefix(scopeKey, effectType),
            BuildParticleEffectRelativePath(normalizedParticleEffectId));
        var blobClient = _particleEffectsBlobContainerClient.GetBlobClient(blobName);
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

    private async Task<ClientMessagesResponse> GetClientMessagesDocumentAsync(CancellationToken cancellationToken)
    {
        var blobName = _options.ClientMessagesBlobName.Trim('/');
        var blobClient = _clientMessagesBlobContainerClient.GetBlobClient(blobName);

        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken);
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            using var document = JsonDocument.Parse(response.Value.Content.ToString());
            var root = document.RootElement;
            var messages = root.TryGetProperty("messages", out var messagesElement) &&
                           messagesElement.ValueKind == JsonValueKind.Array
                ? messagesElement
                    .EnumerateArray()
                    .Select(ParseClientMessage)
                    .Where(message => message is not null)
                    .Select(message => message!)
                    .ToArray()
                : [];

            return new ClientMessagesResponse(
                GetInt32Property(root, "version", 1),
                GetStringProperty(root, "updatedAt") ?? DateTimeOffset.UnixEpoch.ToString("O"),
                messages,
                properties.Value.ETag.ToString(),
                properties.Value.LastModified);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new ClientMessagesResponse(
                1,
                DateTimeOffset.UnixEpoch.ToString("O"),
                [],
                null,
                null);
        }
    }

    private async Task<Dictionary<string, ClientMessageUserState>> GetClientMessagesStateAsync(
        string scopeKey,
        CancellationToken cancellationToken)
    {
        var blobClient = _userPreferencesBlobContainerClient.GetBlobClient(
            BuildBlobName(BuildUserPreferencesRootPrefix(scopeKey), "messages-state.json"));

        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken);
            using var document = JsonDocument.Parse(response.Value.Content.ToString());
            var root = document.RootElement;
            var result = new Dictionary<string, ClientMessageUserState>(StringComparer.OrdinalIgnoreCase);

            if (!root.TryGetProperty("messages", out var messagesElement) ||
                messagesElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in messagesElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                result[property.Name] = new ClientMessageUserState(
                    GetDateTimeOffsetProperty(property.Value, "readAt"),
                    GetDateTimeOffsetProperty(property.Value, "dismissedAt"));
            }

            return result;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new Dictionary<string, ClientMessageUserState>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task UpdateClientMessageStateAsync(
        string scopeKey,
        string messageId,
        DateTimeOffset? readAt,
        DateTimeOffset? dismissedAt,
        bool markDismissed,
        CancellationToken cancellationToken)
    {
        var state = await GetClientMessagesStateAsync(scopeKey, cancellationToken);
        state.TryGetValue(messageId, out var existingState);
        state[messageId] = new ClientMessageUserState(
            existingState?.ReadAt ?? readAt,
            markDismissed ? (existingState?.DismissedAt ?? dismissedAt) : existingState?.DismissedAt);

        await SaveClientMessagesStateAsync(scopeKey, state, cancellationToken);
    }

    private async Task SaveClientMessagesStateAsync(
        string scopeKey,
        IReadOnlyDictionary<string, ClientMessageUserState> state,
        CancellationToken cancellationToken)
    {
        var blobClient = _userPreferencesBlobContainerClient.GetBlobClient(
            BuildBlobName(BuildUserPreferencesRootPrefix(scopeKey), "messages-state.json"));
        var payload = new
        {
            version = 1,
            updatedAt = DateTimeOffset.UtcNow,
            messages = state
        };
        var jsonContent = JsonSerializer.Serialize(payload, JsonOptions);

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
        await blobClient.SetHttpHeadersAsync(
            new BlobHttpHeaders
            {
                ContentType = "application/json"
            },
            cancellationToken: cancellationToken);
    }

    private static ClientMessageResponse? ParseClientMessage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = GetStringProperty(element, "id");
        var title = GetStringProperty(element, "title");
        var body = GetStringProperty(element, "body");
        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return new ClientMessageResponse(
            id,
            DescribeAudience(element),
            title,
            body,
            GetStringProperty(element, "kind") ?? "news",
            GetStringProperty(element, "severity") ?? "info",
            GetStringProperty(element, "startsAt") ?? DateTimeOffset.UnixEpoch.ToString("O"),
            GetStringProperty(element, "expiresAt"),
            GetBooleanProperty(element, "dismissible", true),
            ParseClientMessageAction(element),
            GetStringProperty(element, "createdAt") ?? DateTimeOffset.UnixEpoch.ToString("O"),
            false,
            false,
            null,
            null);
    }

    private static ClientMessageActionResponse? ParseClientMessageAction(JsonElement element)
    {
        if (!element.TryGetProperty("action", out var actionElement) ||
            actionElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var label = GetStringProperty(actionElement, "label");
        return string.IsNullOrWhiteSpace(label)
            ? null
            : new ClientMessageActionResponse(
                label,
                GetStringProperty(actionElement, "url"),
                GetStringProperty(actionElement, "route"));
    }

    private static bool MessageMatchesUser(ClientMessageResponse message, StorageUserContext userContext)
    {
        if (message.Audience.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return message.Audience
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => StringMatchesUser(value, userContext));
    }

    private static bool MessageMatchesUser(JsonElement messageElement, StorageUserContext userContext)
    {
        if (!messageElement.TryGetProperty("audience", out var audienceElement))
        {
            return true;
        }

        if (audienceElement.ValueKind == JsonValueKind.String)
        {
            var audience = audienceElement.GetString();
            return string.IsNullOrWhiteSpace(audience) ||
                   audience.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                   StringMatchesUser(audience, userContext);
        }

        if (audienceElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return AudienceArrayMatchesUser(audienceElement, "users", userContext) ||
               AudienceArrayMatchesUser(audienceElement, "userIds", userContext) ||
               AudienceArrayMatchesUser(audienceElement, "emails", userContext);
    }

    private static bool MessageMatchesUser(ClientMessageResponse message, JsonElement messageElement, StorageUserContext userContext)
        => message.Audience.Equals("all", StringComparison.OrdinalIgnoreCase) ||
           MessageMatchesUser(messageElement, userContext);

    private static bool AudienceArrayMatchesUser(JsonElement audienceElement, string propertyName, StorageUserContext userContext)
    {
        if (!audienceElement.TryGetProperty(propertyName, out var valuesElement) ||
            valuesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return valuesElement.EnumerateArray().Any(value =>
            value.ValueKind == JsonValueKind.String &&
            StringMatchesUser(value.GetString(), userContext));
    }

    private static bool StringMatchesUser(string? value, StorageUserContext userContext)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Equals(userContext.ScopeKey, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(userContext.ObjectId) &&
             value.Equals(userContext.ObjectId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(userContext.Email) &&
             value.Equals(userContext.Email, StringComparison.OrdinalIgnoreCase)));

    private static string DescribeAudience(JsonElement element)
    {
        if (!element.TryGetProperty("audience", out var audienceElement))
        {
            return "all";
        }

        return audienceElement.ValueKind == JsonValueKind.String
            ? audienceElement.GetString() ?? "all"
            : string.Join(",", EnumerateAudienceValues(audienceElement));
    }

    private static IEnumerable<string> EnumerateAudienceValues(JsonElement audienceElement)
    {
        if (audienceElement.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var propertyName in new[] { "users", "userIds", "emails" })
        {
            if (!audienceElement.TryGetProperty(propertyName, out var valuesElement) ||
                valuesElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var value in valuesElement.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    yield return value.GetString()!;
                }
            }
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static int GetInt32Property(JsonElement element, string propertyName, int fallback)
        => element.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var result)
            ? result
            : fallback;

    private static bool GetBooleanProperty(JsonElement element, string propertyName, bool fallback)
        => element.TryGetProperty(propertyName, out var value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static DateTimeOffset? GetDateTimeOffsetProperty(JsonElement element, string propertyName)
    {
        var value = GetStringProperty(element, propertyName);
        return DateTimeOffset.TryParse(value, out var result) ? result : null;
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

    private async Task<UserParticleEffectListItem?> CreateUserParticleEffectListItemAsync(
        BlobItem blobItem,
        string scopeKey,
        string effectType,
        CancellationToken cancellationToken)
    {
        var rootPrefix = BuildUserParticleEffectsRootPrefix(scopeKey, effectType);
        var relativeBlobPath = TrimPrefix(rootPrefix, blobItem.Name);
        var blobClient = _particleEffectsBlobContainerClient.GetBlobClient(blobItem.Name);

        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
            var document = DeserializeStoredParticleEffectDocument(response.Value.Content.ToString());

            return new UserParticleEffectListItem(
                document.Id,
                document.Name,
                document.Type,
                relativeBlobPath,
                document.SchemaVersion,
                document.Version,
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

    private async Task<StoredParticleEffectDocument?> GetStoredParticleEffectDocumentAsync(
        BlobClient blobClient,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken: cancellationToken);
            return DeserializeStoredParticleEffectDocument(response.Value.Content.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static StoredParticleEffectDocument DeserializeStoredParticleEffectDocument(string json)
    {
        var document = JsonSerializer.Deserialize<StoredParticleEffectDocument>(json, JsonOptions);
        if (document is null)
        {
            throw new InvalidOperationException("Particle effect document could not be parsed.");
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

    private static Dictionary<string, string> BuildParticleEffectBlobMetadata(StoredParticleEffectDocument document)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = document.Id,
            ["name"] = document.Name,
            ["type"] = document.Type,
            ["schemaversion"] = document.SchemaVersion.ToString(),
            ["version"] = document.Version.ToString()
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

    private string BuildUserParticleEffectsRootPrefix(string scopeKey, string effectType)
        => BuildBlobName(_options.ParticleEffectsPrefix, $"users/{scopeKey}/{ToParticleEffectPathSegment(effectType)}");

    private static string BuildStackStampDefinitionRelativePath(string stackStampId)
        => $"{stackStampId}/definition.json";

    private static string BuildStackStampPreviewRelativePath(string stackStampId)
        => $"{stackStampId}/preview.png";

    private static string BuildParticleEffectRelativePath(string particleEffectId)
        => $"{particleEffectId}.json";

    private static string NormalizeGameId(string gameId)
    {
        var normalized = InvalidGameIdCharacters.Replace(gameId.Trim(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A valid gameId is required.", nameof(gameId));
        }

        return normalized;
    }

    private static int? TryParseNullableInt(string? value)
    {
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static Dictionary<string, string> BuildGameBlobMetadata(
        string gameId,
        string name,
        string storedFormat,
        string? storedSchema,
        int? storedVersion)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["gameid"] = gameId,
            ["name"] = name,
            ["format"] = storedFormat
        };

        if (!string.IsNullOrWhiteSpace(storedSchema))
        {
            metadata["schema"] = storedSchema;
        }

        if (storedVersion.HasValue)
        {
            metadata["schemaversion"] = storedVersion.Value.ToString();
        }

        return metadata;
    }

    private static string ResolveStoredGameFormat(string? metadataFormat, JsonElement payload)
    {
        if (string.Equals(metadataFormat, StoredFormatCanonical, StringComparison.OrdinalIgnoreCase))
        {
            return StoredFormatCanonical;
        }

        if (string.Equals(metadataFormat, StoredFormatLegacy, StringComparison.OrdinalIgnoreCase))
        {
            return StoredFormatLegacy;
        }

        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("document", out var documentNode)
            && documentNode.ValueKind == JsonValueKind.Object
            && documentNode.TryGetProperty("schema", out var schemaNode)
            && schemaNode.ValueKind == JsonValueKind.String
            && string.Equals(schemaNode.GetString(), StoredSchemaKsGame, StringComparison.Ordinal))
        {
            return StoredFormatCanonical;
        }

        return StoredFormatLegacy;
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

    public static string NormalizeParticleEffectId(string particleEffectId)
    {
        var normalized = InvalidParticleEffectIdCharacters.Replace(particleEffectId.Trim(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A valid particleEffectId is required.", nameof(particleEffectId));
        }

        return normalized;
    }

    private static string ToParticleEffectPathSegment(string effectType)
        => effectType switch
        {
            _ when string.Equals(effectType, ParticleEffectTypeFootTrail, StringComparison.OrdinalIgnoreCase) => "foot-trails",
            _ when string.Equals(effectType, ParticleEffectTypeLandingImpact, StringComparison.OrdinalIgnoreCase) => "landing-impacts",
            _ => NormalizeParticleEffectId(effectType)
        };

    private static BlobRequestConditions BuildJsonAssetWriteConditions(string? ifMatchEtag, bool createOnly)
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




