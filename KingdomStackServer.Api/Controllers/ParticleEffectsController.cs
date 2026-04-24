using Azure;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace KingdomStackServer.Api.Controllers;

[ApiController]
[Route("api/assets/particle-effects")]
public sealed class ParticleEffectsController : ControllerBase
{
    private const string FootTrailType = "footTrail";
    private const string LandingImpactType = "landingImpact";
    private static readonly HashSet<string> ParticleShapes = new(StringComparer.Ordinal)
    {
        "circle",
        "square",
        "diamond"
    };
    private static readonly HashSet<string> FootTrailStepModes = new(StringComparer.Ordinal)
    {
        "alternate",
        "both",
        "left",
        "right"
    };

    private readonly AzureBlobProxyService _azureBlobProxyService;

    public ParticleEffectsController(AzureBlobProxyService azureBlobProxyService)
    {
        _azureBlobProxyService = azureBlobProxyService;
    }

    [HttpGet("foot-trails")]
    public async Task<IActionResult> ListFootTrails(
        [FromQuery] string? prefix,
        [FromQuery] int? limit,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
        => await ListEffects(FootTrailType, prefix, limit, continuationToken, cancellationToken);

    [HttpGet("landing-impacts")]
    public async Task<IActionResult> ListLandingImpacts(
        [FromQuery] string? prefix,
        [FromQuery] int? limit,
        [FromQuery] string? continuationToken,
        CancellationToken cancellationToken)
        => await ListEffects(LandingImpactType, prefix, limit, continuationToken, cancellationToken);

    [HttpGet("foot-trails/{particleEffectId}")]
    public async Task<IActionResult> GetFootTrail(
        string particleEffectId,
        CancellationToken cancellationToken)
        => await GetEffect(FootTrailType, particleEffectId, cancellationToken);

    [HttpGet("landing-impacts/{particleEffectId}")]
    public async Task<IActionResult> GetLandingImpact(
        string particleEffectId,
        CancellationToken cancellationToken)
        => await GetEffect(LandingImpactType, particleEffectId, cancellationToken);

    [HttpPost("foot-trails")]
    public async Task<IActionResult> CreateFootTrail(
        [FromBody] CreateParticleEffectRequest request,
        CancellationToken cancellationToken)
        => await CreateEffect(FootTrailType, request, cancellationToken);

    [HttpPost("landing-impacts")]
    public async Task<IActionResult> CreateLandingImpact(
        [FromBody] CreateParticleEffectRequest request,
        CancellationToken cancellationToken)
        => await CreateEffect(LandingImpactType, request, cancellationToken);

    [HttpPut("foot-trails/{particleEffectId}")]
    public async Task<IActionResult> UpdateFootTrail(
        string particleEffectId,
        [FromBody] UpdateParticleEffectRequest request,
        CancellationToken cancellationToken)
        => await UpdateEffect(FootTrailType, particleEffectId, request, cancellationToken);

    [HttpPut("landing-impacts/{particleEffectId}")]
    public async Task<IActionResult> UpdateLandingImpact(
        string particleEffectId,
        [FromBody] UpdateParticleEffectRequest request,
        CancellationToken cancellationToken)
        => await UpdateEffect(LandingImpactType, particleEffectId, request, cancellationToken);

    [HttpDelete("foot-trails/{particleEffectId}")]
    public async Task<IActionResult> DeleteFootTrail(
        string particleEffectId,
        CancellationToken cancellationToken)
        => await DeleteEffect(FootTrailType, particleEffectId, cancellationToken);

    [HttpDelete("landing-impacts/{particleEffectId}")]
    public async Task<IActionResult> DeleteLandingImpact(
        string particleEffectId,
        CancellationToken cancellationToken)
        => await DeleteEffect(LandingImpactType, particleEffectId, cancellationToken);

    private async Task<IActionResult> ListEffects(
        string effectType,
        string? prefix,
        int? limit,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var result = await _azureBlobProxyService.ListUserParticleEffectsAsync(
            userContext.ScopeKey,
            effectType,
            prefix,
            limit,
            continuationToken,
            cancellationToken);

        return Ok(new ParticleEffectListResponse(
            effectType,
            prefix?.Trim() ?? string.Empty,
            result.Items.Count,
            result.ContinuationToken,
            result.Items.Select(MapListItem).ToArray()));
    }

    private async Task<IActionResult> GetEffect(
        string effectType,
        string particleEffectId,
        CancellationToken cancellationToken)
    {
        var normalizedParticleEffectId = particleEffectId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedParticleEffectId))
        {
            return BadRequest("A particleEffectId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var effect = await _azureBlobProxyService.GetUserParticleEffectAsync(
            userContext.ScopeKey,
            effectType,
            normalizedParticleEffectId,
            cancellationToken);

        if (effect is null)
        {
            return NotFound();
        }

        return Ok(MapDetail(effect));
    }

    private async Task<IActionResult> CreateEffect(
        string effectType,
        [FromBody] CreateParticleEffectRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateParticleEffect(effectType, request.Effect, out var effect, out var effectId);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var name = ResolveName(request.Name, effectId!);
        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);

        try
        {
            var result = await _azureBlobProxyService.SaveUserParticleEffectAsync(
                userContext.ScopeKey,
                effectType,
                effectId!,
                name,
                effect!.Value,
                ifMatchEtag: null,
                createOnly: true,
                cancellationToken);

            return CreatedAtAction(
                GetActionName(effectType),
                new { particleEffectId = result.Id },
                MapDetail(result));
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
        {
            return Conflict($"A {ToEffectLabel(effectType)} particle effect with this id already exists.");
        }
    }

    private async Task<IActionResult> UpdateEffect(
        string effectType,
        string particleEffectId,
        [FromBody] UpdateParticleEffectRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedParticleEffectId = particleEffectId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedParticleEffectId))
        {
            return BadRequest("A particleEffectId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ETag))
        {
            return BadRequest("An etag is required for updates.");
        }

        var validationError = ValidateParticleEffect(effectType, request.Effect, out var effect, out var effectId);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var normalizedRouteId = AzureBlobProxyService.NormalizeParticleEffectId(normalizedParticleEffectId);
        var normalizedEffectId = AzureBlobProxyService.NormalizeParticleEffectId(effectId!);
        if (!string.Equals(normalizedRouteId, normalizedEffectId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("The route particleEffectId must match effect.id.");
        }

        var name = ResolveName(request.Name, effectId!);
        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);

        try
        {
            var result = await _azureBlobProxyService.SaveUserParticleEffectAsync(
                userContext.ScopeKey,
                effectType,
                normalizedRouteId,
                name,
                effect!.Value,
                request.ETag,
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
            return StatusCode(StatusCodes.Status412PreconditionFailed, "The particle effect was modified by another request.");
        }
    }

    private async Task<IActionResult> DeleteEffect(
        string effectType,
        string particleEffectId,
        CancellationToken cancellationToken)
    {
        var normalizedParticleEffectId = particleEffectId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedParticleEffectId))
        {
            return BadRequest("A particleEffectId is required.");
        }

        var userContext = await _azureBlobProxyService.GetStorageUserContextAsync(cancellationToken);
        var deleted = await _azureBlobProxyService.DeleteUserParticleEffectAsync(
            userContext.ScopeKey,
            effectType,
            normalizedParticleEffectId,
            cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    private ParticleEffectListItemResponse MapListItem(UserParticleEffectListItem item)
    {
        return new ParticleEffectListItemResponse(
            item.Id,
            item.Name,
            item.Type,
            item.BlobPath,
            BuildLoadUrl(item.Type, item.Id),
            item.SchemaVersion,
            item.Version,
            item.CreatedAt,
            item.LastModified,
            item.ETag);
    }

    private ParticleEffectResponse MapDetail(UserParticleEffectContent item)
    {
        return new ParticleEffectResponse(
            item.Id,
            item.Name,
            item.Type,
            item.BlobPath,
            BuildLoadUrl(item.Type, item.Id),
            item.SchemaVersion,
            item.Version,
            item.CreatedAt,
            item.LastModified,
            item.ETag,
            item.Effect);
    }

    private ParticleEffectResponse MapDetail(UserParticleEffectSaveResult item)
    {
        return new ParticleEffectResponse(
            item.Id,
            item.Name,
            item.Type,
            item.BlobPath,
            BuildLoadUrl(item.Type, item.Id),
            item.SchemaVersion,
            item.Version,
            item.CreatedAt,
            item.LastModified,
            item.ETag,
            item.Effect);
    }

    private string BuildLoadUrl(string effectType, string id)
        => $"{Request.Scheme}://{Request.Host}/api/assets/particle-effects/{ToRouteSegment(effectType)}/{id}";

    private static string ResolveName(string? requestedName, string effectId)
    {
        var normalizedName = requestedName?.Trim();
        return string.IsNullOrWhiteSpace(normalizedName) ? effectId : normalizedName;
    }

    private static string? ValidateParticleEffect(
        string expectedType,
        JsonElement? candidate,
        out JsonElement? effect,
        out string? effectId)
    {
        effect = null;
        effectId = null;

        if (candidate is null || candidate.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "An effect payload is required.";
        }

        var value = candidate.Value;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return "The effect payload must be an object.";
        }

        if (!TryGetRequiredString(value, "id", out effectId))
        {
            return "effect.id is required.";
        }

        if (!TryGetRequiredString(value, "type", out var type)
            || !string.Equals(type, expectedType, StringComparison.Ordinal))
        {
            return $"Only {expectedType} particle effects are supported by this endpoint.";
        }

        if (!value.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object)
        {
            return "effect.config is required.";
        }

        var configValidationError = string.Equals(expectedType, LandingImpactType, StringComparison.Ordinal)
            ? ValidateLandingImpactConfig(config)
            : ValidateFootTrailConfig(config);
        if (configValidationError is not null)
        {
            return configValidationError;
        }

        effect = value;
        return null;
    }

    private static string? ValidateFootTrailConfig(JsonElement config)
    {
        var shapeError = ValidateShape(config);
        if (shapeError is not null)
        {
            return shapeError;
        }

        var colorError = ValidateRequiredColor(config, "primaryColor")
            ?? ValidateRequiredColor(config, "secondaryColor");
        if (colorError is not null)
        {
            return colorError;
        }

        var numberError =
            ValidateRequiredFiniteNumber(config, "sizeMin", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "sizeMax", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "endSizeMin", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "endSizeMax", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "lifeMinMs", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "lifeMaxMs", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "spawnRateMs", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "spawnCount", allowNegative: false)
            ?? ValidateOptionalFiniteNumber(config, "contactTolerancePx", allowNegative: false)
            ?? ValidateOptionalFiniteNumber(config, "emissionYOffsetPx", allowNegative: true);
        if (numberError is not null)
        {
            return numberError;
        }

        var offsetError = ValidateRequiredOffset(config, "leftOffset")
            ?? ValidateRequiredOffset(config, "rightOffset");
        if (offsetError is not null)
        {
            return offsetError;
        }

        if (config.TryGetProperty("whenTouching", out var whenTouching)
            && !IsOptionalNonEmptyString(whenTouching))
        {
            return "effect.config.whenTouching must be a non-empty string when provided.";
        }

        if (!TryGetRequiredString(config, "stepMode", out var stepMode)
            || !FootTrailStepModes.Contains(stepMode!))
        {
            return "effect.config.stepMode must be one of alternate, both, left, or right.";
        }

        return ValidateMinMaxPair(config, "sizeMin", "sizeMax")
            ?? ValidateMinMaxPair(config, "endSizeMin", "endSizeMax")
            ?? ValidateMinMaxPair(config, "lifeMinMs", "lifeMaxMs");
    }

    private static string? ValidateLandingImpactConfig(JsonElement config)
    {
        var shapeError = ValidateShape(config);
        if (shapeError is not null)
        {
            return shapeError;
        }

        var colorError = ValidateRequiredColor(config, "primaryColor")
            ?? ValidateRequiredColor(config, "secondaryColor");
        if (colorError is not null)
        {
            return colorError;
        }

        var numberError =
            ValidateRequiredFiniteNumber(config, "sizeMin", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "sizeMax", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "lifeMinMs", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "lifeMaxMs", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "burstCount", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "spreadAngleDeg", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "forceMin", allowNegative: false)
            ?? ValidateRequiredFiniteNumber(config, "forceMax", allowNegative: false)
            ?? ValidateOptionalFiniteNumber(config, "cameraShakeDurationMs", allowNegative: false)
            ?? ValidateOptionalFiniteNumber(config, "cameraShakeIntensity", allowNegative: false);
        if (numberError is not null)
        {
            return numberError;
        }

        var offsetError = ValidateRequiredOffset(config, "originOffset");
        if (offsetError is not null)
        {
            return offsetError;
        }

        if (config.TryGetProperty("whenLandingOn", out var whenLandingOn)
            && !IsOptionalNonEmptyString(whenLandingOn))
        {
            return "effect.config.whenLandingOn must be a non-empty tile id string when provided.";
        }

        return ValidateMinMaxPair(config, "sizeMin", "sizeMax")
            ?? ValidateMinMaxPair(config, "lifeMinMs", "lifeMaxMs")
            ?? ValidateMinMaxPair(config, "forceMin", "forceMax");
    }

    private static string? ValidateShape(JsonElement config)
    {
        if (!TryGetRequiredString(config, "shape", out var shape)
            || !ParticleShapes.Contains(shape!))
        {
            return "effect.config.shape must be one of circle, square, or diamond.";
        }

        return null;
    }

    private static string? ValidateRequiredColor(JsonElement config, string propertyName)
    {
        if (!TryGetRequiredString(config, propertyName, out var color)
            || !IsHexColor(color!))
        {
            return $"effect.config.{propertyName} must be a #RRGGBB color.";
        }

        return null;
    }

    private static string? ValidateRequiredFiniteNumber(
        JsonElement config,
        string propertyName,
        bool allowNegative)
    {
        if (!TryGetFiniteNumber(config, propertyName, out var value))
        {
            return $"effect.config.{propertyName} must be a finite number.";
        }

        if (!allowNegative && value < 0)
        {
            return $"effect.config.{propertyName} must be non-negative.";
        }

        return null;
    }

    private static string? ValidateOptionalFiniteNumber(
        JsonElement config,
        string propertyName,
        bool allowNegative)
    {
        if (!config.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        if (!TryGetFiniteNumber(config, propertyName, out var value))
        {
            return $"effect.config.{propertyName} must be a finite number.";
        }

        if (!allowNegative && value < 0)
        {
            return $"effect.config.{propertyName} must be non-negative.";
        }

        return null;
    }

    private static string? ValidateRequiredOffset(JsonElement config, string propertyName)
    {
        if (!config.TryGetProperty(propertyName, out var offset)
            || offset.ValueKind != JsonValueKind.Object)
        {
            return $"effect.config.{propertyName} must be an object with x and y numbers.";
        }

        if (!TryGetFiniteNumber(offset, "x", out _)
            || !TryGetFiniteNumber(offset, "y", out _))
        {
            return $"effect.config.{propertyName} must include finite x and y numbers.";
        }

        return null;
    }

    private static string? ValidateMinMaxPair(
        JsonElement config,
        string minPropertyName,
        string maxPropertyName)
    {
        if (!TryGetFiniteNumber(config, minPropertyName, out var min)
            || !TryGetFiniteNumber(config, maxPropertyName, out var max))
        {
            return null;
        }

        return min <= max
            ? null
            : $"effect.config.{minPropertyName} must be less than or equal to {maxPropertyName}.";
    }

    private static string GetActionName(string effectType)
        => string.Equals(effectType, LandingImpactType, StringComparison.Ordinal)
            ? nameof(GetLandingImpact)
            : nameof(GetFootTrail);

    private static string ToRouteSegment(string effectType)
        => string.Equals(effectType, LandingImpactType, StringComparison.Ordinal)
            ? "landing-impacts"
            : "foot-trails";

    private static string ToEffectLabel(string effectType)
        => string.Equals(effectType, LandingImpactType, StringComparison.Ordinal)
            ? "landing impact"
            : "foot trail";

    private static bool TryGetRequiredString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetFiniteNumber(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value)
            && !double.IsNaN(value)
            && !double.IsInfinity(value);
    }

    private static bool IsOptionalNonEmptyString(JsonElement property)
        => property.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            || (property.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.GetString()));

    private static bool IsHexColor(string value)
    {
        if (value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index += 1)
        {
            var character = value[index];
            var isHex =
                character is >= '0' and <= '9'
                || character is >= 'a' and <= 'f'
                || character is >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}
