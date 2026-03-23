namespace KingdomStackServer.Api;

public sealed record UserPreferencesResponse(
    string BlobPath,
    long? ContentLength,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string Url);
