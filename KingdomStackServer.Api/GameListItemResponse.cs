namespace KingdomStackServer.Api;

public sealed record GameListItemResponse(
    string GameId,
    string Name,
    string BlobPath,
    string DownloadUrl,
    string LoadUrl,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    long? ContentLength);
