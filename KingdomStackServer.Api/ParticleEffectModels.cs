using System.Text.Json;

namespace KingdomStackServer.Api;

public sealed record CreateParticleEffectRequest(
    string? Name,
    JsonElement? Effect);

public sealed record UpdateParticleEffectRequest(
    string? Name,
    string? ETag,
    JsonElement? Effect);

public sealed record ParticleEffectListResponse(
    string Type,
    string Prefix,
    int Count,
    string? ContinuationToken,
    ParticleEffectListItemResponse[] Items);

public sealed record ParticleEffectListItemResponse(
    string Id,
    string Name,
    string Type,
    string BlobPath,
    string LoadUrl,
    int SchemaVersion,
    int Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag);

public sealed record ParticleEffectResponse(
    string Id,
    string Name,
    string Type,
    string BlobPath,
    string LoadUrl,
    int SchemaVersion,
    int Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag,
    JsonElement Effect);

public sealed record StoredParticleEffectDocument(
    string Id,
    string Name,
    string Type,
    int SchemaVersion,
    int Version,
    JsonElement Effect);

public sealed record UserParticleEffectListResult(
    IReadOnlyList<UserParticleEffectListItem> Items,
    string? ContinuationToken);

public sealed record UserParticleEffectListItem(
    string Id,
    string Name,
    string Type,
    string BlobPath,
    int SchemaVersion,
    int Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag);

public sealed record UserParticleEffectContent(
    string Id,
    string Name,
    string Type,
    string BlobPath,
    int SchemaVersion,
    int Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag,
    JsonElement Effect);

public sealed record UserParticleEffectSaveResult(
    string Id,
    string Name,
    string Type,
    string BlobPath,
    int SchemaVersion,
    int Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag,
    JsonElement Effect);
