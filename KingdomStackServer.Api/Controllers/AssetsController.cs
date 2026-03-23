using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace KingdomStackServer.Api.Controllers;

[ApiController]
[Route("api/assets/tiles")]
public class AssetsController : ControllerBase
{
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

        if (request.Game is null || request.Game.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return BadRequest("A game payload is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var requestName = request.Name?.Trim();
        var embeddedGameId = TryGetEmbeddedString(request.Game.Value, "gameId")
            ?? TryGetEmbeddedString(request.Game.Value, "id");
        if (!string.IsNullOrWhiteSpace(embeddedGameId)
            && !string.Equals(embeddedGameId, gameId, StringComparison.Ordinal))
        {
            return BadRequest("request.gameId must match the embedded game's id.");
        }

        var embeddedGameName = TryGetEmbeddedString(request.Game.Value, "name");
        if (!string.IsNullOrWhiteSpace(requestName)
            && !string.IsNullOrWhiteSpace(embeddedGameName)
            && !string.Equals(embeddedGameName, requestName, StringComparison.Ordinal))
        {
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
            request.Game.Value.GetRawText(),
            cancellationToken);

        return Ok(new UserGameMetadataResponse(
            result.GameId,
            result.Name,
            result.BlobPath,
            string.Empty,
            $"{Request.Scheme}://{Request.Host}/api/games/{result.GameId}",
            result.CreatedAt,
            result.LastModified,
            result.ContentLength));
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
                item.ContentLength))
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
}
