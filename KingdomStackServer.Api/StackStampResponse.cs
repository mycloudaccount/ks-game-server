namespace KingdomStackServer.Api;

public sealed record StackStampResponse(
    string Id,
    string Name,
    string? Description,
    string BlobPath,
    string LoadUrl,
    string? PreviewUrl,
    int SchemaVersion,
    int Version,
    string[] Tags,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag,
    StackStampDefinitionDto Definition);
