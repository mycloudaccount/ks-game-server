namespace KingdomStackServer.Api;

public sealed record UserGameSaveResult(
    string GameId,
    string Name,
    string BlobPath,
    string BlobUrl,
    DateTimeOffset? CreatedAt,
    DateTimeOffset LastModified,
    long ContentLength);
