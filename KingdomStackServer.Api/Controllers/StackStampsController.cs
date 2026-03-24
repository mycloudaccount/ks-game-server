using Azure;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace KingdomStackServer.Api.Controllers;

[ApiController]
[Route("api/assets/stack-stamps")]
public sealed class StackStampsController : ControllerBase
{
    private static readonly HashSet<int> SupportedSchemaVersions = [1, 2];
    private const int MaxPreviewBytes = 512 * 1024;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    private readonly AzureBlobProxyService _azureBlobProxyService;

    public StackStampsController(AzureBlobProxyService azureBlobProxyService)
    {
        _azureBlobProxyService = azureBlobProxyService;
    }

    [HttpGet]
    public async Task<IActionResult> ListStackStamps(
        [FromQuery] string? prefix,
        [FromQuery] string? search,
        [FromQuery] string? tag,
        [FromQuery] int? limit,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
    {
        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var result = await _azureBlobProxyService.ListUserStackStampsAsync(
            userContext.ScopeKey,
            prefix,
            search,
            tag,
            limit,
            continuationToken,
            cancellationToken);

        return Ok(new StackStampListResponse(
            prefix?.Trim() ?? string.Empty,
            result.Items.Count,
            result.ContinuationToken,
            result.Items.Select(MapListItem).ToArray()));
    }

    [HttpGet("{stackStampId}")]
    public async Task<IActionResult> GetStackStamp(
        string stackStampId,
        CancellationToken cancellationToken)
    {
        var normalizedStackStampId = stackStampId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedStackStampId))
        {
            return BadRequest("A stackStampId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var stamp = await _azureBlobProxyService.GetUserStackStampAsync(
            userContext.ScopeKey,
            normalizedStackStampId,
            cancellationToken);

        if (stamp is null)
        {
            return NotFound();
        }

        return Ok(MapDetail(stamp));
    }

    [HttpPost]
    public async Task<IActionResult> CreateStackStamp(
        [FromBody] CreateStackStampRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateCreateOrUpdateRequest(request.Name, request.Definition);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var previewBytesResult = TryDecodePreviewPng(request.PreviewImageBase64, allowMissing: true);
        if (!previewBytesResult.Success)
        {
            return BadRequest(previewBytesResult.ErrorMessage);
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var stackStampId = $"stack-{Guid.NewGuid():N}";

        if (await _azureBlobProxyService.UserStackStampNameExistsAsync(
                userContext.ScopeKey,
                request.Name!.Trim(),
                excludeStackStampId: null,
                cancellationToken))
        {
            return Conflict("A stack stamp with this name already exists.");
        }

        try
        {
            var result = await _azureBlobProxyService.SaveUserStackStampAsync(
                userContext.ScopeKey,
                stackStampId,
                request.Name!.Trim(),
                NormalizeOptionalText(request.Description),
                request.Tags,
                request.Definition!,
                previewBytesResult.Bytes,
                previewBytesResult.Bytes is null ? null : "image/png",
                null,
                clearPreview: false,
                createOnly: true,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetStackStamp),
                new { stackStampId = result.Id },
                MapDetail(result));
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
        {
            return Conflict("A stack stamp with this id already exists.");
        }
    }

    [HttpPut("{stackStampId}")]
    public async Task<IActionResult> UpdateStackStamp(
        string stackStampId,
        [FromBody] UpdateStackStampRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedStackStampId = stackStampId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedStackStampId))
        {
            return BadRequest("A stackStampId is required.");
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

        if (!string.IsNullOrWhiteSpace(request.PreviewImageBase64) && request.ClearPreview == true)
        {
            return BadRequest("previewImageBase64 and clearPreview cannot both be provided.");
        }

        var previewBytesResult = TryDecodePreviewPng(request.PreviewImageBase64, allowMissing: true);
        if (!previewBytesResult.Success)
        {
            return BadRequest(previewBytesResult.ErrorMessage);
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);

        if (await _azureBlobProxyService.UserStackStampNameExistsAsync(
                userContext.ScopeKey,
                request.Name!.Trim(),
                normalizedStackStampId,
                cancellationToken))
        {
            return Conflict("A stack stamp with this name already exists.");
        }

        try
        {
            var result = await _azureBlobProxyService.SaveUserStackStampAsync(
                userContext.ScopeKey,
                normalizedStackStampId,
                request.Name!.Trim(),
                NormalizeOptionalText(request.Description),
                request.Tags,
                request.Definition!,
                previewBytesResult.Bytes,
                previewBytesResult.Bytes is null ? null : "image/png",
                request.ETag,
                request.ClearPreview == true,
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
            return StatusCode(StatusCodes.Status412PreconditionFailed, "The stack stamp was modified by another request.");
        }
    }

    [HttpDelete("{stackStampId}")]
    public async Task<IActionResult> DeleteStackStamp(
        string stackStampId,
        CancellationToken cancellationToken)
    {
        var normalizedStackStampId = stackStampId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedStackStampId))
        {
            return BadRequest("A stackStampId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var deleted = await _azureBlobProxyService.DeleteUserStackStampAsync(
            userContext.ScopeKey,
            normalizedStackStampId,
            cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("{stackStampId}/preview")]
    public async Task<IActionResult> GetStackStampPreview(
        string stackStampId,
        CancellationToken cancellationToken)
    {
        var normalizedStackStampId = stackStampId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedStackStampId))
        {
            return BadRequest("A stackStampId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var preview = await _azureBlobProxyService.GetUserStackStampPreviewAsync(
            userContext.ScopeKey,
            normalizedStackStampId,
            cancellationToken);

        if (preview is null)
        {
            return NotFound();
        }

        ApplyAssetHeaders(preview);
        return File(
            preview.Content,
            preview.ContentType,
            fileDownloadName: $"{normalizedStackStampId}.png",
            enableRangeProcessing: true);
    }

    private StackStampListItemResponse MapListItem(UserStackStampListItem item)
    {
        return new StackStampListItemResponse(
            item.Id,
            item.Name,
            item.Description,
            item.BlobPath,
            BuildLoadUrl(item.Id),
            item.HasPreview ? BuildPreviewUrl(item.Id) : null,
            item.SchemaVersion,
            item.Version,
            item.Tags,
            item.Footprint,
            item.EntryCount,
            item.TileReferences,
            item.CharacterReferences,
            item.CreatedAt,
            item.LastModified,
            item.ETag);
    }

    private StackStampResponse MapDetail(UserStackStampContent item)
    {
        return new StackStampResponse(
            item.Id,
            item.Name,
            item.Description,
            item.BlobPath,
            BuildLoadUrl(item.Id),
            item.HasPreview ? BuildPreviewUrl(item.Id) : null,
            item.SchemaVersion,
            item.Version,
            item.Tags,
            item.CreatedAt,
            item.LastModified,
            item.ETag,
            item.Definition);
    }

    private StackStampResponse MapDetail(UserStackStampSaveResult item)
    {
        return new StackStampResponse(
            item.Id,
            item.Name,
            item.Description,
            item.BlobPath,
            BuildLoadUrl(item.Id),
            item.HasPreview ? BuildPreviewUrl(item.Id) : null,
            item.SchemaVersion,
            item.Version,
            item.Tags,
            item.CreatedAt,
            item.LastModified,
            item.ETag,
            item.Definition);
    }

    private string BuildLoadUrl(string id)
        => $"{Request.Scheme}://{Request.Host}/api/assets/stack-stamps/{id}";

    private string BuildPreviewUrl(string id)
        => $"{Request.Scheme}://{Request.Host}/api/assets/stack-stamps/{id}/preview";

    private static string? ValidateCreateOrUpdateRequest(string? name, StackStampDefinitionDto? definition)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "A name is required.";
        }

        if (definition is null)
        {
            return "A definition is required.";
        }

        if (!SupportedSchemaVersions.Contains(definition.SchemaVersion))
        {
            return "Only schemaVersion 1 and 2 are supported.";
        }

        if (definition.Entries is null || definition.Entries.Length == 0)
        {
            return "At least one entry is required.";
        }

        var entryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var occupiedCells = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in definition.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.EntryId))
            {
                return "Each entry must have an entryId.";
            }

            if (!entryIds.Add(entry.EntryId.Trim()))
            {
                return $"Duplicate entryId '{entry.EntryId}' is not allowed.";
            }

            var cellKey = $"{entry.Dx}:{entry.Dy}";
            if (!occupiedCells.Add(cellKey))
            {
                return $"Multiple entries cannot occupy the same cell ({entry.Dx}, {entry.Dy}).";
            }

            if (string.Equals(entry.EntityType, "tile", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasRequiredString(entry.Payload, "tileId"))
                {
                    return $"Tile entry '{entry.EntryId}' must include payload.tileId.";
                }

                continue;
            }

            if (string.Equals(entry.EntityType, "character", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasRequiredString(entry.Payload, "characterId"))
                {
                    return $"Character entry '{entry.EntryId}' must include payload.characterId.";
                }

                continue;
            }

            return $"Entry '{entry.EntryId}' has unsupported entityType '{entry.EntityType}'.";
        }

        if (definition.SchemaVersion == 2)
        {
            var stacks = definition.Stacks ?? [];
            var stackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entryMembership = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var stack in stacks)
            {
                if (string.IsNullOrWhiteSpace(stack.StackId))
                {
                    return "Each stack must have a stackId.";
                }

                var normalizedStackId = stack.StackId.Trim();
                if (!stackIds.Add(normalizedStackId))
                {
                    return $"Duplicate stackId '{stack.StackId}' is not allowed.";
                }

                if (string.IsNullOrWhiteSpace(stack.Name))
                {
                    return $"Stack '{stack.StackId}' must have a name.";
                }

                if (string.IsNullOrWhiteSpace(stack.AnchorEntryId))
                {
                    return $"Stack '{stack.StackId}' must have an anchorEntryId.";
                }

                var normalizedAnchorEntryId = stack.AnchorEntryId.Trim();
                if (!entryIds.Contains(normalizedAnchorEntryId))
                {
                    return $"Stack '{stack.StackId}' references missing anchorEntryId '{stack.AnchorEntryId}'.";
                }

                var normalizedEntryIds = (stack.EntryIds ?? [])
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (normalizedEntryIds.Length == 0)
                {
                    return $"Stack '{stack.StackId}' must include at least one entryId.";
                }

                if (!normalizedEntryIds.Contains(normalizedAnchorEntryId, StringComparer.OrdinalIgnoreCase))
                {
                    return $"Stack '{stack.StackId}' anchorEntryId must also appear in entryIds.";
                }

                foreach (var entryId in normalizedEntryIds)
                {
                    if (!entryIds.Contains(entryId))
                    {
                        return $"Stack '{stack.StackId}' references missing entryId '{entryId}'.";
                    }

                    if (entryMembership.TryGetValue(entryId, out var ownerStackId))
                    {
                        return $"Entry '{entryId}' cannot belong to more than one stack ('{ownerStackId}' and '{stack.StackId}').";
                    }

                    entryMembership[entryId] = normalizedStackId;
                }
            }

            foreach (var stack in stacks)
            {
                foreach (var childStackId in stack.ChildStackIds ?? [])
                {
                    if (string.IsNullOrWhiteSpace(childStackId) || !stackIds.Contains(childStackId.Trim()))
                    {
                        return $"Stack '{stack.StackId}' references missing childStackId '{childStackId}'.";
                    }
                }

                if (!string.IsNullOrWhiteSpace(stack.ParentStackId) && !stackIds.Contains(stack.ParentStackId.Trim()))
                {
                    return $"Stack '{stack.StackId}' references missing parentStackId '{stack.ParentStackId}'.";
                }
            }

            var cycleError = ValidateStackGraphHasNoCycles(stacks);
            if (cycleError is not null)
            {
                return cycleError;
            }
        }

        return null;
    }

    private static string? ValidateStackGraphHasNoCycles(StackStampGroupDto[] stacks)
    {
        var childrenById = stacks.ToDictionary(
            stack => stack.StackId.Trim(),
            stack => (stack.ChildStackIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Dfs(string stackId)
        {
            if (visited.Contains(stackId))
            {
                return false;
            }

            if (!visiting.Add(stackId))
            {
                return true;
            }

            foreach (var childId in childrenById.GetValueOrDefault(stackId, []))
            {
                if (Dfs(childId))
                {
                    return true;
                }
            }

            visiting.Remove(stackId);
            visited.Add(stackId);
            return false;
        }

        foreach (var stackId in childrenById.Keys)
        {
            if (Dfs(stackId))
            {
                return $"Stack hierarchy contains a cycle involving '{stackId}'.";
            }
        }

        return null;
    }

    private static bool HasRequiredString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString());
    }

    private static (bool Success, byte[]? Bytes, string? ErrorMessage) TryDecodePreviewPng(string? previewImageBase64, bool allowMissing)
    {
        if (string.IsNullOrWhiteSpace(previewImageBase64))
        {
            return allowMissing
                ? (true, null, null)
                : (false, null, "A preview image is required.");
        }

        var payload = previewImageBase64.Trim();
        var commaIndex = payload.IndexOf(',');
        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
        {
            payload = payload[(commaIndex + 1)..];
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return (false, null, "previewImageBase64 must be valid base64.");
        }

        if (bytes.Length == 0)
        {
            return (false, null, "previewImageBase64 must not be empty.");
        }

        if (bytes.Length > MaxPreviewBytes)
        {
            return (false, null, $"previewImageBase64 exceeds the {MaxPreviewBytes} byte limit.");
        }

        if (bytes.Length < PngSignature.Length || !bytes.Take(PngSignature.Length).SequenceEqual(PngSignature))
        {
            return (false, null, "previewImageBase64 must be a valid PNG image.");
        }

        return (true, bytes, null);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
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
