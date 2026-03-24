namespace KingdomStackServer.Api;

public sealed record UserStackStampListItem(
    string Id,
    string Name,
    string? Description,
    string BlobPath,
    bool HasPreview,
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
