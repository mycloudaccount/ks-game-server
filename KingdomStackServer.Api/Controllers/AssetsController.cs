using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace KingdomStackServer.Api.Controllers;

[ApiController]
[Route("api/assets/tiles")]
public class AssetsController : ControllerBase
{
    private const string StoredFormatCanonical = "canonical";
    private const string StoredFormatLegacy = "legacy";
    private const string StoredSchemaKsGame = "ks.game";
    private readonly AzureBlobProxyService _azureBlobProxyService;
    private readonly AzureBlobProxyOptions _options;

    public AssetsController(
        AzureBlobProxyService azureBlobProxyService,
        IOptions<AzureBlobProxyOptions> options)
    {
        _azureBlobProxyService = azureBlobProxyService;
        _options = options.Value;
    }

    [HttpGet("list")]
    public async Task<IActionResult> ListTileAssets(
        [FromQuery] string? prefix,
        CancellationToken cancellationToken)
    {
        var items = await _azureBlobProxyService.ListTileAssetsAsync(prefix, cancellationToken);
        var normalizedPrefix = prefix?.Trim('/');

        var response = new TileAssetListResponse(
            normalizedPrefix ?? string.Empty,
            items.Count,
            items.Select(item => new TileAssetListResponseItem(
                Path.GetFileNameWithoutExtension(item.BlobPath),
                Path.GetFileName(item.BlobPath),
                item.BlobPath,
                $"{Request.Scheme}://{Request.Host}/api/assets/tiles/{item.BlobPath}",
                item.ContentLength,
                item.LastModified))
                .ToArray());

        return Ok(response);
    }

    [HttpGet("/api/assets/characters/list")]
    public async Task<IActionResult> ListCharacterAssets(
        [FromQuery] string? prefix,
        CancellationToken cancellationToken)
    {
        var items = await _azureBlobProxyService.ListCharacterAssetsAsync(prefix, cancellationToken);
        var normalizedPrefix = prefix?.Trim('/');

        var response = new TileAssetListResponse(
            normalizedPrefix ?? string.Empty,
            items.Count,
            items.Select(item => new TileAssetListResponseItem(
                Path.GetFileNameWithoutExtension(item.BlobPath),
                Path.GetFileName(item.BlobPath),
                item.BlobPath,
                $"{Request.Scheme}://{Request.Host}/api/assets/characters/{item.BlobPath}",
                item.ContentLength,
                item.LastModified))
                .ToArray());

        return Ok(response);
    }

    [HttpGet("/api/assets/character-source-packs/list")]
    public async Task<IActionResult> ListCharacterSourcePackAssets(
        [FromQuery] string? prefix,
        CancellationToken cancellationToken)
    {
        var items = await _azureBlobProxyService.ListCharacterSourcePackAssetsAsync(prefix, cancellationToken);
        var normalizedPrefix = prefix?.Trim('/');
        var packItems = items
            .Where(item => item.BlobPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var response = new TileAssetListResponse(
            normalizedPrefix ?? string.Empty,
            packItems.Length,
            packItems.Select(item => new TileAssetListResponseItem(
                Path.GetFileNameWithoutExtension(item.BlobPath),
                Path.GetFileName(item.BlobPath),
                item.BlobPath,
                $"{Request.Scheme}://{Request.Host}/api/assets/character-source-packs/{item.BlobPath}",
                item.ContentLength,
                item.LastModified))
                .ToArray());

        return Ok(response);
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetTileCatalog(CancellationToken cancellationToken)
    {
        var manifest = await _azureBlobProxyService.GetTileManifestModelAsync(cancellationToken);
        var response = new TileCatalogResponse
        {
            Version = manifest.Version,
            GeneratedBy = manifest.GeneratedBy,
            Tiles = manifest.Tiles.Select(tile => new TileCatalogItem
            {
                Id = tile.Id,
                Kind = tile.Kind,
                Images = tile.Images,
                ImageUrls = tile.Images.ToDictionary(
                    pair => pair.Key,
                    pair => $"{Request.Scheme}://{Request.Host}/api/assets/tiles/{pair.Value}"),
                Variants = tile.Variants,
                UiColor = tile.UiColor,
                PhaserColor = tile.PhaserColor,
                Properties = tile.Properties,
                Metadata = tile.Metadata
            }).ToArray()
        };

        return Ok(response);
    }

    [HttpGet("tiles.json")]
    public async Task<IActionResult> GetTilesManifest(CancellationToken cancellationToken)
    {
        var manifest = await _azureBlobProxyService.GetTilesManifestAsync(cancellationToken);
        return Content(manifest, "application/json");
    }

    [HttpGet("bundle")]
    public async Task<IActionResult> DownloadTilesBundle(CancellationToken cancellationToken)
    {
        var asset = await _azureBlobProxyService.GetTileAssetAsync(
            _options.TilesBundleFileName,
            cancellationToken);

        if (asset is null)
        {
            return NotFound();
        }

        if (asset.ContentLength.HasValue)
        {
            Response.ContentLength = asset.ContentLength.Value;
        }

        if (asset.LastModified.HasValue)
        {
            Response.Headers.LastModified = asset.LastModified.Value.ToString("R");
        }

        if (!string.IsNullOrWhiteSpace(asset.ETag))
        {
            Response.Headers.ETag = asset.ETag;
        }

        return File(
            asset.Content,
            asset.ContentType,
            fileDownloadName: _options.TilesBundleFileName,
            enableRangeProcessing: true);
    }

    [HttpGet("/api/assets/characters/bundle")]
    public async Task<IActionResult> DownloadCharactersBundle(CancellationToken cancellationToken)
    {
        var asset = await _azureBlobProxyService.GetCharacterAssetAsync(
            _options.CharactersBundleFileName,
            cancellationToken);

        if (asset is null)
        {
            return NotFound();
        }

        ApplyAssetHeaders(asset);

        return File(
            asset.Content,
            "application/zip",
            fileDownloadName: _options.CharactersBundleFileName,
            enableRangeProcessing: true);
    }

    [HttpGet("/api/assets/character-source-packs/bundle")]
    public async Task<IActionResult> DownloadCharacterSourcePacksBundle(CancellationToken cancellationToken)
    {
        var asset = await _azureBlobProxyService.GetCharacterSourcePackAssetAsync(
            _options.CharacterSourcePacksBundleFileName,
            cancellationToken);

        if (asset is null)
        {
            return NotFound();
        }

        ApplyAssetHeaders(asset);

        return File(
            asset.Content,
            "application/zip",
            fileDownloadName: _options.CharacterSourcePacksBundleFileName,
            enableRangeProcessing: true);
    }

    [HttpGet("{**blobPath}")]
    public async Task<IActionResult> GetTileAsset(string blobPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobPath))
        {
            return BadRequest("A blob path is required.");
        }

        var asset = await _azureBlobProxyService.GetTileAssetAsync(blobPath, cancellationToken);
        if (asset is null)
        {
            return NotFound();
        }

        ApplyAssetHeaders(asset);

        var fileName = Path.GetFileName(blobPath);
        var contentType = blobPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? "application/zip"
            : asset.ContentType;

        return File(
            asset.Content,
            contentType,
            fileDownloadName: fileName,
            enableRangeProcessing: true);
    }

    [HttpGet("/api/assets/characters/{**blobPath}")]
    public async Task<IActionResult> GetCharacterAsset(string blobPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobPath))
        {
            return BadRequest("A blob path is required.");
        }

        var asset = await _azureBlobProxyService.GetCharacterAssetAsync(blobPath, cancellationToken);
        if (asset is null)
        {
            return NotFound();
        }

        ApplyAssetHeaders(asset);

        var fileName = Path.GetFileName(blobPath);
        var contentType = blobPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? "application/zip"
            : asset.ContentType;

        return File(
            asset.Content,
            contentType,
            fileDownloadName: fileName,
            enableRangeProcessing: true);
    }

    [HttpGet("/api/assets/character-source-packs/{**blobPath}")]
    public async Task<IActionResult> GetCharacterSourcePackAsset(string blobPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobPath))
        {
            return BadRequest("A blob path is required.");
        }

        var asset = await _azureBlobProxyService.GetCharacterSourcePackAssetAsync(blobPath, cancellationToken);
        if (asset is null)
        {
            return NotFound();
        }

        ApplyAssetHeaders(asset);

        var fileName = Path.GetFileName(blobPath);
        var contentType = blobPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? "application/zip"
            : asset.ContentType;

        return File(
            asset.Content,
            contentType,
            fileDownloadName: fileName,
            enableRangeProcessing: true);
    }

    [HttpPost("/api/assets/games")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Obsolete("Legacy file upload endpoint. Prefer POST /api/games for application saves.")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(262_144_000)]
    public async Task<IActionResult> UploadUserGameAsset(
        [FromForm] UploadUserGameAssetRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest("A non-empty file is required.");
        }

        var fileName = Path.GetFileName(request.File.FileName);
        var blobPath = string.IsNullOrWhiteSpace(request.BlobPath)
            ? fileName
            : request.BlobPath.Trim();
        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);

        await using var content = request.File.OpenReadStream();
        var result = await _azureBlobProxyService.UploadUserGameAsync(
            userContext.ScopeKey,
            blobPath,
            content,
            request.File.ContentType,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("/api/games")]
    public async Task<IActionResult> SaveGame(
        [FromBody] SaveGameRequest request,
        CancellationToken cancellationToken)
    {
        var gameId = request.GameId?.Trim();
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return BadRequest("A gameId is required.");
        }

        var hasDocument = request.Document is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null };
        var hasGame = request.Game is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null };
        if (!hasDocument && !hasGame)
        {
            return BadRequest("A canonical document or legacy game payload is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var requestName = request.Name?.Trim();
        var payload = hasDocument ? request.Document!.Value : request.Game!.Value;
        string storedFormat;
        string? storedSchema = null;
        int? storedVersion = null;
        string? embeddedGameId;
        string? embeddedGameName;

        if (hasDocument)
        {
            storedFormat = StoredFormatCanonical;
            storedSchema = TryGetNestedString(payload, "document", "schema");
            storedVersion = TryGetNestedInt(payload, "document", "version");
            embeddedGameId = TryGetNestedString(payload, "document", "id");
            embeddedGameName = TryGetNestedString(payload, "document", "name");

            if (!string.Equals(storedSchema, StoredSchemaKsGame, StringComparison.Ordinal))
            {
                return CanonicalValidationFailed(
                    new CanonicalValidationIssueResponse(
                        "document.schema.invalid",
                        "request.document.document.schema must be 'ks.game'.",
                        "document.document.schema"));
            }

            if (!storedVersion.HasValue || storedVersion.Value <= 0)
            {
                return CanonicalValidationFailed(
                    new CanonicalValidationIssueResponse(
                        "document.version.invalid",
                        "request.document.document.version must be a positive integer.",
                        "document.document.version"));
            }

            if (string.IsNullOrWhiteSpace(embeddedGameId))
            {
                return CanonicalValidationFailed(
                    new CanonicalValidationIssueResponse(
                        "document.id.missing",
                        "request.document.document.id is required.",
                        "document.document.id"));
            }

            if (string.IsNullOrWhiteSpace(embeddedGameName))
            {
                return CanonicalValidationFailed(
                    new CanonicalValidationIssueResponse(
                        "document.name.missing",
                        "request.document.document.name is required.",
                        "document.document.name"));
            }
        }
        else
        {
            storedFormat = StoredFormatLegacy;
            embeddedGameId = TryGetEmbeddedString(payload, "gameId")
                ?? TryGetEmbeddedString(payload, "id");
            embeddedGameName = TryGetEmbeddedString(payload, "name");
        }

        if (!string.IsNullOrWhiteSpace(embeddedGameId)
            && !string.Equals(embeddedGameId, gameId, StringComparison.Ordinal))
        {
            if (hasDocument)
            {
                return CanonicalValidationFailed(
                    new CanonicalValidationIssueResponse(
                        "document.id.mismatch",
                        "request.gameId must match request.document.document.id.",
                        "document.document.id",
                        RelatedIds: [gameId, embeddedGameId]));
            }

            return BadRequest("request.gameId must match the embedded game's id.");
        }

        if (!string.IsNullOrWhiteSpace(requestName)
            && !string.IsNullOrWhiteSpace(embeddedGameName)
            && !string.Equals(embeddedGameName, requestName, StringComparison.Ordinal))
        {
            if (hasDocument)
            {
                return CanonicalValidationFailed(
                    new CanonicalValidationIssueResponse(
                        "document.name.mismatch",
                        "request.name must match request.document.document.name when provided.",
                        "document.document.name",
                        RelatedIds: [requestName, embeddedGameName]));
            }

            return BadRequest("request.name must match the embedded game's name when provided.");
        }

        var name = !string.IsNullOrWhiteSpace(requestName)
            ? requestName
            : !string.IsNullOrWhiteSpace(embeddedGameName)
                ? embeddedGameName
                : gameId;

        var result = await _azureBlobProxyService.SaveUserGameJsonAsync(
            userContext.ScopeKey,
            gameId,
            name,
            payload.GetRawText(),
            storedFormat,
            storedSchema,
            storedVersion,
            cancellationToken);

        return Ok(new UserGameMetadataResponse(
            result.GameId,
            result.Name,
            result.BlobPath,
            string.Empty,
            $"{Request.Scheme}://{Request.Host}/api/games/{result.GameId}",
            result.CreatedAt,
            result.LastModified,
            result.ContentLength,
            storedFormat,
            storedSchema,
            storedVersion));
    }

    [HttpGet("/api/games")]
    public async Task<IActionResult> ListGames(
        [FromQuery] string? prefix,
        CancellationToken cancellationToken)
    {
        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var items = await _azureBlobProxyService.ListUserGameAssetsAsync(userContext.ScopeKey, prefix, cancellationToken);
        var normalizedPrefix = prefix?.Trim('/') ?? string.Empty;

        return Ok(new GameListResponse(
            normalizedPrefix,
            items.Count,
            items.Select(item => new GameListItemResponse(
                item.GameId,
                item.Name,
                item.BlobPath,
                string.Empty,
                $"{Request.Scheme}://{Request.Host}/api/games/{item.GameId}",
                item.CreatedAt,
                item.LastModified,
                item.ContentLength,
                item.StoredFormat,
                item.StoredSchema,
                item.StoredVersion))
                .ToArray()));
    }

    [HttpPost("/api/preferences")]
    public async Task<IActionResult> SavePreferences(
        [FromBody] SaveUserPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Preferences is null || request.Preferences.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return BadRequest("A preferences payload is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var result = await _azureBlobProxyService.SaveUserPreferencesJsonAsync(
            userContext.ScopeKey,
            request.Preferences.Value.GetRawText(),
            cancellationToken);

        return Ok(new UserPreferencesResponse(
            result.BlobPath,
            result.ContentLength,
            result.CreatedAt,
            result.LastModified,
            $"{Request.Scheme}://{Request.Host}/api/preferences"));
    }

    [HttpGet("/api/preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken)
    {
        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var preferences = await _azureBlobProxyService.GetUserPreferencesJsonAsync(userContext.ScopeKey, cancellationToken);
        if (preferences is null)
        {
            return NotFound();
        }

        return Ok(new UserPreferencesContentResponse(
            preferences.BlobPath,
            preferences.ContentLength,
            preferences.CreatedAt,
            preferences.LastModified,
            preferences.Preferences));
    }

    [HttpDelete("/api/preferences")]
    public async Task<IActionResult> DeletePreferences(CancellationToken cancellationToken)
    {
        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var deleted = await _azureBlobProxyService.DeleteUserPreferencesAsync(
            userContext.ScopeKey,
            cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("/api/client-messages")]
    public async Task<IActionResult> GetClientMessages(CancellationToken cancellationToken)
    {
        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var messages = await _azureBlobProxyService.GetClientMessagesAsync(
            userContext,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(messages.ETag))
        {
            Response.Headers.ETag = messages.ETag;
        }

        if (messages.LastModified is not null)
        {
            Response.Headers.LastModified = messages.LastModified.Value.ToString("R");
        }

        return Ok(messages);
    }

    [HttpPost("/api/client-messages/{messageId}/read")]
    public async Task<IActionResult> MarkClientMessageRead(
        string messageId,
        CancellationToken cancellationToken)
    {
        var normalizedMessageId = messageId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessageId))
        {
            return BadRequest("A messageId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        await _azureBlobProxyService.MarkClientMessageReadAsync(
            userContext.ScopeKey,
            normalizedMessageId,
            cancellationToken);

        return NoContent();
    }

    [HttpPost("/api/client-messages/{messageId}/dismiss")]
    public async Task<IActionResult> DismissClientMessage(
        string messageId,
        CancellationToken cancellationToken)
    {
        var normalizedMessageId = messageId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessageId))
        {
            return BadRequest("A messageId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        await _azureBlobProxyService.DismissClientMessageAsync(
            userContext.ScopeKey,
            normalizedMessageId,
            cancellationToken);

        return NoContent();
    }

    [HttpPost("/api/client-messages/read-all")]
    public async Task<IActionResult> MarkAllClientMessagesRead(CancellationToken cancellationToken)
    {
        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        await _azureBlobProxyService.MarkAllClientMessagesReadAsync(
            userContext,
            cancellationToken);

        return NoContent();
    }

    [HttpGet("/api/games/{gameId}")]
    public async Task<IActionResult> GetGame(
        string gameId,
        CancellationToken cancellationToken)
    {
        var normalizedGameId = gameId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedGameId))
        {
            return BadRequest("A gameId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var game = await _azureBlobProxyService.GetUserGameJsonAsync(userContext.ScopeKey, normalizedGameId, cancellationToken);
        if (game is null)
        {
            return NotFound();
        }

        return Ok(new GameResponse(
            game.GameId,
            game.Name,
            game.BlobPath,
            game.ContentLength,
            game.CreatedAt,
            game.LastModified,
            string.Empty,
            $"{Request.Scheme}://{Request.Host}/api/games/{game.GameId}",
            game.StoredFormat,
            game.StoredSchema,
            game.StoredVersion,
            game.Document,
            game.Game));
    }

    [HttpDelete("/api/games/{gameId}")]
    public async Task<IActionResult> DeleteGame(
        string gameId,
        CancellationToken cancellationToken)
    {
        var normalizedGameId = gameId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedGameId))
        {
            return BadRequest("A gameId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var deleted = await _azureBlobProxyService.DeleteUserGameAsync(
            userContext.ScopeKey,
            normalizedGameId,
            cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("/api/assets/azure-identity")]
    public async Task<IActionResult> GetAzureStorageIdentity(CancellationToken cancellationToken)
    {
        var hostEnvironment = HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        if (!hostEnvironment.IsDevelopment())
        {
            return NotFound();
        }

        var identity = await _azureBlobProxyService.GetStorageIdentityInfoAsync(cancellationToken);
        return Ok(identity);
    }

    private void ApplyAssetHeaders(AzureBlobContent asset)
    {
        if (asset.ContentLength.HasValue)
        {
            Response.ContentLength = asset.ContentLength.Value;
        }

        if (asset.LastModified.HasValue)
        {
            Response.Headers.LastModified = asset.LastModified.Value.ToString("R");
        }

        if (!string.IsNullOrWhiteSpace(asset.ETag))
        {
            Response.Headers.ETag = asset.ETag;
        }
    }

    private static string? TryGetEmbeddedString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static string? TryGetNestedString(
        JsonElement element,
        string objectPropertyName,
        string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(objectPropertyName, out var nested)
            || nested.ValueKind != JsonValueKind.Object
            || !nested.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static int? TryGetNestedInt(
        JsonElement element,
        string objectPropertyName,
        string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(objectPropertyName, out var nested)
            || nested.ValueKind != JsonValueKind.Object
            || !nested.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var parsed))
        {
            return null;
        }

        return parsed;
    }

    private IActionResult CanonicalValidationFailed(
        params CanonicalValidationIssueResponse[] issues)
    {
        return BadRequest(new CanonicalValidationErrorResponse(
            "canonical_validation_failed",
            "Canonical game document validation failed.",
            issues));
    }
}
