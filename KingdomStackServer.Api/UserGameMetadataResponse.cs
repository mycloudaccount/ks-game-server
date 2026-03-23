namespace KingdomStackServer.Api;

public sealed record UserGameMetadataResponse(
    string GameId,
    string Name,
    string BlobPath,
    string DownloadUrl,
    string LoadUrl,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    long? ContentLength);
