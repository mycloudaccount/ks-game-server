using Azure;
using Microsoft.AspNetCore.Mvc;

namespace KingdomStackServer.Api.Controllers;

[ApiController]
[Route("api/characters")]
public sealed class CharactersController : ControllerBase
{
    private const int MaxThumbnailBytes = 1024 * 1024;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    private readonly AzureBlobProxyService _azureBlobProxyService;

    public CharactersController(AzureBlobProxyService azureBlobProxyService)
    {
        _azureBlobProxyService = azureBlobProxyService;
    }

    [HttpGet]
    public async Task<IActionResult> ListCharacters(
        [FromQuery] string? prefix,
        [FromQuery] int? limit,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var result = await _azureBlobProxyService.ListUserEditorCharactersAsync(
            userContext.ScopeKey,
            prefix,
            limit,
            continuationToken,
            cancellationToken);

        return Ok(new EditorCharacterListResponse(
            prefix?.Trim() ?? string.Empty,
            result.Items.Count,
            result.ContinuationToken,
            result.Items.Select(MapListItem).ToArray()));
    }

    [HttpGet("{characterId}")]
    public async Task<IActionResult> GetCharacter(
        string characterId,
        CancellationToken cancellationToken)
    {
        var normalizedCharacterId = characterId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCharacterId))
        {
            return BadRequest("A characterId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var character = await _azureBlobProxyService.GetUserEditorCharacterAsync(
            userContext.ScopeKey,
            normalizedCharacterId,
            cancellationToken);

        if (character is null)
        {
            return NotFound();
        }

        return Ok(MapDetail(character));
    }

    [HttpPost]
    public async Task<IActionResult> CreateCharacter(
        [FromBody] CreateEditorCharacterRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateCreateOrUpdateRequest(request.Name, request.Definition);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var thumbnailBytesResult = TryDecodeThumbnailPng(request.ThumbnailImageBase64, allowMissing: true);
        if (!thumbnailBytesResult.Success)
        {
            return BadRequest(thumbnailBytesResult.ErrorMessage);
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var characterId = $"char-{Guid.NewGuid():N}";

        if (await _azureBlobProxyService.UserEditorCharacterNameExistsAsync(
                userContext.ScopeKey,
                request.Name!.Trim(),
                excludeCharacterId: null,
                cancellationToken))
        {
            return Conflict("A character with this name already exists.");
        }

        try
        {
            var result = await _azureBlobProxyService.SaveUserEditorCharacterAsync(
                userContext.ScopeKey,
                characterId,
                request.Name!.Trim(),
                request.Definition!,
                thumbnailBytesResult.Bytes,
                thumbnailBytesResult.Bytes is null ? null : "image/png",
                null,
                clearThumbnail: false,
                createOnly: true,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetCharacter),
                new { characterId = result.Id },
                MapDetail(result));
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
        {
            return Conflict("A character with this id already exists.");
        }
    }

    [HttpPut("{characterId}")]
    public async Task<IActionResult> UpdateCharacter(
        string characterId,
        [FromBody] UpdateEditorCharacterRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedCharacterId = characterId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCharacterId))
        {
            return BadRequest("A characterId is required.");
        }

        var validationError = ValidateCreateOrUpdateRequest(request.Name, request.Definition);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        if (string.IsNullOrWhiteSpace(request.ETag))
        {
            return BadRequest("An etag is required for updates.");
        }

        if (!string.IsNullOrWhiteSpace(request.ThumbnailImageBase64) && request.ClearThumbnail == true)
        {
            return BadRequest("thumbnailImageBase64 and clearThumbnail cannot both be provided.");
        }

        var thumbnailBytesResult = TryDecodeThumbnailPng(request.ThumbnailImageBase64, allowMissing: true);
        if (!thumbnailBytesResult.Success)
        {
            return BadRequest(thumbnailBytesResult.ErrorMessage);
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);

        if (await _azureBlobProxyService.UserEditorCharacterNameExistsAsync(
                userContext.ScopeKey,
                request.Name!.Trim(),
                normalizedCharacterId,
                cancellationToken))
        {
            return Conflict("A character with this name already exists.");
        }

        try
        {
            var result = await _azureBlobProxyService.SaveUserEditorCharacterAsync(
                userContext.ScopeKey,
                normalizedCharacterId,
                request.Name!.Trim(),
                request.Definition!,
                thumbnailBytesResult.Bytes,
                thumbnailBytesResult.Bytes is null ? null : "image/png",
                request.ETag,
                request.ClearThumbnail == true,
                createOnly: false,
                cancellationToken);

            return Ok(MapDetail(result));
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed, "The character was modified by another request.");
        }
    }

    [HttpDelete("{characterId}")]
    public async Task<IActionResult> DeleteCharacter(
        string characterId,
        CancellationToken cancellationToken)
    {
        var normalizedCharacterId = characterId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCharacterId))
        {
            return BadRequest("A characterId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var deleted = await _azureBlobProxyService.DeleteUserEditorCharacterAsync(
            userContext.ScopeKey,
            normalizedCharacterId,
            cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("{characterId}/thumbnail")]
    public async Task<IActionResult> GetCharacterThumbnail(
        string characterId,
        CancellationToken cancellationToken)
    {
        var normalizedCharacterId = characterId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCharacterId))
        {
            return BadRequest("A characterId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var thumbnail = await _azureBlobProxyService.GetUserEditorCharacterThumbnailAsync(
            userContext.ScopeKey,
            normalizedCharacterId,
            cancellationToken);

        if (thumbnail is null)
        {
            return NotFound();
        }

        ApplyAssetHeaders(thumbnail);
        return File(
            thumbnail.Content,
            thumbnail.ContentType,
            fileDownloadName: $"{normalizedCharacterId}.png",
            enableRangeProcessing: true);
    }

    private EditorCharacterListItemResponse MapListItem(UserEditorCharacterListItem item)
    {
        return new EditorCharacterListItemResponse(
            item.Id,
            item.Name,
            item.BlobPath,
            BuildLoadUrl(item.Id),
            item.HasThumbnail ? BuildThumbnailUrl(item.Id) : null,
            item.SchemaVersion,
            item.Version,
            item.SelectedPackId,
            item.CreatedAt,
            item.LastModified,
            item.ETag);
    }

    private EditorCharacterResponse MapDetail(UserEditorCharacterContent item)
    {
        return new EditorCharacterResponse(
            item.Id,
            item.Name,
            item.BlobPath,
            BuildLoadUrl(item.Id),
            item.HasThumbnail ? BuildThumbnailUrl(item.Id) : null,
            item.SchemaVersion,
            item.Version,
            item.SelectedPackId,
            item.CreatedAt,
            item.LastModified,
            item.ETag,
            item.Definition);
    }

    private EditorCharacterResponse MapDetail(UserEditorCharacterSaveResult item)
    {
        return new EditorCharacterResponse(
            item.Id,
            item.Name,
            item.BlobPath,
            BuildLoadUrl(item.Id),
            item.HasThumbnail ? BuildThumbnailUrl(item.Id) : null,
            item.SchemaVersion,
            item.Version,
            item.SelectedPackId,
            item.CreatedAt,
            item.LastModified,
            item.ETag,
            item.Definition);
    }

    private string BuildLoadUrl(string characterId)
        => $"{Request.Scheme}://{Request.Host}/api/characters/{characterId}";

    private string BuildThumbnailUrl(string characterId)
        => $"{Request.Scheme}://{Request.Host}/api/characters/{characterId}/thumbnail";

    private static string? ValidateCreateOrUpdateRequest(
        string? name,
        EditorCharacterDefinitionDto? definition)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "A character name is required.";
        }

        if (definition is null)
        {
            return "A character definition is required.";
        }

        if (definition.Recipe is null)
        {
            return "A character recipe is required.";
        }

        if (definition.Recipe.EnabledAttachmentCategories is null)
        {
            return "enabledAttachmentCategories is required.";
        }

        if (definition.Recipe.SelectedModelIds is null)
        {
            return "selectedModelIds is required.";
        }

        if (definition.AttachmentPositionOffsets is null)
        {
            return "attachmentPositionOffsets is required.";
        }

        if (definition.AttachmentRotationOffsets is null)
        {
            return "attachmentRotationOffsets is required.";
        }

        return null;
    }

    private static (bool Success, byte[]? Bytes, string? ErrorMessage) TryDecodeThumbnailPng(
        string? thumbnailImageBase64,
        bool allowMissing)
    {
        if (string.IsNullOrWhiteSpace(thumbnailImageBase64))
        {
            return allowMissing
                ? (true, null, null)
                : (false, null, "A thumbnail image is required.");
        }

        var normalized = thumbnailImageBase64.Trim();
        var commaIndex = normalized.IndexOf(',');
        if (commaIndex >= 0)
        {
            normalized = normalized[(commaIndex + 1)..];
        }

        try
        {
            var bytes = Convert.FromBase64String(normalized);
            if (bytes.Length == 0)
            {
                return (false, null, "The thumbnail image was empty.");
            }

            if (bytes.Length > MaxThumbnailBytes)
            {
                return (false, null, $"The thumbnail image must be {MaxThumbnailBytes / 1024} KB or smaller.");
            }

            if (bytes.Length < PngSignature.Length
                || !PngSignature.SequenceEqual(bytes.Take(PngSignature.Length)))
            {
                return (false, null, "The thumbnail image must be a PNG.");
            }

            return (true, bytes, null);
        }
        catch (FormatException)
        {
            return (false, null, "The thumbnail image was not valid base64.");
        }
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
}
