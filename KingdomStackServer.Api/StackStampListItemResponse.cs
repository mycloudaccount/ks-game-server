namespace KingdomStackServer.Api;

public sealed record StackStampListItemResponse(
    string Id,
    string Name,
    string? Description,
    string BlobPath,
    string LoadUrl,
    string? PreviewUrl,
    int SchemaVersion,
    int Version,
    string[] Tags,
    StackStampFootprintResponse Footprint,
    int EntryCount,
    string[] TileReferences,
    string[] CharacterReferences,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag);
