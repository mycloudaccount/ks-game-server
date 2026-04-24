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
    string? StoredFormat,
    string? StoredSchema,
    int? StoredVersion,
    JsonElement? Document,
    JsonElement? Game);
