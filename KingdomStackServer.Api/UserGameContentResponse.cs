using System.Text.Json;

namespace KingdomStackServer.Api;

public sealed record UserGameContentResponse(
    string GameId,
    string Name,
    string BlobPath,
    long? ContentLength,
    DateTimeOffset? CreatedAt,
    string DownloadUrl,
    string LoadUrl,
    DateTimeOffset? LastModified,
    JsonElement Game);
