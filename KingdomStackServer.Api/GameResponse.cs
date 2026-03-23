using System.Text.Json;

namespace KingdomStackServer.Api;

public sealed record GameResponse(
    string GameId,
    string Name,
    string BlobPath,
    long? ContentLength,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string DownloadUrl,
    string LoadUrl,
    JsonElement Game);
