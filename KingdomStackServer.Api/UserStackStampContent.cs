namespace KingdomStackServer.Api;

public sealed record UserStackStampContent(
    string Id,
    string Name,
    string? Description,
    string BlobPath,
    bool HasPreview,
    int SchemaVersion,
    int Version,
    string[] Tags,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag,
    StackStampDefinitionDto Definition);
