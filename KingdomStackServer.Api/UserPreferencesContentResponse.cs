using System.Text.Json;

namespace KingdomStackServer.Api;

public sealed record UserPreferencesContentResponse(
    string BlobPath,
    long? ContentLength,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    JsonElement Preferences);
